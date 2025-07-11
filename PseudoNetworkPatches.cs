using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using HarmonyLib;
using LaunchPadBooster.Utils;

namespace LibConstruct
{
  static class CanConstructPatch
  {
    public static void RunPatch(Harmony harmony)
    {
      var method = ReflectionUtils.Method(() => default(Device).CanConstruct());
      var patches = Harmony.GetPatchInfo(method);

      // patch the (transpiled) original and all prefix/postfix patch methods
      var allMethods = new List<MethodInfo> { method };
      if (patches != null)
      {
        allMethods.AddRange(patches.Prefixes.Select(patch => patch.PatchMethod));
        allMethods.AddRange(patches.Postfixes.Select(patch => patch.PatchMethod));
      }

      var transpiler = new HarmonyMethod(ReflectionUtils.Method(() => Patch(default, default)));

      foreach (var targetMethod in allMethods)
      {
        harmony.Patch(targetMethod, transpiler: transpiler);
      }
    }

    static int FindDeviceArg(MethodInfo method)
    {
      if (!method.IsStatic && typeof(Device).IsAssignableFrom(method.DeclaringType))
        return 0;
      var argOffset = method.IsStatic ? 0 : 1;
      var index = method.GetParameters().ToList().FindIndex(p => typeof(Device).IsAssignableFrom(p.ParameterType));
      if (index == -1) return -1;
      return index + argOffset;
    }

    // use MinValue priority to force running after all other transpilers
    [HarmonyTranspiler, HarmonyPriority(int.MinValue)]
    static IEnumerable<CodeInstruction> Patch(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
      var thisIndex = FindDeviceArg((MethodInfo)__originalMethod);

      var matcher = new CodeMatcher(instructions);
      var connectedDevices = ReflectionUtils.Method(() => default(SmallGrid).ConnectedDevices());
      var match = new CodeMatch(inst => inst.Calls(connectedDevices));

      // check for all instances of calling ConnectedDevices in case someone else patched it
      while (true && thisIndex != -1)
      {
        matcher.MatchEndForward(match);
        if (!matcher.IsValid) break;

        // insert a call to RemovePseudoConnected immediately after the call to ConnectedDevices
        // that will return a new list that gets assigned to the local and used later
        matcher.Advance(1);
        matcher.InsertAndAdvance(
          new CodeInstruction(OpCodes.Ldarg_S, thisIndex), // this
          CodeInstruction.Call(() => PseudoNetworks.RemovePseudoConnected(default, default))
        );
      }

      return matcher.Instructions();
    }
  }
}