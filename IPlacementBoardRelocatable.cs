
using System;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using UnityEngine;

namespace LibConstruct
{
  public interface IPlacementBoardRelocatable : IPlacementBoardStructure, IReferencable
  {
    public Thing GetAsThing { get; } // from Thing
    public Structure AsStructure { get; } // from Thing

    // called when this structure is relocated
    public void OnStructureRelocated();
  }

  public static class BoardRelocateHooks
  {
    // called like:
    // if (attack.SourceItem is MyRelocateTool myTool)
    //   return BoardRelocateHooks.StructureAttackWith(this, attack, doAction, BoardRelocateHooks.NormalToolRelocateContinue(myTool));
    public static Thing.DelayedActionInstance StructureAttackWith(IPlacementBoardRelocatable structure, Attack attack, bool doAction, Func<bool> continueRelocating = null)
    {
      // mark the current attack as a relocate start attack so we process it on the client
      InventoryManagerPatch.IsRelocateAttack = true;

      var action = new Thing.DelayedActionInstance
      {
        Duration = 0f,
        ActionMessage = CustomGameStrings.BoardStructureRelocateAction,
      };
      if (doAction && PlacementBoard.RelocateMouseReleased)
        PlacementBoard.StartRelocate(structure, continueRelocating);
      return action.Succeed();
    }

    public static Func<bool> NormalToolRelocateContinue(DynamicThing tool)
    {
      return () =>
        tool &&
        InventoryManager.ActiveHandSlot.Get() == tool &&
        InventoryManager.CurrentMode == InventoryManager.Mode.Normal &&
        !KeyManager.GetMouseDown("Secondary");
    }
  }
}