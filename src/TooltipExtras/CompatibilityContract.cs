using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace TooltipExtras;

internal sealed record CompatibilityEntry(string Group, string Assembly, string Type, string Kind, string Member, string Expected);
internal sealed record CompatibilityResult(bool Allowed, string Reason)
{
    public static CompatibilityResult Success => new(true, "互換性あり");
}
internal static class CompatibilityContract
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    public static string Value(Type type, string kind, string member) => kind switch
    {
        "field" => Field(type, member),
        "property" => Property(type, member),
        "methods" => Methods(type, member, false),
        "body" => Methods(type, member, true),
        "operation" => Operation(type, member),
        "enum" => Convert.ToInt64(Enum.Parse(type, member)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        "layout" => type.StructLayoutAttribute is { Value: LayoutKind.Explicit } layout ? "explicit:" + layout.Pack : throw new NotSupportedException(type.FullName + ": 明示的な構造配置がありません"),
        _ => throw new NotSupportedException(kind)
    };
    static string Field(Type type, string name)
    {
        var field = type.GetField(name, Flags) ?? throw new MissingFieldException(type.FullName, name);
        var offset = field.GetCustomAttribute<FieldOffsetAttribute>() ?? throw new NotSupportedException(type.FullName + "." + name + ": フィールド位置が取得できません");
        return offset.Value + ":" + MethodCompatibility.Member(field);
    }
    static string Property(Type type, string name)
    {
        var p = type.GetProperty(name, Flags) ?? throw new MissingMemberException(type.FullName, name);
        return MethodCompatibility.Member(p.PropertyType) + ":" + p.CanRead + ":" + p.CanWrite;
    }
    static string Methods(Type type, string name, bool body)
    {
        var methods = type.GetMethods(Flags).Where(m => m.Name == name).OrderBy(MethodCompatibility.Member, StringComparer.Ordinal).ToArray();
        if (methods.Length == 0) throw new MissingMethodException(type.FullName, name);
        return string.Join(";", methods.Select(m => MethodCompatibility.Member(m) + (body ? ":" + MethodCompatibility.Fingerprint(m) : "")));
    }
    static string Operation(Type type, string name)
    {
        var methods = type.GetMethods(Flags).Where(m => m.Name == name).OrderBy(MethodCompatibility.Member, StringComparer.Ordinal).ToArray();
        if (methods.Length == 0) throw new MissingMethodException(type.FullName, name);
        return string.Join(";", methods.Select(m => MethodCompatibility.Member(m) + ":" + MethodCompatibility.CallTree(m)));
    }
    public static CompatibilityResult Check(IEnumerable<CompatibilityEntry> entries, string group, Func<string, string, Type?> resolve)
    {
        var selected = entries.Where(e => e.Group == group).ToArray();
        if (selected.Length == 0) return new(false, "互換性定義がありません: " + group);
        foreach (var entry in selected)
        {
            try
            {
                var type = resolve(entry.Assembly, entry.Type) ?? throw new TypeLoadException(entry.Type);
                var actual = Value(type, entry.Kind, entry.Member);
                if (actual != entry.Expected) return new(false, $"非互換: {entry.Type}.{entry.Member} ({entry.Kind}) / 期待={entry.Expected} / 実際={actual}");
            }
            catch (Exception ex) { return new(false, $"確認失敗: {entry.Type}.{entry.Member} / {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
        }
        return CompatibilityResult.Success;
    }
    public static CompatibilityEntry[] Load() => JsonSerializer.Deserialize<CompatibilityEntry[]>(typeof(CompatibilityContract).Assembly.GetManifestResourceStream("TooltipExtras.CompatibilityContracts.json") ?? throw new InvalidOperationException("互換性定義がありません"))!;
}
