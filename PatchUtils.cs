using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LibConstruct
{
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
    public static MethodInfo PropertyGetter<T>(Expression<Func<T>> expr) =>
      ((expr.Body as MemberExpression).Member as PropertyInfo).GetGetMethod();

    public static FieldInfo Field<T>(Expression<Func<T>> expr) =>
      (expr.Body as MemberExpression).Member as FieldInfo;

    public static T CreateDelegate<T>(this MethodInfo method) where T : Delegate =>
      (T)method.CreateDelegate(typeof(T));
  }
}