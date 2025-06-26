
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Util;
using UnityEngine;

namespace LibConstruct
{
  public abstract partial class PlacementBoard
  {
    private static long NextID;

    protected List<HostPair> Hosts = new();
    protected List<BoxCollider> Colliders = new();
    public List<IPlacementBoardStructure> Structures = new();
    // use SortedDictionary since Grid3 doesn't hash well
    protected SortedDictionary<Grid3, BoardCell> Cells = new();
    // ID is only used to remerge boards on load
    public long ID;
    public Vector3 PositionOffset = Vector3.zero;
    public Quaternion RotationOffset = Quaternion.identity;

    public Vector3 OriginPosition => this.Origin.position + this.PositionOffset;
    public Quaternion OriginRotation => this.Origin.rotation * this.RotationOffset;

    public Transform Origin => this.Hosts.Count > 0 ? this.Hosts[0].Origin : null;
    public IPlacementBoardHost PrimaryHost => this.Hosts.Count > 0 ? this.Hosts[0].Host : null;

    public PlacementBoard()
    {
      this.ID = NextID++;
    }

    public virtual PlacementBoardSaveData SerializeSave()
    {
      var saveData = new PlacementBoardSaveData();
      this.InitializeSaveData(ref saveData);
      return saveData;
    }
    protected virtual void InitializeSaveData(ref PlacementBoardSaveData saveData)
    {
      saveData.PositionOffset = this.PositionOffset;
      saveData.RotationOffset = this.RotationOffset;
    }
    public virtual void DeserializeSave(PlacementBoardSaveData saveData)
    {
      this.PositionOffset = saveData.PositionOffset;
      this.RotationOffset = saveData.RotationOffset;
      // swap the zero quaternion (if missing from save) with identity
      if (this.RotationOffset == default)
        this.RotationOffset = Quaternion.identity;
    }

    public virtual void SerializeOnJoin(RocketBinaryWriter writer) { }
    public virtual void DeserializeOnJoin(RocketBinaryReader reader) { }
    public virtual void BuildUpdate(RocketBinaryWriter writer) { }
    public virtual void ProcessUpdate(RocketBinaryReader reader) { }

    public void AddHost(IPlacementBoardHost host, Transform origin)
    {
      if (this.Hosts.Any(v => v.Host == host))
        return;
      this.Hosts.Add(new(host, origin));
      foreach (var collider in host.CollidersForBoard(this))
        this.AddCollider(collider);
    }

    private void AddCollider(BoxCollider collider)
    {
      if (this.Colliders.Contains(collider))
        return;
      this.Colliders.Add(collider);
      BoardColliderLookup[collider] = this;
      foreach (var cell in this.BoundsCells(collider.transform, ColliderLocalBounds(collider), true))
        cell.ColliderRef();
    }

    public void RemoveHost(IPlacementBoardHost host)
    {
      var idx = this.Hosts.FindIndex(v => v.Host == host);
      if (idx == -1)
        return;
      var oldOrigin = this.Hosts[idx].Origin;

      var removedStructures = new HashSet<IPlacementBoardStructure>();
      foreach (var collider in host.CollidersForBoard(this))
        this.RemoveCollider(collider, removedStructures);
      if (removedStructures.Count != 0)
        this.RemoveOrphanedStructures(host, removedStructures.ToList());

      var originPos = this.OriginPosition;
      var originRotation = this.OriginRotation;

      this.Hosts.RemoveAt(idx);
      if (idx != 0 || this.PrimaryHost == null)
        return;
      // if this was the primary host (and there is any host left), need to reparent everything and adjust origin offset
      this.PositionOffset = originPos - this.Origin.position;
      this.RotationOffset = originRotation * Quaternion.Inverse(this.Origin.rotation);
      foreach (var structure in this.Structures)
      {
        structure.Transform.SetParent(this.Origin, worldPositionStays: true);
      }
    }

    private void RemoveCollider(BoxCollider collider, HashSet<IPlacementBoardStructure> removedStructures)
    {
      if (!this.Colliders.Contains(collider))
        return;
      this.Colliders.Remove(collider);
      BoardColliderLookup.Remove(collider);
      var toRemove = new HashSet<IPlacementBoardStructure>();
      foreach (var cell in this.ValidBoundsCells(collider.transform, ColliderLocalBounds(collider)))
      {
        if (!cell.ColliderDeref())
        {
          if (cell.Structure != null)
            removedStructures.Add(cell.Structure);
          this.Cells.Remove(cell.Position);
        }
      }
    }

    public void Register(IPlacementBoardStructure boardStructure)
    {
      if (boardStructure.Board != null || this.Structures.Contains(boardStructure) || boardStructure is not Structure structure)
        return;
      boardStructure.Board = this;
      var grid = this.WorldToGrid(boardStructure.Transform.position);
      boardStructure.Transform.SetParent(this.Origin);
      boardStructure.Transform.position = this.GridToWorld(grid);
      var cells = this.ValidBoundsCells(boardStructure.Transform, structure.Bounds).ToArray();
      foreach (var cell in cells)
        cell.Structure = boardStructure;
      boardStructure.BoardCells = cells;
      this.Structures.Add(boardStructure);
      this.OnStructureRegistered(boardStructure);
      foreach (var host in this.Hosts)
        host.Host.OnBoardStructureRegistered(this, boardStructure);
    }

