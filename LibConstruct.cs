using HarmonyLib;
using StationeersMods.Interface;

namespace LibConstruct
{
  [StationeersMod("LibConstruct", "LibConstruct [StationeersMods]", "0.1.0")]
  class LibConstructMod : ModBehaviour
  {
    public override void OnLoaded(ContentHandler contentHandler)
    {
      UnityEngine.Debug.Log("LibConstruct Loaded!");
    }
  }
}