using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Util;
using HarmonyLib;
using LaunchPadBooster.Utils;
using UnityEngine;

namespace LibConstruct
{
  [HarmonyPatch(typeof(InventoryManager))]
  static partial class InventoryManagerPatch
  {
    public static Dictionary<string, Structure> constructionCursors;
    static InventoryManagerPatch()
    {
      var field = typeof(InventoryManager).GetField("_constructionCursors", BindingFlags.Static | BindingFlags.NonPublic);
      constructionCursors = (Dictionary<string, Structure>)field.GetValue(null);
    }

    // private in InventoryManager and not worth reflection
    static int RotateBlueprintHash = Animator.StringToHash("RotateBlueprint");

    [HarmonyPatch("PlacementMode"), HarmonyTranspiler]
    static IEnumerable<CodeInstruction> PlacementMode(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
    {
      var matcher = new CodeMatcher(instructions);
      matcher.MatchStartForward(
        new CodeMatch(OpCodes.Ldsfld, ReflectionUtils.Field(() => InventoryManager.ConstructionCursor)),
        new CodeMatch(OpCodes.Ldsfld, ReflectionUtils.Field(() => InventoryManager.ConstructionCursor)),
        new CodeMatch(OpCodes.Callvirt, ReflectionUtils.PropertyGetter(() => default(Thing).ThingTransformPosition))
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

        var rotOffset = RotationInput();
        currentRotation = (currentRotation + rotOffset + 4) % 4;

        var rot = board.IndexToRotation(currentRotation);

        PlacementBoard.PlacingOnBoard = true;
        boardCursor.Board = board;
        cursor.transform.SetPositionAndRotation(
          board.GridToWorld(PlacementBoard.CursorGrid),
          rot
        );
        InventoryManager.CurrentRotation = rot;
        PlacementBoard.CursorRotation = currentRotation;
        return false;
      }
      else if (InventoryManager.ConstructionCursor is IPlacementBoardStructure boardStructure)
      {
        boardStructure.Board = null;
        // this is copied from InventoryManager.PlacementMode to get the base cursor placement position
        InventoryManager.ConstructionCursor.ThingTransformPosition = InputHelpers.GetCameraForwardGrid(0.6f, InventoryManager.ConstructionCursor.GetCursorOffset);
        InventoryManager.ConstructionCursor.ThingTransformRotation = InventoryManager.Parent.ThingTransformRotation;
        InventoryManager.CurrentRotation = InventoryManager.ConstructionCursor.ThingTransformRotation;
      }
      return true;
    }

    static bool isRotating = false;
    static int RotationInput()
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

    static FieldInfo SpawnPrefabField;

    [HarmonyPatch("UsePrimaryComplete"), HarmonyPrefix]
    static bool UsePrimaryComplete(InventoryManager __instance)
    {
      if (PlacementBoard.PlacingOnBoard && __instance.ConstructionPanel.IsVisible)
      {
        if (SpawnPrefabField == null)
          SpawnPrefabField = typeof(InventoryManager).GetField("SpawnPrefab", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        PlacementBoard.UseMultiConstructorBoard(
          InventoryManager.Parent,
          __instance.ActiveHand.SlotId,
          InventoryManager.ConstructionCursor.ThingTransformPosition,
          InventoryManager.ConstructionCursor.ThingTransformRotation,
          InventoryManager.IsAuthoringMode,
          InventoryManager.ParentBrain.ClientId,
          SpawnPrefabField.GetValue(null) as Thing
        );
        return false;
      }
      return true;
    }
  }

  [HarmonyPatch(typeof(Structure))]
  static partial class StructurePatch
  {
    [HarmonyPatch(nameof(Structure.GetLocalGrid)), HarmonyPostfix]
    static void GetLocalGrid(Structure __instance, ref Grid3 __result)
    {
      if (!__instance.IsCursor || __instance is not IPlacementBoardStructure boardStructure)
        return;

      // if we are a cursor board structure, return the board grid position here so construction stops if we move
      __result = boardStructure.Board.WorldToGrid(boardStructure.Transform.position);
    }
  }
}