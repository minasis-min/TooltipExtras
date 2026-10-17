# Build dependencies

Use Windows x64, PowerShell 7 and .NET SDK 10.0.302 (latest patch in that feature band is allowed). The API target is Dalamud 15. The build references are Dalamud 15.0.3.6-117-gb17c64aca-estell.48. Runtime support is determined by feature-specific contracts, not DLL version or module ID. API number alone does not establish compatibility.

Host references are Dalamud, Dalamud.Bindings.ImGui, FFXIVClientStructs, InteropGenerator.Runtime, Lumina, Lumina.Excel and Newtonsoft.Json. The checks additionally reference host-provided Serilog for real SeString parsing and Microsoft.Extensions.ObjectPool for the host Lumina color builder. Neither is a plugin runtime dependency added by TE. Obtain the matching files from a local Dalamud SDK installation. They are not redistributed. Exact reference file versions and SHA256 hashes are recorded in `reference-lock.json`.

Reference module IDs (build provenance only; not runtime allowlists):

- Dalamud: `0982f2f8-b2dd-42d5-ad75-acd63588fc42`
- FFXIVClientStructs: `f2ce2d31-ef6f-45f8-a331-8b2785f00c81`
- Price Insight 2.11.5.0: `6ba90fe2-e666-4a94-b47d-abf7a855391e`
- Item Vendor Location 2.14.0.1: `ac0d5828-354a-4313-93fe-76680dcbb3fb`
- Simple Tweaks 1.15.0.7 (only the specific verified price layout exception): `f4d37b94-0e44-44cd-83fa-751e1fded533`

Glamour Log uses `GlamourLog.IsItemInArmoire`, `GlamourLog.IsItemInDresser` and `GlamourLog.IsSetComplete` with uint inputs and bool results. The adapter checks registration and invocation results instead of a fixed Glamour Log version.

MinasisUI 0.1.14 is embedded from the four pinned source files in `vendor/MinasisUI`; hashes are checked before building. No independent UI DLL is required. Historical WhichPatch data is pinned to upstream commit `5578bad66709f92811fe43b5c533144593a45d70`, rather than retrieved from the network at runtime. A future data update requires a reviewed plugin update.

Paths below are relative to the repository root; run the commands from that directory.

```powershell
pwsh -NoProfile -File ./scripts/Prepare-References.ps1 -DalamudDirectory '<your matching Dalamud SDK directory>'
pwsh -NoProfile -File ./scripts/Build.ps1
```

The first command copies the seven plugin reference DLLs and the test-only Serilog and Microsoft.Extensions.ObjectPool DLLs into the ignored `.dependencies/Dalamud-15.0.3.6-estell.48/` folder. The second verifies pinned sources/references, builds, runs checks and creates `artifacts/distribution/<Version>/TooltipExtras.zip` and `icon.png`. Caches, temporary files and build output remain under the checkout's ignored `.work/` and `artifacts/` folders. The checkout itself can be on any writable drive.

The ZIP contains runtime files and required license notices only. It is not tracked by Git. No logs, user settings, host DLLs, cooperative fork, old icons, debug PDBs or research material are included. `PathMap` removes the local build root from generated debug metadata. SourceLink generation is disabled to avoid absolute development paths in its debug document map.

Licenses and attribution: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).

The build also writes a complete Dev bundle to a fixed ignored artifacts/plugin/ directory. Use -DevOutputDirectory to select an existing Dev folder. The plugin DLL, manifest, deps.json, icon and required licenses are copied together; saved settings are not overwritten. The manifest uses ApplicableVersion=any; native access remains gated by the feature contracts.

## Runtime compatibility contracts

`src/TooltipExtras/CompatibilityContracts.json` is embedded in the DLL. It records only the used API signatures, enum values, native field positions/types and audited operations. IL tokens, including native call signatures, are resolved to names; assembly versions and module IDs are excluded. Same-assembly called methods are included for the audited operations. Per-target node ownership, geometry and restoration checks remain in effect.

