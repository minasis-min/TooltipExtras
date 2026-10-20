namespace TooltipExtras;

internal sealed class TextTransferPlan
{
    private readonly int[] fields,changed;
    private readonly byte[]?[] before,after;
    private TextTransferPlan(int[] fields,int[] changed,byte[]?[] before,byte[]?[] after)
    {this.fields=fields;this.changed=changed;this.before=before;this.after=after;}
    public static TextTransferPlan FlagsOnly()=>new([],[],[],[]);
    public static TextTransferPlan? Create(int[] fields,byte[]?[] before,byte[]?[] after,bool appendOnly,out string reason,int allowNewFieldsFrom=int.MaxValue)
    {
        reason="";
        if(fields.Length!=before.Length||fields.Length!=after.Length){reason="欄数不一致";return null;}
        var changed=new List<int>();
        for(var i=0;i<fields.Length;i++)
        {
            if(before[i]==null&&after[i]!=null&&!appendOnly&&allowNewFieldsFrom!=int.MaxValue&&fields[i]>=allowNewFieldsFrom){changed.Add(i);continue;}
            if((before[i]==null)!=(after[i]==null)){reason="null欄の変更";return null;}
            if(before[i]==null||before[i]!.AsSpan().SequenceEqual(after[i]))continue;
            if(appendOnly&&!after[i]!.AsSpan().StartsWith(before[i])){reason="追記ではない変更";return null;}
            changed.Add(i);
        }
        return changed.Count==0?null:new(fields,changed.ToArray(),before,after);
    }
    public IEnumerable<int> ChangedFields=>changed.Select(i=>fields[i]);
    public IEnumerable<byte[]> Output(bool appendOnly)
    {
        foreach(var i in changed)yield return appendOnly?after[i]!.AsSpan(before[i]!.Length).ToArray():after[i]!;
    }
    public bool Apply(Action<int,byte[]?> write,Func<int,byte[]?> read,Action<int>? count,int beforeCount,int afterCount,out string failure,Action<bool>? layout=null)
        =>Apply(write,read,count,beforeCount,afterCount,out failure,out _,layout);
    public bool Apply(Action<int,byte[]?> write,Func<int,byte[]?> read,Action<int>? count,int beforeCount,int afterCount,out string failure,out bool rollbackVerified,Action<bool>? layout=null)
    {
        failure="";rollbackVerified=false;
        try
        {
            foreach(var i in changed)write(fields[i],before[i]!);
            count?.Invoke(beforeCount);
            layout?.Invoke(true);
            foreach(var i in changed)
            {
                var actual=read(fields[i]);if((actual==null)!=(before[i]==null)||actual!=null&&!actual.AsSpan().SequenceEqual(before[i]))throw new InvalidOperationException("元欄の書戻し不一致");
            }
            return true;
        }
        catch(Exception ex)
        {
            failure=ex.GetBaseException().Message;
            rollbackVerified=true;
            foreach(var i in changed)try{write(fields[i],after[i]!);}catch{rollbackVerified=false;failure+=" / 元表示の復元失敗";}
            try{count?.Invoke(afterCount);}catch{rollbackVerified=false;failure+=" / 行数の復元失敗";}
            try{layout?.Invoke(false);}catch{rollbackVerified=false;failure+=" / 配置の復元失敗";}
            foreach(var i in changed)try
            {
                var actual=read(fields[i]);
                if((actual==null)!=(after[i]==null)||actual!=null&&!actual.AsSpan().SequenceEqual(after[i]))throw new InvalidOperationException();
            }
            catch{rollbackVerified=false;failure+=" / 元表示の読戻し不一致";}
            return false;
        }
    }
}
internal static class SourceStatCount
{
    // Simple uses Take(count) for existing rows; counts above eight do not prevent
    // rewriting those rows. Only a producer that adds rows must stay in its 0..8 range.
    public static bool CanRestore(int before,int after)=>before==after || before>=0&&before<=8&&after>=before&&after<=8;
}
internal readonly record struct PreviewGeometry(ushort Window,ushort Surface,ushort Root,float Footer)
{
    public bool TryCollapseImage(PreviewGeometry generated,PreviewGeometry previousBase,float rowY,ushort rowHeight,int margin,bool allowRootSync,out PreviewGeometry collapsed)
    {
        collapsed=default;
        if(!generated.IsGeneratedAfter(new(24,24,24,4),rowY,rowHeight,margin))return false;
        if(MatchesCaptured(generated,previousBase,allowRootSync))
        {
            // The callback's incoming window may still belong to the previous item.
            // Invert only this generated image; never choose an incoming/old height.
            if(Window<=rowHeight||Window-rowHeight<24||Footer-rowHeight<0)return false;
            var height=(ushort)(Window-rowHeight);
            collapsed=new(height,height,height,Footer-rowHeight);
            return true;
        }
        return TryFinalizedBase(generated,rowY,rowHeight,out collapsed);
    }    // Some game updates commit the base window height again after Simple's image
    // callback, but leave its footer/image outside that base window. In this exact
    // shape geometry is safe to replace using this generated image and current text bounds.
    public bool TryFinalizedBase(PreviewGeometry generated,float rowY,ushort rowHeight,out PreviewGeometry collapsed)
    {
        collapsed=default;
        if(Window<24||Window>=generated.Window||Surface!=Window||Root!=Window
            ||Footer!=generated.Footer||generated.Footer!=generated.Window-20
            ||!float.IsFinite(rowY)||rowY<0||rowHeight==0||rowY>=Window
            ||rowY+rowHeight<=Window||rowY+rowHeight>generated.Window)return false;
                // Even synchronized component heights can still be the previous tooltip.
        // Derive the result from this image, then let visible native text guard it.
        if(generated.Window<=rowHeight||generated.Window-rowHeight<24)return false;
        var height=(ushort)(generated.Window-rowHeight);
        collapsed=new(height,height,height,height-20);return true;
    }
    public bool MatchesCaptured(PreviewGeometry captured,PreviewGeometry before,bool allowRootSync)
        =>this==captured || allowRootSync&&captured.Root==before.Root&&this==(captured with {Root=captured.Window});
    public bool IsGeneratedAfter(PreviewGeometry before,float rowY,ushort rowHeight,int margin)
        => before.Window>=24&&before.Window<=16000&&Window>=24&&Window<=16000&&Surface==Window&&Footer==Window-20
        &&float.IsFinite(rowY)&&rowY>=0&&rowHeight>0&&rowHeight<=2048&&rowY+rowHeight+margin<=16000
        &&Window==(ushort)(rowY+rowHeight+margin);
}

internal readonly record struct LayoutStamp(ushort Height,float Footer)
{
    public bool TryFollow(LayoutStamp before,LayoutStamp after,out LayoutStamp next)
    {
        next=this;
        if(this!=before||after.Height<24||after.Height>Height||!float.IsFinite(after.Footer)
            ||Math.Abs((Height-after.Height)-(Footer-after.Footer))>0.01f)return false;
        next=after;return true;
    }
}
