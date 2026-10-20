using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Runtime.InteropServices;
namespace TooltipExtras;

internal sealed class DeferredTooltipPresentationException(string message):NotSupportedException(message);

internal static unsafe class DailyRoutineLayout
{
    internal readonly record struct Geometry(int Field,ushort NodeBefore,ushort GroupBefore,ushort DrawBefore,ushort NodeAfter,ushort GroupAfter,int BaselineTrailingLines);
    internal readonly record struct LiveGeometry(float DescriptionY,ushort GroupHeight,float TextY,ushort TextHeight,ushort DrawHeight,ushort Width,float ShopY,ushort ShopHeight,bool DescriptionVisible,bool ShopVisible,bool CrafterVisible,bool DrawMeasured,
        float PriceY,ushort PriceHeight,float PriceTextY,ushort PriceTextHeight,bool PriceVisible,float CrafterY,ushort CrafterHeight,int TrailingLines);
    public static LiveGeometry? Inspect(nint address,bool measure=true)
    {
        var addon=(AddonItemDetail*)address;
        if(addon==null)return null;
        var group=addon->DescriptionGroup;var node=addon->DescriptionText;var shop=addon->ShopSellingPriceGroup;var price=addon->MarketableGroup;var priceText=addon->MarketableText;var crafter=addon->CrafterNameText;
        if(group==null||node==null||shop==null||price==null||priceText==null||node->AtkResNode.ParentNode!=group||node->AtkResNode.Type!=NodeType.Text)return null;
        ushort width=0,height=0;if(measure)node->GetTextDrawSize(&width,&height);
        return new(group->Y,group->Height,node->AtkResNode.Y,node->AtkResNode.Height,height,node->AtkResNode.Width,
            shop->Y,shop->Height,group->IsVisible(),shop->IsVisible(),crafter!=null&&crafter->AtkResNode.IsVisible(),measure,
            price->Y,price->Height,priceText->AtkResNode.Y,priceText->AtkResNode.Height,price->IsVisible(),crafter==null?0:crafter->AtkResNode.Y,crafter==null?(ushort)0:crafter->AtkResNode.Height,
            measure?DailyRoutineText.TrailingLines(node->NodeText.AsSpan()):-1);
    }

    public static int[] PresentationFields(IReadOnlyList<int> fields)=>fields.Where(field=>field is 2 or 13 or 35).Distinct().ToArray();
    public static ushort ResizeGroup(ushort group,ushort oldText,ushort newText)
    {
        var height=(int)group-oldText+newText;
        if(group<oldText||height is <0 or >16000)throw new NotSupportedException("本文グループの高さが未対応です");
        return (ushort)height;
    }
    public static TextFlags FlowFlags(TextFlags flags)=>(flags|TextFlags.WordWrap|TextFlags.MultiLine)&~TextFlags.Ellipsis;
    public static Action<bool>? PrepareEmptyShop(nint address,NumberArrayData* numbers,StringArrayData* strings,bool reviewedNpc)
    {
        var addon=(AddonItemDetail*)address;
        if(addon==null||numbers==null||numbers->IntArray==null||numbers->AtkArrayData.Size<=5||strings==null||strings->StringArray==null||strings->AtkArrayData.Size<=63)return null;
        var group=addon->ShopSellingPriceGroup;var node=addon->ShopSellingPriceText;
        if(group==null||node==null||node->AtkResNode.Type!=NodeType.Text||node->AtkResNode.ParentNode!=group)return null;
        byte* text=strings->StringArray[63];var visible=group->IsVisible();
        if(!DailyRoutineText.EmptyShopCandidate(reviewedNpc,text==null||text[0]==0,node->NodeText.AsSpan().IsEmpty,node->AtkResNode.IsVisible(),visible,numbers->IntArray[5]))return null;
        return restored=>
        {
            group->ToggleVisibility(restored?false:visible);
            addon->UpdateGroupPositions(numbers,strings);
            if(group->IsVisible()!=(restored?false:visible))throw new InvalidOperationException("空の販売価格欄の可視状態不一致");
        };
    }
    internal sealed class PinnedInput : IDisposable
    {
        private GCHandle handle;
        public nint Address=>handle.IsAllocated?handle.AddrOfPinnedObject():throw new ObjectDisposedException(nameof(PinnedInput));
        public PinnedInput(byte[] terminated)
        {
            if(terminated.Length==0||terminated[^1]!=0)throw new ArgumentException("NUL終端が必要です",nameof(terminated));
            handle=GCHandle.Alloc(terminated,GCHandleType.Pinned);
        }
        public void Dispose(){if(handle.IsAllocated)handle.Free();}
    }

