using System.Collections;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class SimpleGlamourNameAdapter(Func<bool> allowed,Func<(ulong Id,nint Addon)> context) : IDisposable
{
    private const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    private sealed record Saved(ulong Item,nint Addon,nint Vanilla,nint Replacement,uint Id,byte[] ItemText,byte[] GlamourText,byte[] Content);
    private Saved? saved;
    private readonly RelaySnapshot display=new();
    private uint itemColor,glamourColor;
    private bool colored;
    public long RequestsKept {get;private set;}
    public long Restores {get;private set;}
    public long Draws {get;private set;}
    public string LastStage {get;private set;}="待機";
    public object Colors=>new {Sampled=colored,ItemRgba=itemColor,GlamourRgba=glamourColor};
    private Func<bool>? sourceEnabled;
    public string Status {get;private set;}="待機中";
    public long Captures {get;private set;}
    public long Rejections {get;private set;}
    private static byte[]? Read(StringArrayData* strings,int field)
    {
        if(strings==null||strings->StringArray==null||strings->AtkArrayData.Size<=field)return null;
        byte* p=strings->StringArray[field];if(p==null)return null;
        var length=0;while(length<32768&&p[length]!=0)length++;
        return length is 0 or 32768?null:new ReadOnlySpan<byte>(p,length).ToArray();
    }
    public void Capture(object source,Func<bool> enabled,Action<nint,nint> original,nint numbers,nint strings)
    {
        var key=context();byte[]? item=null,glamour=null;
        try{if(allowed()&&key.Id!=0){item=Read((StringArrayData*)strings,0);glamour=Read((StringArrayData*)strings,1);}}
        catch{Status="元表示を維持: 名前配列の読取り失敗";}
        try{original(numbers,strings);}catch(Exception ex){Clear();Status="元の名前表示処理で例外: "+ex.GetBaseException().Message;return;}
        try
        {
            if(!allowed()||context()!=key||item==null||glamour==null){Clear();return;}
            var custom=source.GetType().Assembly.GetType("SimpleTweaksPlugin.Utility.CustomNodes",true)!;
            var ids=(IDictionary)custom.GetField("NodeIds",Flags)!.GetValue(null)!;
            if(ids["GlamItemNames::GlamNameReplacementText#0"] is not uint id||!Nodes((AtkUnitBase*)key.Addon,id,out var vanilla,out var replacement))
            {Clear();Rejections++;Status="元表示を維持: 名前ノードが未確認";return;}
            var content=replacement->NodeText.AsSpan().ToArray();
            if(!replacement->AtkResNode.IsVisible()||!GlamourNameText.Matches(item,glamour,content))
            {Clear();Rejections++;Status="元表示を維持: 名前の生成結果が不一致";return;}
            sourceEnabled=enabled;saved=new(key.Id,key.Addon,(nint)vanilla,(nint)replacement,id,item,glamour,content);
            display.Begin(key.Id,key.Addon);display.Set(0,content);colored=false;LastStage="生成直後に捕捉";
            replacement->AtkResNode.ToggleVisibility(false);vanilla->AtkResNode.ToggleVisibility(true);
            Captures++;Status="アイテム名・ミラプリ名を分離済み";
        }
        catch(Exception ex)
        {
            if(saved is {} s&&Match(s,out var vanilla,out var replacement))
            {
                try{vanilla->AtkResNode.ToggleVisibility(false);}catch{}
                try{replacement->AtkResNode.ToggleVisibility(true);}catch{}
            }
            Clear();Status="元の名前表示へ戻して停止: "+ex.GetBaseException().Message;
        }
    }
    private static bool Nodes(AtkUnitBase* addon,uint id,out AtkTextNode* vanilla,out AtkTextNode* replacement)
    {
        vanilla=replacement=null;if(addon==null)return false;
        var v=NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,33,out _);
        var r=NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,id,out _);
        if(v==null||r==null||v->Type!=NodeType.Text||r->Type!=NodeType.Text||v->ParentNode==null||v->ParentNode!=r->ParentNode)return false;
        vanilla=(AtkTextNode*)v;replacement=(AtkTextNode*)r;return true;
    }
    private bool Match(Saved s,out AtkTextNode* vanilla,out AtkTextNode* replacement)
    {
        vanilla=replacement=null;var key=context();
        return key==(s.Item,s.Addon)&&Nodes((AtkUnitBase*)s.Addon,s.Id,out vanilla,out replacement)
            &&(nint)vanilla==s.Vanilla&&(nint)replacement==s.Replacement&&replacement->NodeText.AsSpan().SequenceEqual(s.Content);
    }
    public void PreDraw()
    {
        if(!allowed()){Restore();return;}
        if(sourceEnabled?.Invoke()!=true){Clear();return;}
        if(saved is {} s&&Match(s,out var vanilla,out var replacement))
        {
            var normal=vanilla->TextColor.RGBA;var glam=replacement->TextColor.RGBA;
            if(!colored||normal!=itemColor||glam!=glamourColor)
            {
                display.Set(0,GlamourNameText.WithColors(s.ItemText,s.GlamourText,normal,glam));
                itemColor=normal;glamourColor=glam;colored=true;
            }
            replacement->AtkResNode.ToggleVisibility(false);vanilla->AtkResNode.ToggleVisibility(true);
            Draws++;LastStage="描画前に元ノードの色を取得・分離を適用";
        }
    }
    public void Requested(ulong item,nint addon)
    {
        if(saved is {} s&&(s.Item!=item||s.Addon!=addon)){Clear();return;}
        display.Begin(item,addon);
        if(saved!=null){RequestsKept++;LastStage="要求更新: 取得済みの名前を保持";}
    }
    public byte[]? Get(ulong item,nint addon)=>allowed()&&sourceEnabled?.Invoke()==true?display.Get(item,addon):null;
    public void Restore()
    {
        if(saved is {} s&&sourceEnabled?.Invoke()==true&&Match(s,out var vanilla,out var replacement)
            &&!replacement->AtkResNode.IsVisible()&&vanilla->AtkResNode.IsVisible())
        {vanilla->AtkResNode.ToggleVisibility(false);replacement->AtkResNode.ToggleVisibility(true);Restores++;LastStage="元プラグインの表示へ復元";}
        Clear();
    }
    public void Clear(){saved=null;sourceEnabled=null;display.Clear();colored=false;}
    public void Dispose()=>Restore();
}
