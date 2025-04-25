
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
      if (PlacementBoard.CursorBoard != null)
      {
        var board = PlacementBoard.CursorBoard;
        var boardStructure = board.EquivalentStructure(structure);
        if (boardStructure == null)
          return true;
        constructionCursors.TryGetValue(boardStructure.name, out var cursor);
        if (cursor == null)
          return true;
        if (cursor != InventoryManager.ConstructionCursor)
        {
          if (InventoryManager.ConstructionCursor != null)
            InventoryManager.ConstructionCursor.gameObject.SetActive(false);
          InventoryManager.ConstructionCursor = cursor;
          cursor.gameObject.SetActive(true);
        }
        cursor.transform.SetPositionAndRotation(
          board.GridToWorld(PlacementBoard.CursorGrid),
          board.Origin.localRotation
        );
        InventoryManager.CurrentRotation = cursor.ThingTransformRotation;
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
}