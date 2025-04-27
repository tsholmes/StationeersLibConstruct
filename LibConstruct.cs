using System;
using System.Collections.Generic;
using Assets.Scripts.Serialization;
using HarmonyLib;
using StationeersMods.Interface;

namespace LibConstruct
{
  [StationeersMod("LibConstruct", "LibConstruct [StationeersMods]", "0.1.0")]
  class LibConstructMod : ModBehaviour
  {
    public override void OnLoaded(ContentHandler contentHandler)
    {
      base.OnLoaded(contentHandler);

      var harmony = new Harmony("LibConstruct");
      harmony.PatchAll();
    }
  }

  [HarmonyPatch]
  static class SaveDataPatch
  {
    [HarmonyPatch(typeof(XmlSaveLoad), nameof(XmlSaveLoad.AddExtraTypes)), HarmonyPrefix]
    static void AddExtraTypes(ref List<Type> extraTypes)
    {
      // TODO
    }
  }
}