using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LibConstruct
{
  public abstract partial class PlacementBoard
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

      UpdateRelocation();
    }

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
        CursorBoard.RotationToIndex(targetRotation),
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

      var structure = Thing.Create<IPlacementBoardStructure>((Structure)create.StructurePrefab, create.WorldPosition, create.WorldRotation);
      structure.SetStructureData(create.WorldRotation, create.OwnerClientId, create.Position, create.CustomColor);
      create.Board.Register(structure);
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
      // unparent the structures from the board so the gameobjects aren't destroyed when the parent is
      foreach (var structure in removedStructures)
        structure.Transform.SetParent(null);
      if (!GameManager.RunSimulation)
        return;
      foreach (var structure in removedStructures)
        OnServer.Destroy((Thing)structure);
    }
  }
}