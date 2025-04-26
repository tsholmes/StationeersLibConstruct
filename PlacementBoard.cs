
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Util;
using UnityEngine;

namespace LibConstruct
{
  // TODO: IReferencable?
  public abstract class PlacementBoard
  {
    private static float MaxCursorDistance = (float)Math.Sqrt(8f) * 0.6f;

    public static readonly Dictionary<BoxCollider, PlacementBoard> BoardColliderLookup = new();
    public static PlacementBoard CursorBoard;
    public static Grid3 CursorGrid;
    public static int CursorRotation;
    public static bool PlacingOnBoard = false;

    public static void FindCursorBoard()
    {
      CursorBoard = null;
      if (CursorManager.Instance.CursorTargetCollider is BoxCollider boxCollider)
      {
        BoardColliderLookup.TryGetValue(
          boxCollider,
          out CursorBoard
        );
        if (CursorBoard != null && CursorManager.CursorHit.distance > MaxCursorDistance)
          CursorBoard = null;
      }
      if (CursorBoard == null && CursorManager.CursorThing is IPlacementBoardStructure boardStruct)
        CursorBoard = boardStruct.Board;
      if (CursorBoard != null)
        CursorGrid = CursorBoard.WorldToGrid(CursorManager.CursorHit.point);
    }

    protected List<IPlacementBoardHost> Hosts = new();
    protected List<BoxCollider> Colliders = new();
    // use SortedDictionary since Grid3 doesn't hash well
    protected SortedDictionary<Grid3, BoardCell> Cells = new();
    public Transform Origin;
    public float GridSize;

    public void AddHost(IPlacementBoardHost host)
    {
      if (this.Hosts.Contains(host))
        return;
      foreach (var collider in host.CollidersForBoard(this))
        this.AddCollider(collider);
      this.Hosts.Add(host);
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
      if (!this.Hosts.Contains(host))
        return;
      foreach (var collider in host.CollidersForBoard(this))
        this.RemoveCollider(collider);
      this.Hosts.Remove(host);
    }

    private void RemoveCollider(BoxCollider collider)
    {
      if (!this.Colliders.Contains(collider))
        return;
      this.Colliders.Remove(collider);
      BoardColliderLookup.Remove(collider);
      foreach (var cell in this.BoundsCells(collider.transform, ColliderLocalBounds(collider), false))
      {
        if (!cell.ColliderDeref())
        {
          // TODO: destroy cell structure?
          this.Cells.Remove(cell.Position);
        }
      }
    }

    private static Bounds ColliderLocalBounds(BoxCollider collider) => new Bounds(collider.center, collider.size);

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
      world -= this.Origin.position;
      var gridVec = new Vector3(
        Vector3.Dot(world, this.Origin.right),
        Vector3.Dot(world, this.Origin.up)
      ) / this.GridSize;

      return new Grid3(gridVec.Round());
    }

    public Vector3 GridToWorld(Grid3 grid)
    {
      var vecGrid = grid.ToVector3();
      return this.Origin.position + (vecGrid.x * this.Origin.right + vecGrid.y * this.Origin.up + vecGrid.z * this.Origin.forward) * this.GridSize;
    }

    public abstract IPlacementBoardStructure EquivalentStructure(Structure structure);

    public static void UseMultiConstructorBoard(Thing player, int activeHandSlotId, Vector3 targetLocation, Quaternion targetRotation, bool authoringMode, ulong steamId, Thing spawnPrefab)
    {
      var constructor = player.Slots[activeHandSlotId].Get<MultiConstructor>();
      if (!constructor)
        constructor = Prefab.Find<MultiConstructor>(spawnPrefab.PrefabHash);
      if (!constructor)
        return;

      var prefabStructure = Prefab.Find<Structure>(InventoryManager.ConstructionCursor.PrefabHash);
      if (prefabStructure is not IPlacementBoardStructure prefab)
        return;

      var entryQuantity = prefabStructure.BuildStates[0].Tool.EntryQuantity;
      if (!authoringMode && !constructor.OnUseItem(entryQuantity, null))
        return;

      var create = new CreateBoardStructureInstance(prefab, CursorBoard, CursorBoard.WorldToGrid(targetLocation), targetRotation, steamId);
      if (constructor.PaintableMaterial != null && prefabStructure.PaintableMaterial != null)
        create.CustomColor = constructor.CustomColor.Index;

      if (GameManager.RunSimulation)
      {
        var structure = Thing.Create<IPlacementBoardStructure>((Structure)create.Prefab, create.WorldPosition, create.Rotation);
        var thing = (Thing)structure;
        structure.Board = create.Board;
        structure.BoardCells = create.Board.BoundsCells(thing.Transform, thing.Bounds).ToArray();
        foreach (var cell in structure.BoardCells)
        {
          cell.Structure = structure;
        }
        structure.SetStructureData(create.Rotation, create.OwnerClientId, create.Position, create.CustomColor);
        // TODO: structure.OnBoardRegistered?
      }
      else
      {
        // TODO: networking
      }
    }

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
  }
}