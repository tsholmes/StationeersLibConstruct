using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster;
using UnityEngine;

namespace LibConstruct;

class LibConstructMod : MonoBehaviour
{
  public const string ModID = "LibConstruct";
  public static Mod MOD;

  public static ConfigEntry<bool> RepairBoardLoadOrder;

  public void OnLoaded(ConfigFile config, ModData mod)
  {
    var about = mod.GetAboutData();
    MOD = new(ModID, about.Version);
    // add these types in case implementers try to save lists of them
    MOD.AddSaveDataType<PlacementBoardHostSaveData>();
    MOD.AddSaveDataType<PlacementBoardSaveData>();
    MOD.AddSaveDataType<PlacementBoardStructureSaveData>();

    MOD.Networking.RegisterLegacyMessage<CreateBoardStructureMessage>();
    MOD.Networking.RegisterLegacyMessage<RelocateBoardStructureMessage>();

    MOD.Networking.Required = true;

    var harmony = new Harmony(ModID);
    harmony.PatchAll();

    WorldManager.OnGameDataLoaded += () => CanConstructPatch.RunPatch(harmony);

    RepairBoardLoadOrder = config.Bind(
      new ConfigDefinition("Debug", "RepairBoardLoadOrder"),
      false,
      new ConfigDescription(
        "If you have a save thats not loading due to errors coming from LibConstruct, try enabling this setting. Warning: this will slow down loading significantly"
      )
    );
  }
}