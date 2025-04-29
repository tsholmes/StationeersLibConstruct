
using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Util;
using UnityEngine;

namespace LibConstruct
{
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

    private static long NextID;
    public static bool Loading;
    private static Dictionary<long, IBoardRef> LoadingRefs = new();
    public static BoardRef<T> LoadRef<T>(long id, long primaryHostId) where T : PlacementBoard, new()
    {
      if (!Loading)
      {
        // if we aren't loading, this is being called for a newly created object on a client.
        // in that case the primary host should already have the board created
        return new BoardRef<T> { Board = (T)FindExisting(id, primaryHostId) };
      }
      if (id >= NextID)
        NextID = id + 1;
      if (!LoadingRefs.TryGetValue(id, out var iref))
      {
        iref = new BoardRef<T>();
        LoadingRefs[id] = iref;
      }
      return (BoardRef<T>)iref;
    }

    public static PlacementBoard FindExisting(long id, long primaryHostId)
    {
      var host = Thing.Find<IPlacementBoardHost>(primaryHostId);
      foreach (var board in host.GetPlacementBoards())
        if (board?.ID == id)
          return board;
      throw new Exception($"could not find PlacementBoard {id} on host {primaryHostId}");
    }

    public static void RegisterLoading(IPlacementBoardStructure structure, long boardID, long primaryHostId)
    {
      if (Loading)
      {
        // board elements are ordered after the primary host so the ref should always be here
        var iref = LoadingRefs[boardID];
        iref.Board.AwaitingRegister.Add(structure);
      }
      else
      {
        // if we aren't loading, this is a newly created object on a client and we should just register it directly
        var board = FindExisting(boardID, primaryHostId);
        board.Register(structure);
      }
    }

    public static void StartLoad()
    {
      LoadingRefs.Clear();
      Loading = true;
    }
    public static void FinishLoad()
    {
      foreach (var iref in LoadingRefs.Values)
      {
        foreach (var structure in iref.Board.AwaitingRegister)
          iref.Board.Register(structure);
        iref.Board.AwaitingRegister.Clear();
      }
      LoadingRefs.Clear();
      Loading = false;
    }

    protected List<IPlacementBoardHost> Hosts = new();
    protected List<BoxCollider> Colliders = new();
    public List<IPlacementBoardStructure> Structures = new();
    // use SortedDictionary since Grid3 doesn't hash well
    protected SortedDictionary<Grid3, BoardCell> Cells = new();
    private List<IPlacementBoardStructure> AwaitingRegister = new();
    // ID is only used to remerge boards on load
    public long ID;
    public Transform Origin;

    public IPlacementBoardHost PrimaryHost => this.Hosts.Count > 0 ? this.Hosts[0] : null;

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
    protected virtual void InitializeSaveData(ref PlacementBoardSaveData saveData) { }
    public virtual void DeserializeSave(PlacementBoardSaveData saveData) { }

