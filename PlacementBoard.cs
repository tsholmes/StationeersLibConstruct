
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

    public static bool IsRelocating => RelocatingStructure != null;
    public static IPlacementBoardRelocatable RelocatingStructure;
    public static IPlacementBoardRelocatable RelocatingCursor;
    public static Func<bool> RelocatingContinue;
    // since relocate works differently than other interactions, we need to explicitly wait for the mouse to be released before the next action
    public static bool RelocateMouseReleased;

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

      UpdateRelocation();
    }

    public static void StartRelocate(IPlacementBoardRelocatable structure, Func<bool> continueRelocating = null)
    {
      // clean up any previous relocations
      CancelRelocate();

      var board = structure.Board;
      // convert rotation back to 0-3 rotation number so we start at the same rotation
      var relativeRot = structure.Transform.rotation * Quaternion.Inverse(board.Origin.rotation);
      relativeRot.ToAngleAxis(out var relativeAngle, out _);
      CursorRotation = Mathf.RoundToInt(relativeAngle / 90f);

      InventoryManagerPatch.constructionCursors.TryGetValue(structure.name, out var cursor);
      if (cursor is not IPlacementBoardRelocatable relocCursor)
      {
        Debug.LogWarning($"cursor not found for {structure.name}");
        return;
      }
      RelocatingCursor = relocCursor;

      RelocatingStructure = structure;
      RelocatingContinue = continueRelocating;
      RelocateMouseReleased = false;
    }

    public static void CancelRelocate()
    {
      RelocatingStructure = null;
      if (RelocatingCursor != null)
        RelocatingCursor.GetAsThing.gameObject.SetActive(false);
      RelocatingCursor = null;
      RelocatingContinue = null;
    }

    public static void FinishRelocate()
    {
      RelocateMouseReleased = false;

      RelocateOnServer(new RelocateBoardStructureInstance(
        RelocatingStructure,
        CursorGrid,
        CursorRotation
      ));

      CancelRelocate();
    }

    public static void RelocateOnServer(RelocateBoardStructureInstance relocate)
    {
      if (GameManager.RunSimulation)
        RelocateBoardStructure(relocate);
      else
      {
        // on a client we just send the message to the server and actually perform the relocate when it echoes it back
        new RelocateBoardStructureMessage(relocate).SendToServer();
      }
    }

    public static void RelocateBoardStructure(RelocateBoardStructureInstance relocate)
    {
      var structure = relocate.Structure;
      var board = structure.Board;

      var pos = board.GridToWorld(relocate.Position);
      var rot = board.OriginRotation * Quaternion.AngleAxis(90f * relocate.Rotation, Vector3.forward);

      structure.Transform.SetPositionAndRotation(pos, rot);

      // unregister from previous cells
      foreach (var cell in structure.BoardCells)
        cell.Structure = null;

      // register to new cells
      var cells = board.BoundsCells(structure.Transform, structure.AsStructure.Bounds).ToArray();
      foreach (var cell in cells)
        cell.Structure = structure;
      structure.BoardCells = cells;

      // update structure registration so it saves in the right spot
      structure.AsStructure.RegisteredPosition = pos;
      structure.AsStructure.RegisteredRotation = rot;

      board.OnStructureRelocated(structure);
      structure.OnStructureRelocated();

      if (GameManager.RunSimulation && NetworkServer.HasClients())
        new RelocateBoardStructureMessage(relocate).SendToClients();
    }

    public static Thing.DelayedActionInstance RelocateAction()
    {
      var action = new Thing.DelayedActionInstance
      {
        ActionMessage = "Relocate", // TODO: localize,
        Duration = 0f,
        Selection = RelocatingCursor.GetAsThing.GetSelection(),
      };
      if (KeyManager.GetButtonDown(KeyMap.QuantityModifier))
        CursorRotation = (CursorRotation + 1) % 4;

      var canConstruct = RelocatingCursor.AsStructure.CanConstruct();
      if (!canConstruct.CanConstruct)
        return action.Fail(canConstruct.ErrorMessage);

      return action.Succeed();
    }

    private static void UpdateRelocation()
    {
      if (!KeyManager.GetMouse("Primary"))
        RelocateMouseReleased = true;
      if (RelocatingStructure == null)
          return;
      if (RelocatingContinue != null && !RelocatingContinue())
      {
        CancelRelocate();
        return;
      }

      var pointingAtBoard = CursorBoard == RelocatingStructure.Board;
      RelocatingCursor.GetAsThing.gameObject.SetActive(pointingAtBoard);

      RelocatingCursor.Board = pointingAtBoard ? RelocatingStructure.Board : null;

      if (!pointingAtBoard)
        return;

      var rot = CursorBoard.OriginRotation * Quaternion.AngleAxis(90f * CursorRotation, Vector3.forward);
      RelocatingCursor.Transform.SetPositionAndRotation(
        CursorBoard.GridToWorld(CursorGrid),
        rot
      );

      var cursorStructure = RelocatingCursor.AsStructure;
      var valid = cursorStructure.CanConstruct().CanConstruct;
      var color = valid ? Color.green : Color.red;
      color.a = InventoryManager.Instance.CursorAlphaConstructionMesh;

      // copied from end of InventoryManager.PlacementMode
      if (cursorStructure.Wireframe)
        cursorStructure.Wireframe.BlueprintRenderer.material.color = color;
      else
      {
        foreach (var renderer in cursorStructure.Renderers)
          if (renderer.HasRenderer())
            renderer.SetColor(color);
      }
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

    protected List<HostPair> Hosts = new();
    protected List<BoxCollider> Colliders = new();
    public List<IPlacementBoardStructure> Structures = new();
    // use SortedDictionary since Grid3 doesn't hash well
    protected SortedDictionary<Grid3, BoardCell> Cells = new();
    private List<IPlacementBoardStructure> AwaitingRegister = new();
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
        host.Host.OnBoardStructureRegistered(this, boardStructure);
    }

    public void Deregister<T>(T structure) where T : Structure, IPlacementBoardStructure
    {
      if (structure.Board != this || !this.Structures.Contains(structure))
        return;
      this.OnStructureDeregistered(structure);
      foreach (var host in this.Hosts)
        host.Host.OnBoardStructureDeregistered(this, structure);
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

    protected struct HostPair
    {
      public IPlacementBoardHost Host;
      public Transform Origin;

      public HostPair(IPlacementBoardHost host, Transform origin)
      {
        this.Host = host;
        this.Origin = origin;
      }
    }
  }
}