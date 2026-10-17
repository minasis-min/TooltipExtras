using System.Reflection;
using System.Reflection.Emit;
namespace TooltipExtras;
internal static class SimpleDispatchProxy
{
    public static Type Create(Type baseType)
    {
        var callback = baseType.GetMethod("OnGenerateItemTooltip")!;
        var getter = baseType.GetProperty("Enabled")!.GetMethod!;
        var ctor = baseType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,Type.EmptyTypes,null)!;
        if (!getter.IsVirtual || !callback.IsVirtual || callback.GetParameters().Length != 2 || callback.GetParameters().Any(p=>!p.ParameterType.IsPointer)) throw new NotSupportedException("Simple dispatch signature");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TooltipExtras.SimpleDispatch."+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.RunAndCollect);
        var type = assembly.DefineDynamicModule("main").DefineType("ScopedTooltip",TypeAttributes.Public|TypeAttributes.Sealed,baseType);
        var active = type.DefineField("active",typeof(Func<bool>),FieldAttributes.Private);
        var action = type.DefineField("action",typeof(Action<nint,nint>),FieldAttributes.Private);
        var constructor = type.DefineConstructor(MethodAttributes.Public,CallingConventions.Standard,[typeof(Func<bool>),typeof(Action<nint,nint>)]);
        var il = constructor.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,ctor);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Stfld,active);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Stfld,action);il.Emit(OpCodes.Ret);
        var get=type.DefineMethod("get_Enabled",MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.SpecialName|MethodAttributes.HideBySig,typeof(bool),Type.EmptyTypes);
        il=get.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,active);il.Emit(OpCodes.Callvirt,typeof(Func<bool>).GetMethod("Invoke")!);il.Emit(OpCodes.Ret);type.DefineMethodOverride(get,getter);
        var call=type.DefineMethod(callback.Name,MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.HideBySig,typeof(void),callback.GetParameters().Select(p=>p.ParameterType).ToArray());
        il=call.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,action);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Callvirt,typeof(Action<nint,nint>).GetMethod("Invoke")!);il.Emit(OpCodes.Ret);type.DefineMethodOverride(call,callback);
        return type.CreateType()!;
    }
}
