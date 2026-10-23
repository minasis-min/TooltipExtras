using System.Reflection;
namespace TooltipExtras;

internal static class ProducerMethods
{
    // Inherited source callbacks with a single ret instruction cannot add any
    // item tooltip text. Dedicated API/image event paths remain responsible.
    public static bool IsEmptyInherited(MethodInfo method,Type baseType)=>
        method.DeclaringType==baseType&&!method.IsStatic&&method.ReturnType==typeof(void)
        &&method.GetMethodBody()?.GetILAsByteArray() is [0x2A];
}
