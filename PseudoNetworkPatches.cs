using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using HarmonyLib;
using LaunchPadBooster.Utils;

namespace LibConstruct;

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
      harmony.Patch(targetMethod, transpiler: transpiler);
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
    var fillConnected = typeof(SmallGrid).GetMethods().First(
      m => m.Name is nameof(SmallGrid.FillConnected) && m.IsGenericMethodDefinition
    ).MakeGenericMethod(typeof(Device));
    var match = new CodeMatch(inst => inst.Calls(fillConnected));
    var pseudoFillConnected = typeof(PseudoNetworks).GetMethod(nameof(PseudoNetworks.FillConnectedDevices));

    // check for all instances of calling FillConnections in case someone else patched it
    while (true && thisIndex != -1)
    {
      matcher.MatchEndForward(match);
      if (!matcher.IsValid) break;

      // replace with call to RemovePseudoConnected to filter out networked devices
      matcher.Operand = pseudoFillConnected;
      matcher.Advance(1);
    }

    return matcher.Instructions();
  }
}