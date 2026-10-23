using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Dalamud.Hooking;

namespace TooltipExtras;

// A temporary array must never reach another native hook. Hold the host's
// reviewed hook-registration lock from ownership validation through formatting.
internal sealed class StringHookOwnership
{
    private readonly System.Threading.Lock sync;
    private readonly IDictionary hooks;
    private readonly Func<object, IDalamudHook> readHook;
    internal StringHookOwnership(System.Threading.Lock sync,IDictionary hooks,Func<object,IDalamudHook> readHook)
    {this.sync=sync;this.hooks=hooks;this.readHook=readHook;}
    public StringHookOwnership()
    {
        var assembly=typeof(Hook<>).Assembly;
        var result=CompatibilityContract.Check(CompatibilityContract.Load(),"StringHookHost",(_,name)=>assembly.GetType(name));
        if(!result.Allowed)throw new NotSupportedException("文字列出力の所有権を確認できません: "+result.Reason);
        const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
        var manager=assembly.GetType("Dalamud.Hooking.Internal.HookManager",true)!;
        sync=manager.GetProperty("HookEnableSyncRoot",flags)?.GetValue(null) as System.Threading.Lock??throw new MissingMemberException("Hook lock");
        hooks=manager.GetProperty("TrackedHooks",flags)?.GetValue(null) as IDictionary??throw new MissingMemberException("Hook registry");
        var info=assembly.GetType("Dalamud.Hooking.Internal.HookInfo",true)!;
        var getter=info.GetProperty("Hook",BindingFlags.Instance|BindingFlags.NonPublic)?.GetMethod??throw new MissingMemberException("Hook owner");
        var method=new DynamicMethod("StringHookOwner",typeof(IDalamudHook),[typeof(object)],typeof(StringHookOwnership).Module,true);
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,info);il.Emit(OpCodes.Call,getter);il.Emit(OpCodes.Ret);
        readHook=method.CreateDelegate<Func<object,IDalamudHook>>();
    }
    public System.Threading.Lock.Scope Enter()=>sync.EnterScope();
    public void Verify(nint address,IDalamudHook? own)
    {
        if(!sync.IsHeldByCurrentThread)throw new InvalidOperationException("Hook ownership requires host lock");
        var found=own==null;
        foreach(DictionaryEntry entry in hooks)
        {
            var hook=readHook(entry.Value!);
            if(ReferenceEquals(hook,own)){found=true;continue;}
            if(!hook.IsDisposed&&hook.Address==address)throw new NotSupportedException("別のプラグインが同じ文字列出力先を監視しています");
        }
        if(!found||own!=null&&(own.IsDisposed||!own.IsEnabled||own.Address!=address))
            throw new NotSupportedException("文字列出力の所有権が変更されました");
    }
}
