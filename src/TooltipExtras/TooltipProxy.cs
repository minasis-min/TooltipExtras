using System.Reflection;
using System.Reflection.Emit;
namespace TooltipExtras;

// Derive from the provider's actual loaded base, including its pointer types/load context.
// No provider DLL or implementation is copied into TE.
internal static class TooltipProxy
{
    public static Type Create(Type baseType)
    {
        var callback = baseType.GetMethod("OnGenerateItemTooltip") ?? throw new MissingMethodException("OnGenerateItemTooltip");
        var parameters = callback.GetParameters();
        var getter = baseType.GetProperty("IsEnabled")?.GetMethod ?? throw new MissingMethodException("IsEnabled");
        var ctor = baseType.GetConstructors().Single(c => c.GetParameters().Length == 1);
        if (!callback.IsVirtual || callback.ReturnType != typeof(void) || parameters.Length != 2 || parameters.Any(p => !p.ParameterType.IsPointer)
            || !getter.IsVirtual || getter.ReturnType != typeof(bool)) throw new NotSupportedException("Tooltip callback signature");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TooltipExtras.ATProxy." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("TooltipProxy", TypeAttributes.Public | TypeAttributes.Sealed, baseType);
        var enabled = type.DefineField("enabled", typeof(Func<bool>), FieldAttributes.Private);
        var capture = type.DefineField("capture", typeof(Action<nint, nint>), FieldAttributes.Private);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [ctor.GetParameters()[0].ParameterType, typeof(Func<bool>), typeof(Action<nint, nint>)]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, ctor);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, enabled);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Stfld, capture); il.Emit(OpCodes.Ret);
        var get = type.DefineMethod("get_IsEnabled", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName | MethodAttributes.HideBySig, typeof(bool), Type.EmptyTypes);
        il = get.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, enabled); il.Emit(OpCodes.Callvirt, typeof(Func<bool>).GetMethod("Invoke")!); il.Emit(OpCodes.Ret); type.DefineMethodOverride(get, getter);
        var call = type.DefineMethod(callback.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(void), parameters.Select(p => p.ParameterType).ToArray());
        il = call.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, capture);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Callvirt, typeof(Action<nint, nint>).GetMethod("Invoke")!); il.Emit(OpCodes.Ret); type.DefineMethodOverride(call, callback);
        return type.CreateType()!;
    }
    public static Action<nint, nint> Forward(object original, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var forward = new DynamicMethod("ForwardTooltip", typeof(void), [typeof(object), typeof(nint), typeof(nint)], typeof(TooltipProxy).Module, true);
        var il = forward.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, method.DeclaringType!);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Callvirt, method); il.Emit(OpCodes.Ret);
        return (Action<nint, nint>)forward.CreateDelegate(typeof(Action<nint, nint>), original);
    }
}
