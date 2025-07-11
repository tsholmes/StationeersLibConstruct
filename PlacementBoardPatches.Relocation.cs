using System.Collections.Generic;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;
using LaunchPadBooster.Utils;
using UnityEngine;

namespace LibConstruct
{

  [HarmonyPatch(typeof(InventoryManager))]
  static partial class InventoryManagerPatch
  {
    [HarmonyPatch("NormalMode"), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> NormalMode(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);
      var match = new CodeMatch(CodeInstruction.Call(() => KeyManager.GetMouse(default)));
      // go to second KeyManager.GetMouse call
      matcher.MatchStartForward(match).Advance(1).MatchStartForward(match);
      matcher.ThrowIfInvalid("could not find InventoryManager.NormalMode insertion point");
      matcher.Advance(1);
      matcher.Insert(CodeInstruction.Call(() => NormalModeRelocateCheck()));
      return matcher.Instructions();
    }

    static void NormalModeRelocateCheck()
    {
      if (!PlacementBoard.IsRelocating)
        return;

      var action = PlacementBoard.RelocateAction();
      var color = action.IsDisabled ? Color.red : Color.green;
      InventoryManager.Instance.TooltipRef.HandleToolTipDisplay(new PassiveTooltip(action, string.Empty, PlacementBoard.RelocatingStructure.GetAsThing)
      {
        color = color,
      });
      color.a = InventoryManager.Instance.CursorAlphaInteractable;
      CursorManager.SetSelection(action.Selection, color);
      CursorManager.SetSelectionVisibility(InventoryManager.ShowUi && PlacementBoard.RelocatingCursor.GetAsThing.gameObject.activeInHierarchy);
    }

    [HarmonyPatch("NormalModeThing"), HarmonyPrefix]
    static bool NormalModeThing(ref bool __result)
    {
      if (PlacementBoard.IsRelocating)
      {
        __result = true;
        return false;
      }
      return true;
    }

    public static bool IsRelocateAttack = false;

    [HarmonyPatch("HandlePrimaryUse"), HarmonyPrefix]
    static bool HandlePrimaryUse()
    {
      if (!PlacementBoard.IsRelocating)
      {
        // reset flag so we're sure the next attack is a relocate start
        IsRelocateAttack = false;
        return true;
      }

      var action = PlacementBoard.RelocateAction();
      // only relocate if we released the mouse since starting the relocate
      if (!action.IsDisabled && PlacementBoard.RelocateMouseReleased)
        PlacementBoard.FinishRelocate();

      return false;
    }

    [HarmonyPatch("UseItemComplete"), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> UseItemComplete(IEnumerable<CodeInstruction> instructions)
    {
      var matcher = new CodeMatcher(instructions);
      matcher.MatchStartForward(new CodeMatch(OpCodes.Ldfld, ReflectionUtils.Field(() => default(DynamicThing).AttackWithEvent)));
      matcher.RemoveInstruction();
      matcher.Insert(CodeInstruction.Call(() => GetAttackWithEvent(default)));
      return matcher.Instructions();
    }

    static AttackWithEvent GetAttackWithEvent(DynamicThing thing)
    {
      if (IsRelocateAttack)
        return AttackWithEvent.Local;
      return thing.AttackWithEvent;
    }
  }
}