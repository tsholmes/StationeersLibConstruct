
using System;
using System.Collections.Generic;
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
    public static bool PlacingOnBoard = false;

    public static void FindCursorBoard()
    {
      if (CursorManager.Instance.CursorTargetCollider is BoxCollider boxCollider)
      {
        BoardColliderLookup.TryGetValue(
          boxCollider,
          out var cursorBoard
        );
        if (cursorBoard != null && CursorManager.CursorHit.distance < MaxCursorDistance)
        {
          CursorBoard = cursorBoard;
          CursorGrid = cursorBoard.WorldToGrid(CursorManager.CursorHit.point);
        }
        else
          CursorBoard = null;
      }
      else
        CursorBoard = null;
    }

    protected List<IPlacementBoardHost> Hosts = new();
    protected List<BoxCollider> Colliders = new();
    public Transform Origin;
    public float GridSize;

    public void AddHost(IPlacementBoardHost host)
    {
      if (this.Hosts.Contains(host))
        return;
      foreach (var collider in host.CollidersForBoard(this))
      {
        if (this.Colliders.Contains(collider))
          continue;
        this.Colliders.Add(collider);
        BoardColliderLookup[collider] = this;
        // TODO: map collider to grid locations and register
      }
      this.Hosts.Add(host);
    }

    public void RemoveHost(IPlacementBoardHost host)
    {
      if (!this.Hosts.Contains(host))
        return;
      foreach (var collider in host.CollidersForBoard(this))
      {
        if (!this.Colliders.Contains(collider))
          continue;
        // TODO: map collider to grid locations and deregister (possibly removing built grid items)
        this.Colliders.Remove(collider);
        BoardColliderLookup.Remove(collider);
      }
      this.Hosts.Remove(host);
    }

    public Grid3 WorldToGrid(Vector3 world)
    {
      world -= this.Origin.position;
      var gridVec = new Vector3(
        Vector3.Dot(world, this.Origin.right),
        Vector3.Dot(world, this.Origin.up),
        Vector3.Dot(world, this.Origin.forward)
      ) / this.GridSize;

      return new Grid3(gridVec.Round()) { z = 0 };
    }

    public Vector3 GridToWorld(Grid3 grid)
    {
      var vecGrid = grid.ToVector3();
      return this.Origin.position + (vecGrid.x * this.Origin.right + vecGrid.y * this.Origin.up + vecGrid.z * this.Origin.forward) * this.GridSize;
    }

    public abstract IPlacementBoardStructure EquivalentStructure(Structure structure);

    public static void UseMultiConstructorBoard(Thing player, int activeHandSlotId, int inactiveHandSlotId, Vector3 targetLocation, Quaternion targetRotation, bool authoringMode, ulong steamId, Thing spawnPrefab)
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
        structure.Board = create.Board;
        structure.SetStructureData(create.Rotation, create.OwnerClientId, create.Position, create.CustomColor);
      }
      else
      {
        // TODO: networking
      }
    }
  }
}