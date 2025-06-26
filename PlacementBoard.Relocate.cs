using System;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;

namespace LibConstruct
{
  public abstract partial class PlacementBoard
  {
    public static bool IsRelocating => RelocatingStructure != null;
    public static IPlacementBoardRelocatable RelocatingStructure;
    public static IPlacementBoardRelocatable RelocatingCursor;
    public static Func<bool> RelocatingContinue;
    // since relocate works differently than other interactions, we need to explicitly wait for the mouse to be released before the next action
    public static bool RelocateMouseReleased;

    public static void StartRelocate(IPlacementBoardRelocatable structure, Func<bool> continueRelocating = null)
    {
      // clean up any previous relocations
      CancelRelocate();

      var board = structure.Board;
      // convert rotation back to 0-3 rotation number so we start at the same rotation
      CursorRotation = board.RotationToIndex(structure.Transform.rotation);

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

    private static void RelocateOnServer(RelocateBoardStructureInstance relocate)
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
      var rot = board.IndexToRotation(relocate.Rotation);

      structure.Transform.SetPositionAndRotation(pos, rot);

      // unregister from previous cells
      foreach (var cell in structure.BoardCells)
        cell.Structure = null;

      // register to new cells
      var cells = board.ValidBoundsCells(structure.Transform, structure.AsStructure.Bounds).ToArray();
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

      RelocatingCursor.Board = CursorBoard;
      RelocatingCursor.GetAsThing.gameObject.SetActive(CursorBoard != null);

      if (CursorBoard == null)
        return;

      RelocatingCursor.Transform.SetPositionAndRotation(
        CursorBoard.GridToWorld(CursorGrid),
        CursorBoard.IndexToRotation(CursorRotation)
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

    public static Thing.DelayedActionInstance RelocateAction()
    {
      var action = new Thing.DelayedActionInstance
      {
        ActionMessage = CustomGameStrings.BoardStructureRelocateAction,
        Duration = 0f,
        Selection = RelocatingCursor.GetAsThing.GetSelection(),
      };
      if (KeyManager.GetButtonDown(KeyMap.QuantityModifier))
        CursorRotation = (CursorRotation + 1) % 4;

      if (CursorBoard != null && CursorBoard != RelocatingStructure.Board)
        return action.Fail(CustomGameStrings.BoardStructureRelocateDifferentBoard);

      var canConstruct = RelocatingCursor.AsStructure.CanConstruct();
      if (!canConstruct.CanConstruct)
        return action.Fail(canConstruct.ErrorMessage);

      return action.Succeed();
    }
  }
}