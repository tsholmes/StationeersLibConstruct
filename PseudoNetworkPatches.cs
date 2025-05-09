using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Pipes;
using HarmonyLib;

namespace LibConstruct
{
  static class CanConstructPatch
  {
    public static void RunPatch(Harmony harmony)
    {
      var method = PatchUtils.Method(() => default(Device).CanConstruct());
      var patches = Harmony.GetPatchInfo(method);

      // patch the (transpiled) original and all prefix/postfix patch methods
      var allMethods = new List<MethodInfo> { method };
      allMethods.AddRange(patches.Prefixes.Select(patch => patch.PatchMethod));
      allMethods.AddRange(patches.Postfixes.Select(patch => patch.PatchMethod));

      var transpiler = new HarmonyMethod(PatchUtils.Method(() => Patch(default, default)));

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
      var connectedDevices = PatchUtils.Method(() => default(SmallGrid).ConnectedDevices());
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

  static class PatchUtils
  {
    public static MethodInfo Method<T>(Expression<Func<T>> expr)
    {
      var call = expr.Body as MethodCallExpression;
      var method = call.Method;
      if (!method.IsVirtual) return method;

      // Passing a lambda like default(Type).Method() where Method is virtual will return the base MethodInfo
      // Search the explicit type of the left side for an override.

      var leftType = call.Object.Type;
      return leftType.GetMethods().First(m => m.GetBaseDefinition() == method.GetBaseDefinition()) ?? method;
    }
    public static MethodInfo PropertyGetter<T>(Expression<Func<T>> expr) => ((expr.Body as MemberExpression).Member as PropertyInfo).GetGetMethod();

    public static T CreateDelegate<T>(this MethodInfo method) where T : Delegate => (T)method.CreateDelegate(typeof(T));
  }
}