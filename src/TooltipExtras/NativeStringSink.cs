using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Reflection.Emit;
namespace TooltipExtras;

// Active only around a reviewed synchronous formatter, for its exact array and
// thread. Every other native write forwards unchanged. Reviewed formatters use
// a temporary view; the live string array and its subscriptions are untouched.
internal sealed unsafe class NativeStringSink : IDisposable
{
    // Native C++ bool is one byte. The generated public delegate does not carry
    // MarshalAs metadata, so it cannot be used for a global reverse-P/Invoke hook.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SetStringNative(StringArrayData* array,int field,CStringPointer value,
        [MarshalAs(UnmanagedType.I1)] bool readBeforeWrite,[MarshalAs(UnmanagedType.I1)] bool managed,[MarshalAs(UnmanagedType.I1)] bool suppressUpdates);
    private readonly IGameInteropProvider? interop;
    private readonly IFramework? framework;
    private readonly object gate=new();
    private Hook<SetStringNative>? hook;
    private SetStringNative? forward;
    private Frame? current;
    private volatile bool disposeRequested;
    private volatile bool disconnectRequested;
    private bool disconnectQueued;
    private nint hookAddress;
    private StringHookOwnership? ownership;
    public RejectedTextLog? Rejected {get;set;}
    public NativeStringSink(IGameInteropProvider interop,IFramework framework) { this.interop = interop;this.framework=framework; }
    internal NativeStringSink(StringArrayData.Delegates.SetValue forward) { this.forward=(a,f,v,r,m,s)=>forward(a,f,v,r,m,s); }
    public Func<bool> VerifyNativeSetter(Assembly assembly)
    {
        var type=assembly.GetType(typeof(StringArrayData).FullName+"+MemberFunctionPointers",true)!;
        var get=type.GetProperty("SetValue",BindingFlags.Public|BindingFlags.Static)?.GetMethod??throw new MissingMethodException("String setter address");
        if(!get.ReturnType.IsFunctionPointer||get.GetParameters().Length!=0)throw new NotSupportedException("文字列出力のアドレス型が未対応です");
        var method=new DynamicMethod("StringSetterAddress",typeof(nint),Type.EmptyTypes,typeof(NativeStringSink).Module,true);
        var il=method.GetILGenerator();il.Emit(OpCodes.Call,get);il.Emit(OpCodes.Conv_I);il.Emit(OpCodes.Ret);
        var read=method.CreateDelegate<Func<nint>>();var address=read();
        if(address==0||address!=(nint)StringArrayData.MemberFunctionPointers.SetValue)throw new NotSupportedException("元プラグインの文字列出力先が一致しません");
        return ()=>read()==address&&(nint)StringArrayData.MemberFunctionPointers.SetValue==address;
    }
    public void Connect()
    {
        lock(gate)
        {
        if(disposeRequested||disconnectRequested)throw new ObjectDisposedException(nameof(NativeStringSink));
        if(framework?.IsInFrameworkUpdateThread==false)throw new InvalidOperationException("Framework thread required");
        if (forward != null) return;
        var address = (nint)StringArrayData.MemberFunctionPointers.SetValue;
        if (address == 0) throw new NotSupportedException("文字列出力先が未解決です");
        ownership ??= new StringHookOwnership();
        using var held=ownership.Enter();
        ownership.Verify(address,null);
        hook = interop!.HookFromAddress<SetStringNative>(address, SetValue);forward=hook.Original;hookAddress=address;
        try { hook.Enable();ownership.Verify(address,hook); } catch { hook.Dispose(); hook = null;forward=null; throw; }
        }
    }
    public Frame Begin(nint array)
    {
        lock(gate)
        {
        if(framework?.IsInFrameworkUpdateThread==false)throw new InvalidOperationException("Framework thread required");
        if (disposeRequested || disconnectRequested || forward == null || array == 0 || current != null) throw new InvalidOperationException("文字列分離スコープが利用できません");
        if(hook!=null&&(nint)StringArrayData.MemberFunctionPointers.SetValue!=hookAddress)throw new NotSupportedException("文字列出力先が変更されました");
        var frame=new Frame(this,array,Environment.CurrentManagedThreadId);Volatile.Write(ref current,frame);return frame;
        }
    }
    public Frame Capture(StringArrayData* source,Action<nint> formatter)=>Capture(source,formatter,"文字列生成");
    public Frame Capture(StringArrayData* source,Action<nint> formatter,string producer)
    {
        lock(gate)
        {
        // The fake/testing backend has no native hook chain. Production must own
        // the entire setter entry point for the duration of this synchronous call.
        if(ownership==null&&interop!=null)throw new InvalidOperationException("文字列出力が未接続です");
        if(ownership!=null)
        {
            using var held=ownership.Enter();
            ownership.Verify(hookAddress,hook??throw new InvalidOperationException("文字列出力が未接続です"));
            return CaptureOwned(source,formatter,producer);
        }
        return CaptureOwned(source,formatter,producer);
        }
    }
    private Frame CaptureOwned(StringArrayData* source,Action<nint> formatter,string producer)
    {
        if(framework?.IsInFrameworkUpdateThread==false)throw new InvalidOperationException("Framework thread required");
        if(source==null||source->StringArray==null||source->AtkArrayData.Size is <=0 or >256)throw new NotSupportedException("文字列配列の形状不一致");
        // Only the audited getter/setter fields are supplied. No live subscriber,
        // managed allocation table or native array lifetime is borrowed.
        var values=new ReadOnlySpan<CStringPointer>(source->StringArray,source->AtkArrayData.Size).ToArray();
        fixed(CStringPointer* data=values)
        {
            StringArrayData isolated=default;isolated.AtkArrayData.Size=values.Length;isolated.StringArray=data;
            var frame=Begin((nint)(&isolated));frame.Isolated=true;
            using(frame)
            {
                try{formatter((nint)(&isolated));}
                catch{Rejected?.Writes(producer,"生成処理が例外で終了しました",frame.Writes);throw;}
                if(frame.Failure.Length>0)Rejected?.Writes(producer,"文字列の取得失敗: "+frame.Failure,frame.Writes);
            }
            return frame; // Owns managed bytes only; all temporary pins are released.
        }
    }
    internal void SetValue(StringArrayData* array, int field, CStringPointer value, bool readBeforeWrite, bool managed, bool suppressUpdates)
    {
        var next=forward;var scope = Volatile.Read(ref current);
        if (scope == null || scope.Array != (nint)array || scope.Thread != Environment.CurrentManagedThreadId)
        { next!(array, field, value, readBeforeWrite, managed, suppressUpdates); return; }
        // A malformed owned write is withheld as well. Never leak a partial
        // formatter into native output because its panel copy could not be made.
        try
        {
            if (array->StringArray==null || field < 0 || field >= array->AtkArrayData.Size || field >= 256) throw new NotSupportedException("追記欄の範囲外");
            if (!scope.Before.ContainsKey(field))
            {
                byte* previous = array->StringArray[field];
                if (previous == null) scope.Before[field] = null;
                else { var length = 0; while (length < 32768 && previous[length] != 0) length++;
                    if (length == 32768) throw new NotSupportedException("元文字列の上限");
                    scope.Before[field] = new ReadOnlySpan<byte>(previous, length).ToArray(); }
            }
            byte* p = value;
            if (p == null) { scope.Writes[field] = null;if(scope.Isolated)array->StringArray[field]=(byte*)null;return; }
            var size = 0; while (size < 32768 && p[size] != 0) size++;
            if (size == 32768 || scope.Writes.Count >= 256) throw new NotSupportedException("追記文字列の上限");
            scope.Writes[field] = new ReadOnlySpan<byte>(p, size).ToArray();
            if(scope.Isolated)
            {
                scope.BufferedBytes+=size+1;
                if(scope.Pins.Count>=256||scope.BufferedBytes>131072)throw new NotSupportedException("一時文字列の上限");
                var terminated=new byte[size+1];scope.Writes[field]!.CopyTo(terminated,0);
                var pin=GCHandle.Alloc(terminated,GCHandleType.Pinned);
                try{scope.Pins.Add(pin);}catch{pin.Free();throw;}
                array->StringArray[field]=(byte*)pin.AddrOfPinnedObject();
            }
        }
        catch (Exception ex) { scope.Failure = ex.GetBaseException().GetType().Name; }
    }
    internal sealed class Frame(NativeStringSink owner, nint array, int thread) : IDisposable
    {
        public nint Array { get; private set; } = array;
        public int Thread { get; } = thread;
        public Dictionary<int, byte[]?> Writes { get; } = [];
        public Dictionary<int, byte[]?> Before { get; } = [];
        public string Failure { get; internal set; } = "";
        internal bool Isolated;
        internal int BufferedBytes;
        internal List<GCHandle> Pins {get;}=[];
        public void Dispose()
        {
            lock(owner.gate)
            {
            if (!ReferenceEquals(owner.current, this)) return;
            Volatile.Write(ref owner.current,null);
            Array=0;
            foreach(var pin in Pins)pin.Free();Pins.Clear();
            if(owner.disposeRequested||owner.disconnectRequested)owner.Disconnect();
            }
        }
    }
    public void Dispose()
    {
        disposeRequested=true;
        Disconnect();
    }
    public void Disconnect()
    {
        lock(gate)
        {
        disconnectRequested=true;
        if (current != null) return; // The formatter's finally scope completes disposal.
        if(hook==null&&forward==null){disconnectRequested=false;return;}
        if(framework?.IsInFrameworkUpdateThread==false)
        {
            if(!disconnectQueued)
            {
                disconnectQueued=true;
                _=framework.RunOnFrameworkThread(()=>
                {
                    lock(gate){disconnectQueued=false;if(disconnectRequested)Disconnect();}
                });
            }
            return;
        }
        hook?.Dispose(); hook = null;forward=null;
        disconnectRequested=false;
        }
    }
}
