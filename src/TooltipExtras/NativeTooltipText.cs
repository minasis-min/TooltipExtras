using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;

namespace TooltipExtras;

internal static unsafe class NativeTooltipText
{
    public static byte[] Terminate(ReadOnlySpan<byte> text)
    {
        var terminated = new byte[text.Length + 1];
        text.CopyTo(terminated);
        return terminated;
    }

    public static void Set(AtkTextNode* node, byte* input) => node->SetText(new CStringPointer(input));

    // A caller may retain the pin through native text flow and layout. Before
    // freeing it, release only the pointer written from that caller's buffer.
    public static void ReleaseInput(AtkTextNode* node, byte* input)
    {
        if (node->OriginalTextPointer.Value == input)
            node->OriginalTextPointer = node->NodeText.StringPtr;
    }
}
