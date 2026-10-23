using System.Collections;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class SimpleGlamourNameAdapter(Func<bool> allowed,Func<(ulong Id,nint Addon)> context) : IDisposable
{
    private const BindingFlags Flags=BindingFlags.Static|BindingFlags.NonPublic;
    private sealed record Saved(ulong Item,nint Addon,nint Vanilla,nint Replacement,uint Id,byte[] ItemText,byte[] GlamourText,byte[] Content);
    private Saved? saved;
    private Type? sourceType;
    private FieldInfo? nodeIds;
    private IDictionary NodeIds(object source)
    {
        if(sourceType!=source.GetType())
        {
            var type=source.GetType().Assembly.GetType("SimpleTweaksPlugin.Utility.CustomNodes",true)!;
            nodeIds=type.GetField("NodeIds",Flags)??throw new MissingFieldException(type.FullName,"NodeIds");
            sourceType=source.GetType();
        }
        return (IDictionary)nodeIds!.GetValue(null)!;
    }
    private sealed record Direct(ulong Item,nint Addon,byte[] ItemText,byte[] GlamourText);
    private Direct? direct;
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
    private static byte[]? Read(StringArrayData* strings,int field,byte[]? previous=null)
    {
        if(strings==null||strings->StringArray==null||strings->AtkArrayData.Size<=field)return null;
        byte* p=strings->StringArray[field];if(p==null)return null;
        var length=0;while(length<32768&&p[length]!=0)length++;
        if(length is 0 or 32768)return null;
        var bytes=new ReadOnlySpan<byte>(p,length);
        return previous!=null&&bytes.SequenceEqual(previous)?previous:bytes.ToArray();
    }
    private static AtkTextNode* NativeName(nint address)
    {
        var addon=(AtkUnitBase*)address;
        if(addon==null||addon->UldManager.NodeListCount>1024)return null;
        var node=NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,33,out _);
        return node!=null&&node->Type==NodeType.Text?(AtkTextNode*)node:null;
    }
    public bool CaptureExclusive(object source,Func<bool> enabled,nint numbers,nint strings)
    {
        var key=context();
        if(!allowed()||!enabled()||key.Id==0||key.Addon==0){Clear();return true;}
        try
        {
            var vanilla=NativeName(key.Addon);if(vanilla==null){Clear();return false;}
            // Hide a previously created source node (e.g. switching from legacy
            // mode). This path never creates a replacement or changes its text.
            if(NodeIds(source) is {} ids
                &&ids["GlamItemNames::GlamNameReplacementText#0"] is uint id
                &&Nodes((AtkUnitBase*)key.Addon,id,out var standard,out var replacement))
            {replacement->AtkResNode.ToggleVisibility(false);standard->AtkResNode.ToggleVisibility(true);}
            var cached=direct is {} current&&(current.Item,current.Addon)==key?current:null;
            var item=Read((StringArrayData*)strings,0,cached?.ItemText);var glamour=Read((StringArrayData*)strings,1,cached?.GlamourText);
            if(item==null||glamour==null){Clear();return true;}
            var color=vanilla->TextColor.RGBA;
            if(context()!=key||!allowed()){Clear();return false;}
            if(direct is {} previous&&previous.Item==key.Id&&previous.Addon==key.Addon
                &&colored&&itemColor==color&&previous.ItemText.AsSpan().SequenceEqual(item)&&previous.GlamourText.AsSpan().SequenceEqual(glamour))
            {sourceEnabled=enabled;Captures++;return true;}
            var bytes=GlamourNameText.WithColors(item,glamour,color,color);
            if(context()!=key||!allowed()){Clear();return false;}
            saved=null;direct=new(key.Id,key.Addon,item,glamour);sourceEnabled=enabled;
            display.Begin(key.Id,key.Addon);display.Set(0,bytes);itemColor=glamourColor=color;colored=true;
            Captures++;LastStage="現在の名前データから直接分離";Status="名前の追加ノードを作らずTEへ分離済み";return true;
        }
        catch(Exception ex){Clear();Rejections++;Status="名前の取得を保留: "+ex.GetBaseException().GetType().Name;return false;}
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
            var ids=NodeIds(source);
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
        if(direct is {} current)
        {
            if(context()!=(current.Item,current.Addon)){Clear();return;}
            var nativeName=NativeName(current.Addon);if(nativeName==null){Clear();return;}
            var color=nativeName->TextColor.RGBA;
            if(!colored||color!=itemColor){display.Set(0,GlamourNameText.WithColors(current.ItemText,current.GlamourText,color,color));itemColor=glamourColor=color;colored=true;}
            Draws++;LastStage="現在の標準名の色を取得";return;
        }
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
        if(direct is {} current&&(current.Item!=item||current.Addon!=addon)){Clear();return;}
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
    public void Clear(){saved=null;direct=null;sourceEnabled=null;display.Clear();colored=false;}
    public void Dispose(){Restore();sourceType=null;nodeIds=null;}
}
