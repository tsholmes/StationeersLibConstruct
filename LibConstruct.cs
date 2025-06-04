using HarmonyLib;
using StationeersMods.Interface;

namespace LibConstruct
{
  [StationeersMod("LibConstruct", "LibConstruct [StationeersLaunchPad]", "0.1.0")]
  class LibConstructMod : ModBehaviour
  {
    public override void OnLoaded(ContentHandler contentHandler)
    {
      base.OnLoaded(contentHandler);

      var harmony = new Harmony("LibConstruct");
      harmony.PatchAll();

      WorldManager.OnGameDataLoaded += () => CanConstructPatch.RunPatch(harmony);
    }
  }
}