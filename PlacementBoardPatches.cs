
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using HarmonyLib;
using UnityEngine;

namespace LibConstruct
{
  [HarmonyPatch(typeof(InventoryManager))]
  static class InventoryManagerPatch
  {
    [HarmonyPatch("PlacementMode"), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> PlacementMode(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
    {
      var matcher = new CodeMatcher(instructions);
      matcher.MatchStartForward(
        new CodeMatch(OpCodes.Ldsfld, Field(() => InventoryManager.ConstructionCursor)),
        new CodeMatch(OpCodes.Ldsfld, Field(() => InventoryManager.ConstructionCursor)),
        new CodeMatch(OpCodes.Callvirt, PropGetter(() => default(Thing).ThingTransformPosition))
      );
      matcher.ThrowIfInvalid("could not find InventoryManager.PlacementMode insertion point");
      matcher.Insert(CodeInstruction.Call(() => PlacementModeBoardCheck()));
      return matcher.Instructions();
    }

    static void PlacementModeBoardCheck()
    {
      var panel = InventoryManager.Instance.ConstructionPanel;
      if (!panel.IsVisible || !panel.Parent)
        return;
      var constructor = panel.Parent;
      if (!constructor)
        return;
      if (panel.BuildIndex < 0 || panel.BuildIndex >= constructor.Constructables.Count)
        return;
      // call UpdatePlacement every time to be sure we are switching to/from board structure
      InventoryManager.UpdatePlacement(constructor.Constructables[panel.BuildIndex]);
    }

    private static Dictionary<string, Structure> constructionCursors;
    static InventoryManagerPatch()
    {
      var field = typeof(InventoryManager).GetField("_constructionCursors", BindingFlags.Static | BindingFlags.NonPublic);
      constructionCursors = (Dictionary<string, Structure>)field.GetValue(null);
    }

    [HarmonyPatch("UpdatePlacement", typeof(Structure)), HarmonyPrefix]
    static bool UpdatePlacement(ref Structure structure)
    {
      var currentRotation = PlacementBoard.CursorRotation;
      PlacementBoard.CursorRotation = 0;
      PlacementBoard.PlacingOnBoard = false;
      if (PlacementBoard.CursorBoard != null)
      {
        var board = PlacementBoard.CursorBoard;
        var boardStructure = board.EquivalentStructure(structure);
        if (boardStructure == null)
          return true;
        constructionCursors.TryGetValue(boardStructure.name, out var cursor);
        if (cursor == null)
          return true;
        if (cursor is not IPlacementBoardStructure boardCursor)
          return true; // this really shouldn't be possible
        if (cursor != InventoryManager.ConstructionCursor)
        {
          if (InventoryManager.ConstructionCursor != null)
            InventoryManager.ConstructionCursor.gameObject.SetActive(false);
          if (InventoryManager.ConstructionCursor is IPlacementBoardStructure prevCursor)
            prevCursor.Board = null;
          InventoryManager.ConstructionCursor = cursor;
          cursor.gameObject.SetActive(true);
        }

        var rotOffset = RotationOffset();
        currentRotation = (currentRotation + rotOffset + 4) % 4;

        var rot = board.Origin.localRotation * Quaternion.AngleAxis(90 * (float)currentRotation, Vector3.forward);

        PlacementBoard.PlacingOnBoard = true;
        boardCursor.Board = board;
        cursor.transform.SetPositionAndRotation(
          board.GridToWorld(PlacementBoard.CursorGrid),
          rot
        );
        InventoryManager.CurrentRotation = cursor.ThingTransformRotation;
        PlacementBoard.CursorRotation = currentRotation;
        return false;
      }
      return true;
    }

    // private in InventoryManager and not worth reflection
    static int RotateBlueprintHash = Animator.StringToHash("RotateBlueprint");

    static bool isRotating = false;
    static int RotationOffset()
    {
      var offset = 0;

      var rotateBtnDown = KeyManager.GetButton(KeyMap.QuantityModifier);
      if (rotateBtnDown)
      {
        if (!isRotating) offset++;
        offset += InventoryManager.Instance.newScrollData switch { > 0 => 3, < 0 => 1, _ => 0 };
      }
      else if (KeyManager.GetButtonUp(KeyMap.RotateRollRight))
      {
        offset++;
      }
      else if (KeyManager.GetButtonUp(KeyMap.RotateRollLeft))
      {
        offset += 3;
      }
      isRotating = rotateBtnDown;

      offset &= 3;

      if (offset != 0)
        UIAudioManager.Play(RotateBlueprintHash);
      return offset;
    }

    [HarmonyPatch("UsePrimaryComplete"), HarmonyPrefix]
    static bool UsePrimaryComplete(InventoryManager __instance)
    {
      if (PlacementBoard.PlacingOnBoard && __instance.ConstructionPanel.IsVisible)
      {
        PlacementBoard.UseMultiConstructorBoard(
          InventoryManager.Parent,
          __instance.ActiveHand.SlotId,
          InventoryManager.ConstructionCursor.ThingTransformPosition,
          InventoryManager.ConstructionCursor.ThingTransformRotation,
          InventoryManager.IsAuthoringMode,
          InventoryManager.ParentBrain.ClientId,
          InventoryManager.SpawnPrefab
        );
        return false;
      }
      return true;
    }

    static MethodInfo PropGetter<T>(Expression<Func<T>> expr)
    {
      var member = (MemberExpression)expr.Body;
      var prop = (PropertyInfo)member.Member;
      return prop.GetGetMethod();
    }

    static FieldInfo Field<T>(Expression<Func<T>> expr)
    {
      var member = (MemberExpression)expr.Body;
      var field = (FieldInfo)member.Member;
      return field;
    }
  }

  [HarmonyPatch(typeof(CursorManager))]
  static class CursorManagerPatch
  {
    [HarmonyPatch(nameof(CursorManager.SetCursorTarget)), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> SetCursorTarget(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
    {
      var matcher = new CodeMatcher(instructions);
      matcher.MatchStartForward(new CodeMatch(OpCodes.Isinst, typeof(IComputer)));
      matcher.ThrowIfInvalid("could not find CursorManager.SetCursorTarget insertion point");
      matcher.Insert(CodeInstruction.Call(() => PlacementBoard.FindCursorBoard()));
      return matcher.Instructions();
    }
  }

  [HarmonyPatch(typeof(Structure))]
  static class StructurePatch
  {
    [HarmonyPatch(nameof(Structure.OnAssignedReference)), HarmonyPrefix]
    static void OnAssignedReferencePrefix(Structure __instance, out Vector3 __state)
    {
      __state = __instance.ThingTransformPosition;
    }

    [HarmonyPatch(nameof(Structure.OnAssignedReference)), HarmonyPostfix]
    static void OnAssignedReferencePostfix(Structure __instance, Vector3 __state)
    {
      // undo the overriden position
      if (__instance is IPlacementBoardStructure)
        __instance.ThingTransformPosition = __state;
    }
    [HarmonyPatch(nameof(Structure.RebuildGridState)), HarmonyPrefix]
    static void RebuildGridStatePrefix(Structure __instance, out Vector3 __state)
    {
      // TODO: is this what we want to do here?
      __state = __instance.ThingTransformPosition;
    }

    [HarmonyPatch(nameof(Structure.RebuildGridState)), HarmonyPostfix]
    static void RebuildGridStatePostfix(Structure __instance, Vector3 __state)
    {
      // undo the overriden position
      if (__instance is IPlacementBoardStructure)
        __instance.ThingTransformPosition = __state;
    }

    [HarmonyPatch(nameof(Structure.CanConstruct)), HarmonyPrefix]
    static bool CanConstruct(Structure __instance, ref CanConstructInfo __result)
    {
      if (__instance is IPlacementBoardStructure)
      {
        __result = CanConstructInfo.ValidPlacement;
        return false;
      }
      return true;
    }
  }

  [HarmonyPatch(typeof(SmallGrid))]
  static class SmallGridPatch
  {
    // this is only needed if we end up adding board pipes
    [HarmonyPatch(nameof(SmallGrid.RegisterGridUpdate)), HarmonyPrefix]
    static bool RegisterGridUpdate(SmallGrid __instance) => __instance is not IPlacementBoardStructure;

    [HarmonyPatch(nameof(SmallGrid.CanConstruct)), HarmonyPrefix]
    static bool CanConstruct(SmallGrid __instance, ref CanConstructInfo __result)
    {
      if (__instance is IPlacementBoardStructure)
      {
        __result = CanConstructInfo.ValidPlacement;
        return false;
      }
      return true;
    }
  }

  [HarmonyPatch(typeof(GridController))]
  static class GridControllerPatch
  {
    [HarmonyPatch(nameof(GridController.Register)), HarmonyPrefix]
    static bool Register(Structure structure)
    {
      if (structure is not IPlacementBoardStructure) return true;
      GridController.AllStructures.Add(structure);
      structure.OnRegistered(null);
      return false;
    }

    [HarmonyPatch(nameof(GridController.Deregister)), HarmonyPrefix]
    static bool Deregister(Structure structure)
    {
      if (structure is not IPlacementBoardStructure)
        return true;
      GridController.AllStructures.Remove(structure);
      structure.OnDeregistered();
      return false;
    }
  }
}