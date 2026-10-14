# Verification scope

## Automated checks

Release build and regression checks cover placement, price geometry, native node topology, cache invalidation, cooperative IPC lifetime and patch enable/disable policy, independent cleanup after exceptions, autosave ordering, and settings migration.

Settings migration checks operate only in the build tree's ignored `.work/` folder. A C-drive path is constructed to check portability, but no C-drive files are read or written by that check.

## User-reported Dev verification

Price Insight and Glamour Log display were confirmed by the user in the development versions. On 2026-10-10 the user also confirmed target consistency during rapid hover changes. The exact inventory of tested targets and external item-link providers was not supplied; this is not a claim of all-provider coverage.

## Not yet verified in game

The public 0.1.20.0 copy changes settings/diagnostic storage and legacy-settings migration. Its in-game storage/migration behavior is not yet confirmed. This does not invalidate the user's previously reported Dev verification of display behavior.

Custom-repository installation is to be checked by the user after publication. Item Vendor Location separation, first-frame/asynchronous-update recording exclusion with MaskedDalamud, forced cleanup failures/reload, and actual FPS impact are not confirmed. Timing counters in diagnostic JSON can help measure Glamour IPC/index work and diagnostic string-array scanning; they do not measure capture exclusion or total game FPS.

## Current SDK update (2026-10-13)

Version 1.0.1.0 targets Dalamud 15.0.3.6-117-gb17c64aca-estell.48. Nine native GUI types were compared with the previous references: structure sizes and field offsets match. The host identities and reference hashes are updated, with unknown-host rejection retained. The installed Price Insight, vendor and Simple Tweaks DLL identities are unchanged. MinasisUI 0.1.14 is embedded with owned heading-font handles and independent cleanup.

Release build and 83 existing checks pass. In-game load, native tooltip separation, both settings UI modes, font rendering and reload for this exact version remain unverified. Earlier user-reported tests do not establish verification of this update.

## Feature contracts (1.0.2.0)

Contracts pass for both the original SDK references and the current estell.48 SDK despite different module identities. Tests include unrelated recompilation, unrelated field addition, used field position/type changes, missing types, changed method signatures, changed called helper bodies, independent group rejection and detailed diagnostics. Builds/checks run against the current SDK references. Per-target node checks and existing restoration tests are retained. On 2026-10-14 the user reported in-game verification completed for 1.0.2.0. Individual tested scenarios were not specified, so this is not a claim that every UI mode, reload path or recording frame was verified. Installation/update from the published custom repository remains to be confirmed.

## Host type resolution fix (1.0.3.0)

The user's saved 1.0.2.0 diagnostic reports failed host type lookup while provider contracts pass, stopping the entire panel. 1.0.3.0 resolves host types from the exact assemblies used by TE instead of requiring a unique globally loaded assembly name. On 2026-10-14 the user confirmed this fix works in game and requested distribution. Individual scenarios were not specified. This does not establish verification of all UI/reload/recording paths.