    public virtual void SerializeOnJoin(RocketBinaryWriter writer) { }
    public virtual void DeserializeOnJoin(RocketBinaryReader reader) { }
    public virtual void BuildUpdate(RocketBinaryWriter writer) { }
    public virtual void ProcessUpdate(RocketBinaryReader reader) { }

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
      var removedStructures = new HashSet<IPlacementBoardStructure>();
      foreach (var collider in host.CollidersForBoard(this))
        this.RemoveCollider(collider, removedStructures);
      if (removedStructures.Count != 0)
        this.RemoveOrphanedStructures(host, removedStructures.ToList());
      // TODO: if this is the primary host, need to reparent everything and adjust origin offset
      this.Hosts.Remove(host);
    }

    private void RemoveCollider(BoxCollider collider, HashSet<IPlacementBoardStructure> removedStructures)
    {
      if (!this.Colliders.Contains(collider))
        return;
      this.Colliders.Remove(collider);
      BoardColliderLookup.Remove(collider);
      var toRemove = new HashSet<IPlacementBoardStructure>();
      foreach (var cell in this.BoundsCells(collider.transform, ColliderLocalBounds(collider), false))
      {
        if (!cell.ColliderDeref())
        {
          if (cell.Structure != null)
            removedStructures.Add(cell.Structure);
          this.Cells.Remove(cell.Position);
        }
      }
    }

    // This is called when removing a host also removes cells containing board structures.
    // This should always either destroy the board structures, or deregister and reregister them in a different spot
    protected virtual void RemoveOrphanedStructures(IPlacementBoardHost removedHost, List<IPlacementBoardStructure> removedStructures)
    {
      var hostStruct = (Structure)removedHost;
      DeconstructToKits(hostStruct.Transform.position, removedStructures);
      DestroyStructureList(removedStructures);
    }

    // Helper function to call during RemoveOrphanedStructures.
    // Walks the build states of the removed structures to count all the entry tools, and spawns in combined stacks of kits at the specified location
    public static void DeconstructToKits(Vector3 kitSpawnLocation, List<IPlacementBoardStructure> removedStructures)
    {
      if (!GameManager.RunSimulation)
        return;
      var buildKits = new Dictionary<int, int>(); // prefab hash -> count
      var kitParents = new Dictionary<int, Structure>(); // prefab hash -> count
      void addKit(int hash, int count, Structure first)
      {
        if (kitParents.ContainsKey(hash))
          buildKits[hash] += count;
        else
        {
          kitParents[hash] = first;
          buildKits[hash] = count;
        }
      }
      foreach (var boardStruct in removedStructures)
      {
        var structure = (Structure)boardStruct;
        for (var i = structure.CurrentBuildStateIndex; i >= 0; i--)
        {
          var buildState = structure.BuildStates[i];
          var entryTool = buildState.Tool.ToolEntry;
          var entryTool2 = buildState.Tool.ToolEntry2;
          if (entryTool != null && entryTool is not Tool)
            addKit(entryTool.PrefabHash, buildState.Tool.EntryQuantity, structure);
          if (entryTool2 != null && entryTool2 is not Tool)
            addKit(entryTool2.PrefabHash, buildState.Tool.EntryQuantity2, structure);
        }
      }

      foreach (var hash in buildKits.Keys)
      {
        var parent = kitParents[hash];
        var count = buildKits[hash];

        var toolUse = new ToolUse
        {
          ToolEntry = Prefab.Find<Item>(hash),
          EntryQuantity = count,
        };

        var eventInstance = new ConstructionEventInstance
        {
          Position = kitSpawnLocation,
          Rotation = Quaternion.identity,
          Parent = parent,
        };

        toolUse.Deconstruct(eventInstance);
      }
    }

    // Helper function to call during RemoveOrphanedStructures.
    // Destroys the list of structures
    public static void DestroyStructureList(List<IPlacementBoardStructure> removedStructures)
    {
      if (!GameManager.RunSimulation)
        return;
      foreach (var structure in removedStructures)
        OnServer.Destroy((Thing)structure);
    }

    public void Register(IPlacementBoardStructure boardStructure)
    {
      if (boardStructure.Board != null || this.Structures.Contains(boardStructure) || boardStructure is not Structure structure)
        return;
      boardStructure.Board = this;
      var grid = this.WorldToGrid(boardStructure.Transform.position);
      boardStructure.Transform.SetParent(this.Origin);
      boardStructure.Transform.position = this.GridToWorld(grid);
      var cells = this.BoundsCells(boardStructure.Transform, structure.Bounds).ToArray();
      foreach (var cell in cells)
        cell.Structure = boardStructure;
      boardStructure.BoardCells = cells;
      this.Structures.Add(boardStructure);
      this.OnStructureRegistered(boardStructure);
      foreach (var host in this.Hosts)
        host.OnBoardStructureRegistered(this, boardStructure);
    }

    public void Deregister<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      if (structure.Board != this || !this.Structures.Contains(structure))
        return;
      this.OnStructureDeregistered(structure);
      foreach (var host in this.Hosts)
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
      // TODO: just InverseTransform to Origin space (with offset)
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
    public abstract float GridSize { get; }

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

      var create = new CreateBoardStructureInstance(
        constructor,
        prefab,
        CursorBoard,
        CursorBoard.WorldToGrid(targetLocation),
        targetRotation,
        steamId,
        authoringMode
      );
      if (constructor.PaintableMaterial != null && prefabStructure.PaintableMaterial != null)
        create.CustomColor = constructor.CustomColor.Index;

      if (GameManager.RunSimulation)
        CreateBoardStructure(create);
      else
        NetworkClient.SendToServer(new CreateBoardStructureMessage(create));
    }

    public static void CreateBoardStructure(CreateBoardStructureInstance create)
    {
      var prefabStructure = (Structure)create.StructurePrefab;
      var entryQuantity = prefabStructure.BuildStates[0].Tool.EntryQuantity;
      if (!create.AuthoringMode && !create.Constructor.OnUseItem(entryQuantity, null))
        return;

      var structure = Thing.Create<IPlacementBoardStructure>((Structure)create.StructurePrefab, create.WorldPosition, create.Rotation);
      structure.SetStructureData(create.Rotation, create.OwnerClientId, create.Position, create.CustomColor);
      create.Board.Register(structure);
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