using System.Reflection;
using System.Reflection.Emit;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

// The caller checks the actual constructor/getter/setter operation contracts.
// Only the synchronous formatter receives this view; no tooltip is retained.
internal sealed unsafe class BorrowedTooltip
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly Func<object,nint> strings,numbers;
    private readonly Func<nint,nint,object> create;
    public Assembly NativeAssembly {get;}
    public BorrowedTooltip(Type type)
    {
        var baseType=type.BaseType??throw new NotSupportedException("ツールチップの基底型がありません");
        var s=baseType.GetField("_stringArrayData",Flags)??throw new MissingFieldException("Tooltip strings");
        var n=baseType.GetField("NumberArrayData",Flags)??throw new MissingFieldException("Tooltip numbers");
        if(s.FieldType.GetElementType()?.FullName!=typeof(StringArrayData).FullName||n.FieldType.GetElementType()?.FullName!=typeof(NumberArrayData).FullName
            ||!s.FieldType.IsPointer||!n.FieldType.IsPointer)throw new NotSupportedException("ツールチップの配列型が未対応です");
        NativeAssembly=s.FieldType.GetElementType()!.Assembly;
        strings=Read(s);numbers=Read(n);
        var ctor=type.GetConstructor(Flags,null,[s.FieldType,n.FieldType],null)??throw new MissingMethodException("Tooltip constructor");
        var method=new DynamicMethod("CreateTooltipView",typeof(object),[typeof(nint),typeof(nint)],typeof(BorrowedTooltip).Module,true);
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Conv_U);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Newobj,ctor);il.Emit(OpCodes.Ret);create=method.CreateDelegate<Func<nint,nint,object>>();
    }
    private static Func<object,nint> Read(FieldInfo field)
    {
        var method=new DynamicMethod("ReadTooltipArray",typeof(nint),[typeof(object)],typeof(BorrowedTooltip).Module,true);
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,field.DeclaringType!);
        il.Emit(OpCodes.Ldfld,field);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Ret);return method.CreateDelegate<Func<object,nint>>();
    }
    public NativeStringSink.Frame Capture(NativeStringSink sink,object original,Action<object> formatter)
    {
        var numberArray=numbers(original);
        return sink.Capture((StringArrayData*)strings(original),isolated=>formatter(create(isolated,numberArray)),"Item Vendor Location");
    }
}
