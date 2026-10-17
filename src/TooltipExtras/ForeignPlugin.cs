using System.Reflection;
using Dalamud.Plugin;
namespace TooltipExtras;

internal static class ForeignPlugin
{
    public static object? Find(IDalamudPluginInterface pi,string internalName)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var matches=pi.InstalledPlugins.Where(p=>p.InternalName==internalName&&p.IsLoaded).ToArray();
        if(matches.Length==0)return null;
        if(matches.Length!=1)throw new InvalidOperationException("ロード済みプラグインが一意ではありません: "+internalName);
        var exposed=matches[0];
        var localField=exposed.GetType().GetFields(flags).Single(f=>f.FieldType.FullName=="Dalamud.Plugin.Internal.Types.LocalPlugin");
        var local=localField.GetValue(exposed)!;
        for(var type=local.GetType();type!=null;type=type.BaseType)
            if(type.GetField("instance",flags|BindingFlags.DeclaredOnly) is {} field)return field.GetValue(local);
        throw new MissingFieldException("LocalPlugin.instance");
    }
    public static Assembly SimpleAssembly(IDalamudPluginInterface pi)
    {
        var plugin=Find(pi,"SimpleTweaksPlugin")??throw new InvalidOperationException("Simple Tweaksの実インスタンス準備待ち");
        if(plugin.GetType().FullName!="SimpleTweaksPlugin.SimpleTweaksPlugin")throw new NotSupportedException("Simple Tweaksの実型が未対応");
        return plugin.GetType().Assembly;
    }
}