    public void Deregister<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      if (structure.Board != this || !this.Structures.Contains(structure))
        return;
      this.OnStructureDeregistered(structure);
      foreach (var (host, _) in this.Hosts)
        host.OnBoardStructureDeregistered(this, structure);
      foreach (var cell in structure.BoardCells)
        cell.Structure = null;
      structure.BoardCells = null;
      this.Structures.Remove(structure);
    }

    // These are overridable methods to handle structures being added/removed
    // OnStructureRegistered is called *after* the structure is added and its cells registered
    // OnStructureDeregistered is called *before* the structure is removed and its cells cleared
    public virtual void OnStructureRegistered(IPlacementBoardStructure structure) { }
    public virtual void OnStructureDeregistered(IPlacementBoardStructure structure) { }

    // Called when a structure is relocated *after* the structure is registered to its new cells
    public virtual void OnStructureRelocated(IPlacementBoardRelocatable structure) { }

    private static Bounds ColliderLocalBounds(BoxCollider collider) => new(collider.center, collider.size);

    public IEnumerable<Grid3> BoundsGrids(Transform transform, Bounds bounds)
    {
      var ptA = this.WorldToGrid(transform.TransformPoint(bounds.min));
      var ptB = this.WorldToGrid(transform.TransformPoint(bounds.max));
      var minpt = new Grid3() { x = Math.Min(ptA.x, ptB.x), y = Math.Min(ptA.y, ptB.y) };
      var maxpt = new Grid3() { x = Math.Max(ptA.x, ptB.x), y = Math.Max(ptA.y, ptB.y) };
      for (var x = minpt.x; x <= maxpt.x; x += 10)
        for (var y = minpt.y; y <= maxpt.y; y += 10)
        {
          var grid = new Grid3() { x = x, y = y, z = 0 };
          var local = transform.InverseTransformPoint(this.GridToWorld(grid));
          local.z = bounds.center.z; // we only care about xy bounds, so set z to be always inside
          if (bounds.Contains(local))
            yield return grid;
        }
    }

    public IEnumerable<BoardCell> ValidBoundsCells(Transform transform, Bounds bounds)
    {
      foreach (var cell in this.BoundsCells(transform, bounds, create: false))
        if (cell != null)
          yield return cell;
    }

    public IEnumerable<BoardCell> BoundsCells(Transform transform, Bounds bounds, bool create = false)
    {
      foreach (var grid in this.BoundsGrids(transform, bounds))
        yield return this.GetCell(grid, create);
    }

    public BoardCell GetCell(Grid3 position, bool create = false)
    {
      if (this.Cells.TryGetValue(position, out var cell) || !create)
        return cell;
      cell = new BoardCell(this, position);
      this.Cells[position] = cell;
      return cell;
    }

    public Grid3 WorldToGrid(Vector3 world)
    {
      var originRot = this.OriginRotation;
      world -= this.OriginPosition;
      var gridVec = new Vector3(
        Vector3.Dot(world, originRot * Vector3.right),
        Vector3.Dot(world, originRot * Vector3.up)
      ) / this.GridSize;

      return new Grid3(gridVec.Round());
    }

    public Vector3 GridToWorld(Grid3 grid)
    {
      var vecGrid = grid.ToVector3();
      var originRot = this.OriginRotation;
      var right = originRot * Vector3.right;
      var up = originRot * Vector3.up;
      var forward = originRot * Vector3.forward;
      return this.OriginPosition + (vecGrid.x * right + vecGrid.y * up + vecGrid.z * forward) * this.GridSize;
    }

    public int RotationToIndex(Quaternion rotation)
    {
      var relativeRot = rotation * Quaternion.Inverse(this.OriginRotation);
      relativeRot.ToAngleAxis(out var relativeAngle, out _);
      return Mathf.RoundToInt(relativeAngle / 90f);
    }

    public Quaternion IndexToRotation(int index)
    {
      return this.OriginRotation * Quaternion.AngleAxis(90f * index, Vector3.forward);
    }

    public abstract IPlacementBoardStructure EquivalentStructure(Structure structure);
    public abstract float GridSize { get; }

    public class BoardCell
    {
      public readonly PlacementBoard Board;
      public readonly Grid3 Position;
      public IPlacementBoardStructure Structure;

      public BoardCell(PlacementBoard board, Grid3 position)
      {
        this.Board = board;
        this.Position = position;
      }

      // keep a count of how many colliders include this cell so we don't remove cells early when bounds overlap
      private int refs;
      public void ColliderRef() => this.refs++;
      public bool ColliderDeref()
      {
        this.refs--;
        return this.refs > 0;
      }
    }

    protected struct HostPair
    {
      public IPlacementBoardHost Host;
      public Transform Origin;

      public HostPair(IPlacementBoardHost host, Transform origin)
      {
        this.Host = host;
        this.Origin = origin;
      }

      public void Deconstruct(out IPlacementBoardHost host, out Transform origin)
      {
        (host, origin) = (this.Host, this.Origin);
      }
    }
  }
}