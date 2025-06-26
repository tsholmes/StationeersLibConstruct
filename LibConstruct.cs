using BepInEx.Configuration;
using HarmonyLib;
using StationeersMods.Interface;

namespace LibConstruct
{
  [StationeersMod("LibConstruct", "LibConstruct [StationeersLaunchPad]", "0.1.2")]
  class LibConstructMod : ModBehaviour
  {
    public static ConfigEntry<bool> RepairBoardLoadOrder;

    public override void OnLoaded(ContentHandler contentHandler)
    {
      base.OnLoaded(contentHandler);

      var harmony = new Harmony("LibConstruct");
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