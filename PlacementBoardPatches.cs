
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Serialization;
using Assets.Scripts.Util;
using DLC;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace LibConstruct
{
  [HarmonyPatch]
  static class SaveDataPatch
  {
    [HarmonyPatch(typeof(XmlSaveLoad), nameof(XmlSaveLoad.AddExtraTypes)), HarmonyPrefix]
    static void AddExtraTypes(ref List<Type> extraTypes)
    {
      // add these types in case implementers try to save lists of them
      extraTypes.Add(typeof(PlacementBoardHostSaveData));
      extraTypes.Add(typeof(PlacementBoardSaveData));
      extraTypes.Add(typeof(PlacementBoardStructureSaveData));
    }
  }

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

        var rot = board.OriginRotation * Quaternion.AngleAxis(90 * (float)currentRotation, Vector3.forward);

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
      {
        __instance.ThingTransformPosition = __state;
        __instance.RegisteredPosition = __state;
        __instance.RegisteredRotation = __instance.ThingTransformRotation;
      }
    }
    [HarmonyPatch(nameof(Structure.RebuildGridState)), HarmonyPrefix]
    static void RebuildGridStatePrefix(Structure __instance, out Vector3 __state)
    {
      __state = __instance.ThingTransformPosition;
    }

    [HarmonyPatch(nameof(Structure.RebuildGridState)), HarmonyPostfix]
    static void RebuildGridStatePostfix(Structure __instance, Vector3 __state)
    {
      // undo the overriden position
      if (__instance is IPlacementBoardStructure)
      {
        __instance.ThingTransformPosition = __state;
        __instance.RegisteredPosition = __state;
        __instance.RegisteredRotation = __instance.ThingTransformRotation;
      }
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

  [HarmonyPatch(typeof(XmlSaveLoad))]
  static class XmlSaveLoadPatch
  {
    [HarmonyPatch("LoadInNetworks"), HarmonyPrefix]
    static void LoadInNetworks(XmlSaveLoad.WorldData worldData)
    {
      PlacementBoard.StartLoad();
    }

    delegate void AddToSaveDelegate(Thing thing, XmlSaveLoad.WorldData worldData, Thing parent);
    private static AddToSaveDelegate AddToSave;
    static XmlSaveLoadPatch()
    {
      var method = typeof(XmlSaveLoad).GetMethod("AddToSave", BindingFlags.Static | BindingFlags.NonPublic);
      AddToSave = (AddToSaveDelegate)method.CreateDelegate(typeof(AddToSaveDelegate));
    }

    static bool SkipSave(Thing thing, Thing parent)
    {
      return thing is IPlacementBoardStructure structure
        && structure.Board != null
        && structure.Board.PrimaryHost == (parent as IPlacementBoardHost);
    }

    [HarmonyPatch("AddToSave", typeof(Thing), typeof(XmlSaveLoad.WorldData), typeof(Thing)), HarmonyPrefix]
    static bool AddToSavePrefix(Thing thing, XmlSaveLoad.WorldData worldData, Thing parent)
    {
      // if this is a board structure, skip saving until we are saving from the boards primary host
      return !SkipSave(thing, parent);
    }

    [HarmonyPatch("AddToSave", typeof(Thing), typeof(XmlSaveLoad.WorldData), typeof(Thing)), HarmonyPostfix]
    static void AddToSavePostfix(Thing thing, XmlSaveLoad.WorldData worldData, Thing parent)
    {
      if (thing is not IPlacementBoardHost host || SkipSave(thing, parent)) return;
      foreach (var board in host.GetPlacementBoards())
      {
        if (board.PrimaryHost != host)
          continue;
        // if this is the primary host for the board, visit all children
        foreach (var structure in board.Structures)
          AddToSave((Thing)structure, worldData, thing);
      }
    }
  }

  [HarmonyPatch(typeof(SharedDLCManager))]
  static class SharedDLCManagerPatch
  {
    [HarmonyPatch(nameof(SharedDLCManager.HostFinishedLoad)), HarmonyPostfix]
    static void HostFinishedLoad()
    {
      PlacementBoard.FinishLoad();
    }

    [HarmonyPatch(nameof(SharedDLCManager.ClientFinishedLoad)), HarmonyPostfix]
    static void ClientFinishedLoad()
    {
      PlacementBoard.FinishLoad();
    }
  }

  [HarmonyPatch(typeof(WorldManager))]
  static class WorldManagerPatch
  {
    [HarmonyPatch(nameof(WorldManager.DeserializeOnJoin)), HarmonyPrefix]
    static void DeserializeOnJoin(RocketBinaryReader reader)
    {
      PlacementBoard.StartLoad();
    }
  }

  [HarmonyPatch(typeof(NetworkServer))]
  static class NetworkServerPatch
  {
    // NetworkServer handles joins one at a time in ProcessJoinQueue, so it should be fine to use a global here
    // If joins are handled in parallel in the future, we'll need to make this ThreadLocal or something
    // This is done as a stack so we won't need to change anything later to support nested boards (if that ever makes sense)
    private static Stack<PlacementBoard> SerializingBoards = new();

    static bool SkipSerialize(Thing thing)
    {
      return thing is IPlacementBoardStructure structure
        && structure.Board != null
        && (SerializingBoards.Count == 0 || SerializingBoards.Peek() != structure.Board);
    }

    [HarmonyPatch(nameof(NetworkServer.Serialize)), HarmonyPrefix]
    static bool SerializePrefix(RocketBinaryWriter writer, Thing thing, ref uint count)
    {
      return !SkipSerialize(thing);
    }

    [HarmonyPatch(nameof(NetworkServer.Serialize)), HarmonyPostfix]
    static void SerializePostfix(RocketBinaryWriter writer, Thing thing, ref uint count)
    {
      if (thing is not IPlacementBoardHost host || SkipSerialize(thing))
        return;
      foreach (var board in host.GetPlacementBoards())
      {
        SerializingBoards.Push(board);
        foreach (var child in board.Structures)
          NetworkServer.Serialize(writer, (Thing)child, ref count);
        SerializingBoards.Pop();
      }
    }
  }

  [HarmonyPatch(typeof(MessageFactory))]
  static class MessageFactoryPatch
  {
    // It would be better for StationeersMods to have a registry of custom message types
    // so we don't have to worry about conflicting indices
    [HarmonyPatch(nameof(MessageFactory.GetTypeFromIndex)), HarmonyPrefix]
    static bool GetTypeFromIndex(byte index, ref Type __result)
    {
      if (index == CreateBoardStructureMessage.TypeIndex)
      {
        __result = typeof(CreateBoardStructureMessage);
        return false;
      }
      return true;
    }

    [HarmonyPatch(nameof(MessageFactory.GetIndexFromType)), HarmonyPrefix]
    static bool GetIndexFromType(Type type, ref byte __result)
    {
      if (type == typeof(CreateBoardStructureMessage))
      {
        __result = CreateBoardStructureMessage.TypeIndex;
        return false;
      }
      return true;
    }
  }
}