using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

// Read only the current visible native text. No item sheets, text copies or per-item cache.
// Window/control hints and foreign append nodes are not part of the native body.
internal static unsafe class NativeBodyBounds
{
    public static bool TryMinimum(AtkResNode** nodes,int count,AtkResNode* root,AtkResNode* footer,out ushort height)
    {
        height=0;
        if(nodes==null||count is <1 or >1024||root==null||footer==null)return false;
        float bottom=0;var measured=false;
        for(var i=0;i<count;i++)
        {
            var text=nodes[i];
            if(text==null||text->Type!=NodeType.Text||text->NodeId is 0 or >=10000)continue;
            var bytes=((AtkTextNode*)text)->NodeText.BufUsed;
            if(bytes<=1)continue;
            var p=text;float y=text->Height;var skip=false;var depth=0;
            for(;p!=null&&p!=root&&depth<32;depth++,p=p->ParentNode)
            {
                if(p==footer||p->NodeId>=10000||(p->NodeFlags&NodeFlags.Visible)==0){skip=true;break;}
                // Addon scale is outside this local coordinate space. Unknown per-node
                // transforms are not guessed when deciding whether a shrink is safe.
                if(p->ScaleY!=1||p->Rotation!=0||!float.IsFinite(p->Y))return false;
                y+=p->Y;
            }
            if(skip)continue; if(p==null)return false;
            if(p!=root||bytes>32768||text->Height==0||!float.IsFinite(y)||y<0||y>15992)return false;
            measured=true;bottom=Math.Max(bottom,y);
        }
        if(!measured)return false;
        height=(ushort)Math.Max(24,Math.Ceiling(bottom+8));return true;
    }
    public static PreviewGeometry Fit(PreviewGeometry value,ushort minimum,int previewMargin=0)
    {
        // The producer adds its bottom margin as well as image height. Remove that
        // margin once, while keeping the currently measured native body and padding.
        var height=(ushort)Math.Max(minimum,value.Window-previewMargin);
        return new(height,height,height,height-20);
    }
}