    // Omen can materialize all these fields before returning. Restoring arrays
    // alone leaves the rendered text behind, including nonempty descriptions.
    // Reconcile only selected nodes, without rerunning any tooltip generators.
    public static Action<bool>? Prepare(nint address,NumberArrayData* numbers,StringArrayData* strings,
        IReadOnlyList<int> fields,IReadOnlyList<byte[]?> baseline,Action<Geometry>? observe=null,IReadOnlyList<byte[]?>? generated=null)
    {
        var addon=(AddonItemDetail*)address;
        var edits=new List<Edit>();
        foreach(var field in PresentationFields(fields))
        {
            if(addon==null)throw new NotSupportedException("表示ノードが未準備です");
            var node=field switch {2=>addon->CategoryText,13=>addon->DescriptionText,_=>addon->MaterializeText};
            var group=field switch {13=>addon->DescriptionGroup,35=>addon->CraftingAndRepairsGroup,_=>(AtkResNode*)null};
            if(node==null||node->AtkResNode.Type!=NodeType.Text||field!=2&&(group==null||node->AtkResNode.ParentNode!=group))
                throw new NotSupportedException("追記欄の表示ノード構造が未対応です: "+field);
            var current=node->NodeText.AsSpan();
            if(current.Length>32768)throw new NotSupportedException("表示本文長の上限");
            var index=fields.ToList().IndexOf(field);
            var currentBytes=current.ToArray();
            if(generated!=null&&!DailyRoutineText.PresentationMatches(baseline[index]!,generated[index]!,currentBytes))
                throw new DeferredTooltipPresentationException("現在の表示本文が今回の生成内容と一致しません: "+field);
            edits.Add(new(node,group,baseline[index]!,currentBytes,field));
        }
        if(edits.Count==0)return null;
        return restored=>
        {
            try
            {
                Exception? failure=null;
                foreach(var edit in edits)
                    try{edit.Apply(restored);}catch(Exception ex){if(restored)throw;failure??=ex;}
                addon->UpdateGroupPositions(numbers,strings);
                foreach(var edit in edits){edit.Verify(restored);if(restored)observe?.Invoke(edit.Geometry);}
                if(failure!=null)throw new InvalidOperationException("表示の復元失敗",failure);
            }
            finally{foreach(var edit in edits)edit.ReleaseInput();}
        };
    }
    private sealed class Edit
    {
        private readonly AtkTextNode* node;
        private readonly AtkResNode* group;
        private readonly byte[] baseline,original,forward,rollback;
        private readonly ushort nodeHeight,groupHeight;
        private readonly ushort drawHeight;
        public Geometry Geometry=>new(this.field,nodeHeight,groupHeight,drawHeight,node->AtkResNode.Height,group==null?(ushort)0:group->Height,DailyRoutineText.TrailingLines(baseline));
        private readonly int field;
        private readonly TextFlags flags;
        private PinnedInput? input;
        private readonly bool description,visible;
        private static byte[] Terminate(byte[] bytes){var result=new byte[bytes.Length+1];bytes.CopyTo(result,0);return result;}
        public Edit(AtkTextNode* node,AtkResNode* group,byte[] baseline,byte[] original,int field)
        {
            this.node=node;this.group=group;this.baseline=baseline;this.original=original;this.field=field;description=field==13;
            forward=Terminate(baseline);rollback=Terminate(original);
            nodeHeight=node->AtkResNode.Height;groupHeight=group==null?(ushort)0:group->Height;visible=group!=null&&group->IsVisible();
            ushort width=0,height=0;node->GetTextDrawSize(&width,&height);drawHeight=height;
            flags=node->TextFlags;
        }
        public void Apply(bool restored)
        {
            // ApplyTextFlow reads OriginalTextPointer and writes NodeText. Keep
            // its unflowed input separate from the destination until positioning
            // and verification finish; never retain a movable managed pointer.
            input=new(restored?forward:rollback);
            node->TextFlags=restored&&group!=null?FlowFlags(flags):flags;
            node->SetText((byte*)input.Address);
            // SetText can already perform native line wrapping. Verify the same
            // payload/glyph invariant here as after ApplyTextFlow, not raw bytes.
            VerifyContent(restored?baseline:original,"書戻し直後");
            if(group==null)return; // Category keeps its existing row geometry.
            if(restored)
            {
                node->ApplyTextFlow();
                VerifyContent(baseline,"整形直後");
                ushort width=0,height=0;node->GetTextDrawSize(&width,&height);
                VerifyContent(baseline,"計測直後");
                // Source/native reflow may resize the group without updating
                // the node's Height. Subtract the rendered source text, not
                // that stale node height, while preserving the group's padding.
                var next=ResizeGroup(groupHeight,drawHeight,height);
                node->AtkResNode.SetHeight(height);group->SetHeight(next);
            }
            else {node->AtkResNode.SetHeight(nodeHeight);group->SetHeight(groupHeight);}
            group->ToggleVisibility(restored&&description?baseline.Length>0:visible);
        }
        public void Verify(bool restored)
        {
            VerifyContent(restored?baseline:original,"配置直後");
            if(group!=null&&group->IsVisible()!=(restored&&description?baseline.Length>0:visible))
                throw new InvalidOperationException("表示欄の可視状態不一致: "+field);
        }
        private void VerifyContent(byte[] expected,string stage)
        {
            var text=node->NodeText.AsSpan();
            if(text.Length>32768||!DailyRoutineText.SameFlowContent(expected,text.ToArray()))
                throw new InvalidOperationException($"表示本文の内容不一致: {field} / {stage} / flags={(ushort)node->TextFlags:X} / bytes={expected.Length},{text.Length}");
        }
        public void ReleaseInput()
        {
            var pending=input;if(pending==null)return;input=null;
            // The node owns this copy after SetText; future host flow must not
            // retain the temporary pinned input once this transaction returns.
            try
            {
                node->TextFlags=flags;
                if(node->OriginalTextPointer.Value==(byte*)pending.Address)node->OriginalTextPointer=node->NodeText.StringPtr;
            }
            finally{pending.Dispose();}
        }
    }
}
