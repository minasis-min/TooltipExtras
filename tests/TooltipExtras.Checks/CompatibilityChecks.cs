using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class CompatibilityChecks
{
    static Type Sample(int offset = 0, Type? fieldType = null, int constant = 1, bool changedParameter = false, bool unrelated = false)
    {
        var asm = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CompatibilitySample" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var type = asm.DefineDynamicModule("main").DefineType("CompatibilityFixture", TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed, typeof(ValueType), PackingSize.Size1, unrelated ? 64 : 32);
        var field = type.DefineField("Used", fieldType ?? typeof(int), FieldAttributes.Public); field.SetOffset(offset);
        if (unrelated) { var extra = type.DefineField("Unused", typeof(int), FieldAttributes.Public); extra.SetOffset(24); }
        var method = type.DefineMethod("Operation", MethodAttributes.Public | MethodAttributes.Static, typeof(int), changedParameter ? [typeof(int)] : Type.EmptyTypes);
                var helper = type.DefineMethod("Helper", MethodAttributes.Private | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var helperIl = helper.GetILGenerator(); helperIl.Emit(OpCodes.Ldc_I4, constant); helperIl.Emit(OpCodes.Ret);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Call, helper); il.Emit(OpCodes.Ret);
        return type.CreateType()!;
    }
    public static void Run(Action<bool,string> check)
    {
        var baseline = Sample();
        var field = new CompatibilityEntry("Panel", "ignored", baseline.FullName!, "field", "Used", CompatibilityContract.Value(baseline,"field","Used"));
        var operation = new CompatibilityEntry("Price", "ignored", baseline.FullName!, "operation", "Operation", CompatibilityContract.Value(baseline,"operation","Operation"));
        var entries = new[] {field, operation};
        CompatibilityResult Probe(string group, Type? actual) => CompatibilityContract.Check(entries, group, (_,_)=>actual);
        var recompiled = Sample(unrelated:true);
        check(baseline.Module.ModuleVersionId != recompiled.Module.ModuleVersionId && Probe("Panel",recompiled).Allowed && Probe("Price",recompiled).Allowed, "DLL再生成・無関係な項目追加で互換性を維持");
        check(!Probe("Panel",Sample(offset:4)).Allowed, "使用フィールドの位置変更は停止");
        check(!Probe("Panel",Sample(fieldType:typeof(float))).Allowed, "同じ位置でもフィールド型の変更は停止");
        check(!Probe("Price",Sample(changedParameter:true)).Allowed, "使用メソッド署名変更は停止");
        check(!Probe("Price",Sample(constant:2)).Allowed, "署名が同じでも呼出先の処理変更は停止");
        check(MethodCompatibility.Fingerprint(baseline.GetMethod("Operation")!) == MethodCompatibility.Fingerprint(Sample(constant:2).GetMethod("Operation")!), "ルート処理のIL一致だけでは呼出先変更を見落とすケースを再現");
        check(!Probe("Panel",null).Allowed, "使用型が取得できなければ停止");
        check(Probe("Panel",Sample(constant:2)).Allowed && !Probe("Price",Sample(constant:2)).Allowed, "価格非互換でも共通基盤の判定を維持");
        check(Probe("Panel",Sample(offset:4)).Reason.Contains("CompatibilityFixture.Used") && Probe("Panel",Sample(offset:4)).Reason.Contains("期待="), "Debug理由は変更された項目と期待値を含む");
        check(!CompatibilityContract.Check(entries,"Undefined",(_,_)=>baseline).Allowed, "未定義の機能を許可しない");
    }
}
