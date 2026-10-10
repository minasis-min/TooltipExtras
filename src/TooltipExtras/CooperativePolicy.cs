namespace TooltipExtras;

internal static class CooperativePolicy
{
    public const string PatchSource = "WhichPatchWasThat.Patch";
    public static bool Accept(string source, bool patchEnabled) => source != PatchSource || patchEnabled;
    public static bool Display(string source, bool patchEnabled, bool builtInPatch) =>
        source != PatchSource || (patchEnabled && !builtInPatch);
}
