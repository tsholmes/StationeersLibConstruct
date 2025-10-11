using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster;
using UnityEngine;

namespace LibConstruct
{
  [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
  class LibConstructMod : BaseUnityPlugin
  {
    public const string PluginGuid = "LibConstruct";
    public const string PluginName = "LibConstruct [StationeersLaunchPad]";
    public const string PluginVersion = "0.2.6";

    public static Mod MOD = new(PluginGuid, PluginVersion);

    public static ConfigEntry<bool> RepairBoardLoadOrder;

    public void OnLoaded(List<GameObject> prefabs)
    {
      // add these types in case implementers try to save lists of them
      MOD.AddSaveDataType<PlacementBoardHostSaveData>();
      MOD.AddSaveDataType<PlacementBoardSaveData>();
      MOD.AddSaveDataType<PlacementBoardStructureSaveData>();

      MOD.RegisterNetworkMessage<CreateBoardStructureMessage>();
      MOD.RegisterNetworkMessage<RelocateBoardStructureMessage>();

      var harmony = new Harmony(PluginGuid);
      harmony.PatchAll();

      WorldManager.OnGameDataLoaded += () => CanConstructPatch.RunPatch(harmony);

      RepairBoardLoadOrder = this.Config.Bind(
        new ConfigDefinition("Debug", "RepairBoardLoadOrder"),
        false,
        new ConfigDescription(
          "If you have a save thats not loading due to errors coming from LibConstruct, try enabling this setting. Warning: this will slow down loading significantly"
        )
      );
    }
  }
}