`tools/CompatibilityProbe` verifies contracts against explicitly supplied SDK/provider directories. Use `dotnet run --project tools/CompatibilityProbe -- check <contract-file> <SDK-directory> <provider-directories...>` after sourcing `scripts/Environment.ps1`. Its `generate` mode is for reviewing a new baseline, never for silently accepting an incompatible update. Host contracts are checked once per load; provider contracts are checked when active plugins change. Status changes are written to Dalamud Debug logs. Native array diagnostics have a separate guard.

An API-level load rejection happens before plugin initialization and is reported by Dalamud. Even matching managed contracts cannot prove every game update compatible; an unexpected live node shape is rejected and the native display is retained.

## Tooltip callback relay

The Allagan Tools adapter reuses the loaded InventoryTools instance and its existing CriticalCommonLib TooltipService. It replaces only AmountOwnedTooltip, SourceInformationTooltip and UseInformationTooltip registrations with forwarding subclasses, using the original enabled getters and original callbacks. It neither collects inventory data nor formats sources/uses independently. Provider assemblies and source code are not bundled.

AllaganNative checks the used string-array layout and SetValue operations both against TE's host types and against the actual callback pointer types' assembly. AllaganProvider checks the dispatch-loop body, string-accessor bodies and callback signatures. Callback body hashes are deliberately not pinned: the original formatter runs as supplied, then byte-prefix validation accepts only a single appended field among Description, Effects and Levels. Structural changes or a changed dispatch loop stop this adapter. Successful earlier sections remain on the panel if a later section is rejected.

The relay uses Dalamud's internal exposed-plugin wrapper and the provider's hosted service container; these are not stable public IPC. Connection failures leave native callbacks unchanged. Only the proxy entries still owned by TE are restored on detach. A native write failure stops further relay writes and attempts to restore the provider result. This fallback does not guarantee recovery from arbitrary native memory corruption.

The Simple Tweaks extras adapter relays only AdditionalItemInfo.AfterItemDetailUpdate through the existing EventSubscriber.Method slot. The source tweak object, settings, event enable flags and formatter remain intact. The original callback creates its own text node; TE copies its SeString and reverses only the verified window height, component surface height and footer displacement. A changed shape leaves the native result intact. No node is allocated or destroyed by TE. The original before/after operation contracts and event dispatch body must match, and the actual callback native ABI is checked. ID/action and desynthesis changes are outside this adapter. Source configuration must remain enabled. Holding Ctrl retains the last successful extras result for the same visible target if the source produces no visible node; target changes, hide and disabling invalidate it. Game-driven changes to the base tooltip itself are not suppressed.

Item IDs are captured from the generated ItemDetail category node only while the source Show ID feature is enabled and its operation/signature contracts match. A final color-3/ID-text/color-reset suffix with the exact audited decimal/hex format is extracted; unmatched text remains native. No item ID is independently obtained or calculated. Only node text is changed and restored, not the native category row's dimensions; multiline category reflow and recording-frame behavior need live verification. Action IDs and desynthesis are not included. The same Simple Tweaks checkbox controls these item IDs and dedicated extras, with separate compatibility checks/status.

AT captured text has only its outer newlines trimmed for its audited single-text-payload format. Colors, link metadata, internal line breaks and indentation remain. Owned/locations, sources and uses are joined with one line break; spacing between plugin sections is unchanged.
## Additional generated tooltip relay

The text relay supports FoodStats, MateriaStats, DesynthesisSkill, CollectableRewards, LogosTooltip, TrackFadedRolls, TrackGachaItems and TrackOutfits. It obtains the source's existing GenerateItemTooltip hook address through its loaded manager and installs a Dalamud hook with the same audited native signature. During that synchronous generation call only, selected SubTweaks registrations are temporarily replaced with forwarding subclasses. A finally scope restores the original instances, including on failure; source settings and Enabled values are not changed. The source callbacks run at their original position and only once per invocation. Native-hook ordering and live behavior still require game verification.

The callbacks' before/after string fields are copied. Completed effects/stat rows are retained for rewrites; append-only features must match an exact byte prefix. Before values and the materia stat-line count are restored before normal layout continues. Write/readback failures attempt to restore all original provider results and stop the adapter. Exceptions are contained so source error UI does not receive a synthetic proxy. A module-specific dispatcher/native contract and separate contracts for each formatter allow one incompatible formatter to fall back without disabling the others. Exact source instances identify their assemblies, including while older load contexts remain present.

