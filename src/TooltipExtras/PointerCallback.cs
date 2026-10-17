using System.Reflection;
using System.Reflection.Emit;
namespace TooltipExtras;

internal static class PointerCallback
{
    // MethodInfo is used by the producer's existing event subscriber. Keep its
    // real tweak instance/configuration intact; only relay this one method.
    public static MethodInfo Create(Type pointerType, Action<nint> callback)
    {
        if (!pointerType.IsPointer) throw new NotSupportedException("Pointer callback signature");
        var a = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TooltipExtras.Callback." + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var type = a.DefineDynamicModule("main").DefineType("Callback", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var slot = type.DefineField("Forward", typeof(Action<nint>), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineMethod("AfterItemDetailUpdate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [pointerType]);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldsfld, slot); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Callvirt, typeof(Action<nint>).GetMethod("Invoke")!); il.Emit(OpCodes.Ret);
        var result = type.CreateType()!; result.GetField("Forward")!.SetValue(null, callback);
        return result.GetMethod(method.Name)!;
    }
    public static Action<nint> Forward(object target, MethodInfo method)
    {
        var dm = new DynamicMethod("OriginalItemDetail", typeof(void), [typeof(object), typeof(nint)], typeof(PointerCallback).Module, true);
        var il = dm.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, method.DeclaringType!);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Conv_U); il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
        return (Action<nint>)dm.CreateDelegate(typeof(Action<nint>), target);
    }
}
internal readonly record struct SimpleGeometry(ushort Window, ushort Surface, float Footer)
{
    public bool IsAppend(SimpleGeometry after, ushort rowHeight, float rowY) => rowHeight > 0 && Window >= 24 && Window < 16000
        && Window + rowHeight <= 16000 && after.Window == Window + rowHeight && after.Surface == after.Window
        && float.IsFinite(Footer) && float.IsFinite(after.Footer) && after.Footer == Footer + rowHeight
        && float.IsFinite(rowY) && Math.Abs(rowY - (Window - 10f)) < 0.01f;
}
