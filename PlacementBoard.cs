
using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
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

    public abstract PlacementBoardStructure EquivalentStructure(Structure structure);
  }
}