UnlockablePreviews and PaintingPreview use the original post-update callback and resolved lastImage field. The original image selection (including character-dependent selection) is not reimplemented. TE loads that resolved image path through ITextureProvider for ImGui presentation. Only ISharedImmediateTexture is retained; frame-borrowed wraps/handles are used only in the current UI draw. No source texture/node is disposed or recreated by TE. The validated source image is hidden immediately after generation, without waiting for the ImGui texture. Native geometry is reversed at PreDraw once source-owned nodes, positions and component heights match. Heights observed inside GenerateItemTooltip are diagnostic only because native layout may still belong to the previous item. Collapse inverts the current generated image geometry, never an incoming window snapshot; current visible native text bounds prevent preview/price shrinking below body content. The preview-only bottom margin (16px for unlockable previews, 8px for paintings) is removed along with the image, while the measured native body padding is preserved. Hidden ancestors, empty text, control hints and foreign append nodes do not contribute; unsupported transforms defer shrinking. Requested updates restore geometry without showing the same-target source image. While layout is pending, AddonArgs.PreventOriginal skips only that native draw, with a monotonic 100ms deadline per target that repeated updates cannot extend; texture loading never delays the native text. A separate API compatibility check gates this optional draw suppression. No multi-item image cache is added. The painting-specific root-height synchronization exception accepts only a root becoming exactly equal to the captured window height, with every other value unchanged. Source native images remain allocated and owned by their original plugin while hidden.


Live load, native-hook chain ordering, image loading/DPI, food changes, modifier keys, reload/disable, performance and recording frames are not established by the offline model checks.

GlamItemNames uses a separate forwarded callback in the same temporary SubTweaks scope. The existing replacement text node must match the source's ItemName + NewLinePayload + GlamourName exactly before TE copies it. TE hides that source-owned replacement and shows native text node 33; it does not rewrite names, resize the header, query item sheets, or allocate/destroy native nodes. Disabling the relay restores the source visibility only while node identity and copied content still match. Its managed operation and the provider text-node ABI are checked separately.

Name colors are sampled from the original native title node and the provider replacement node at PreDraw, after the game applies current text colors. RGBA bytes are transported using Lumina color payloads, with resets around each name; TE does not derive rarity or query a color sheet. The exact native color field/layout and required builder signatures are checked. Unrelated overload additions do not invalidate a required API signature. Requested updates preserve captured name data; only explicit restore/disable/unload returns the provider replacement to view.

## Coordinated restoration

Price restoration uses the original verified append delta against its exactly tracked live layout, with node identity, content and all window heights still checked. Preview restoration reports an accepted geometry change to the price adapter, including restoration during source detachment. Turning off price separation restores the price's current native position and append height without revealing the preview; both preview restore endpoints follow that exact change. Visible native price updates are peeled only when their append geometry matches, and preview endpoints track the latest visible price delta without cumulative addition. Deferred price restoration retains its snapshot for a later safe retry. Lifecycle hide/finalize/target invalidation remain explicit ownership boundaries.

Preview capture stores immutable source-before/Generated geometry separately from mutable expanded/compact restore endpoints. Painting Root synchronization always uses source-before, including after initial collapse or price restoration. Native body measurement rejects an otherwise eligible visible text whose parent path terminates before the root; explicit hidden/foreign/control exclusions are still skipped. Tests exercise the same shared layout records and restore plans used by the adapters; native lifecycle execution and rendering still require live verification.

## Same-target requested updates

While separation remains enabled, RequestedUpdate for the same exact item key/addon keeps the existing price and preview hidden. It does not expand their geometry merely to let the native/source generators regenerate them. The native layout and compatible source callbacks run normally; changed output is captured again and verified before presentation. Target changes clear old price ownership, while explicit OFF/hide/unload still use coordinated restoration. No HQ/NQ content is synthesized or delayed, and no multi-item cache is introduced.

A shared geometry pass attempts separation at PostRequestedUpdate and before native Update resolves screen coordinates, then revalidates at PreDraw for later producers and asynchronous changes. All native access in the pass requires PriceNative compatibility. Unknown/incomplete layouts defer to later checked passes. Source color sampling and the optional 100ms draw-only suppression remain at PreDraw.
