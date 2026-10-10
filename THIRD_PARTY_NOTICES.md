# Third-party notices

Tooltip Extras original code and the user-created icon are licensed under BSD-3-Clause, Copyright (c) 2026, Minasis. This does not replace the licenses listed below.

- **Which Patch Was That?**, Copyright (c) 2022 Kouzukii, MIT. The action-to-item mappings and historical patch ranges derive from https://github.com/kouzukii/ffxiv-itempatch at commit `5578bad66709f92811fe43b5c533144593a45d70`. The ranges were converted to a binary-search representation. See `docs/licenses/WhichPatchWasThat-MIT.txt` in source, or `licenses/WhichPatchWasThat-MIT.txt` in the release ZIP.
- **Price Insight**, Copyright (c) 2021-2022 Kouzukii, MIT. The version-specific inverse geometry adapter was informed by UpdateItemTooltip/RestoreToNormal in https://github.com/kouzukii/ffxiv-priceinsight at commit `481392ff74aff10bec87d7ff4c4f2f670f5974a0`. The original MIT notice is retained in `docs/licenses/PriceInsight-MIT.txt` (release: `licenses/PriceInsight-MIT.txt`).
- **MinasisUI 0.1.9**, Copyright (c) 2026 Minasis; Copyright (c) 2026 min. Three source files are embedded, with their existing MIT notice retained in `vendor/MinasisUI/LICENSE` (release: `licenses/MinasisUI-MIT.txt`). File hashes are recorded in `vendor/MinasisUI/origin.json`. No separate MinasisUI DLL is required.

## Host-provided dependencies

These dependencies are provided by Dalamud and are not bundled in the source repository or release ZIP. Their own licenses remain applicable:

- Dalamud: AGPL-3.0 — https://github.com/goatcorp/Dalamud/blob/master/LICENSE
- FFXIVClientStructs: MIT, aers — https://github.com/aers/FFXIVClientStructs/blob/main/LICENSE
- Lumina: WTFPL — https://github.com/NotAdam/Lumina/blob/master/LICENSE
- Newtonsoft.Json: MIT, James Newton-King — https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md
- Dear ImGui: MIT, Omar Cornut — https://github.com/ocornut/imgui/blob/master/LICENSE.txt

Dalamud also supplies Dalamud.Bindings.ImGui, Lumina.Excel, InteropGenerator.Runtime, and icon fonts. No host DLLs, game resources or other plugins' inventory collection implementations are redistributed. Glamour Log is accessed through its public IPC; Item Vendor Location uses a pinned managed callback boundary. These integrations do not make those plugins part of this project's BSD license.
