# Build dependencies

Use Windows x64, PowerShell 7 and .NET SDK 10.0.302 (latest patch in that feature band is allowed). The API target is Dalamud 15. The supported runtime host is Dalamud 15.0.3.6; API number alone does not establish compatibility.

Host references are Dalamud, Dalamud.Bindings.ImGui, FFXIVClientStructs, InteropGenerator.Runtime, Lumina, Lumina.Excel and Newtonsoft.Json. Obtain the matching files from a local Dalamud SDK installation. They are not redistributed. Exact reference file versions and SHA256 hashes are recorded in `reference-lock.json`.

Runtime module IDs:

- Dalamud: `9a6f3e4f-c2f0-46f4-b211-7013dde20a8b`
- FFXIVClientStructs: `7fda2a82-4ca8-49d0-9687-4c8eac011509`
- Price Insight 2.11.5.0: `6ba90fe2-e666-4a94-b47d-abf7a855391e`
- Item Vendor Location 2.14.0.1: `ac0d5828-354a-4313-93fe-76680dcbb3fb`
- Simple Tweaks 1.15.0.7 (only the specific verified price layout exception): `f4d37b94-0e44-44cd-83fa-751e1fded533`

Glamour Log uses `GlamourLog.IsItemInArmoire`, `GlamourLog.IsItemInDresser` and `GlamourLog.IsSetComplete` with uint inputs and bool results. The adapter checks registration and invocation results instead of a fixed Glamour Log version.

MinasisUI 0.1.9 is embedded from the three pinned source files in `vendor/MinasisUI`; hashes are checked before building. No independent UI DLL is required. Historical WhichPatch data is pinned to upstream commit `5578bad66709f92811fe43b5c533144593a45d70`, rather than retrieved from the network at runtime. A future data update requires a reviewed plugin update.

Paths below are relative to the repository root; run the commands from that directory.

```powershell
pwsh -NoProfile -File ./scripts/Prepare-References.ps1 -DalamudDirectory '<your matching Dalamud SDK directory>'
pwsh -NoProfile -File ./scripts/Build.ps1
```

The first command copies only the seven reference DLLs into the ignored `.dependencies/Dalamud-15.0.3.6/` folder. The second verifies pinned sources/references, builds, runs checks and creates `artifacts/distribution/1.0.0.0/TooltipExtras.zip` and `icon.png`. Caches, temporary files and build output remain under the checkout's ignored `.work/` and `artifacts/` folders. The checkout itself can be on any writable drive.

The ZIP contains runtime files and required license notices only. It is not tracked by Git. No logs, user settings, host DLLs, cooperative fork, old icons, debug PDBs or research material are included. `PathMap` removes the local build root from generated debug metadata. SourceLink generation is disabled to avoid absolute development paths in its debug document map.

Licenses and attribution: [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
