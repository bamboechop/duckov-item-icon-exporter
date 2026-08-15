# Duckov Item Icon Exporter

Duckov Item Icon Exporter is a standalone, one-time developer utility mod. When enabled, it exports the base game's inventory sprites as transparent PNGs for local Photoshop and UI-design reference. It does not write saves, alter inventory or progression, change game assets, or change game configuration.

The source tree and package intentionally contain no Duckov, Unity, or other game DLLs. Do not redistribute extracted icons: they remain game assets and are for local design reference only.

## Native contract verified

The supplied build gate probes the installed assemblies, rather than assuming a public mod API. The currently verified contract is:

- Duckov scans `Duckov_Data/Mods/<name>/info.ini`, loads `<name>.dll`, and requires `<name>.ModBehaviour` to derive from `Duckov.Modding.ModBehaviour`.
- `ItemAssetsCollection.Instance.entries` is the enumerable base-game collection; `Entry.typeID` is the stable identity and `ItemAssetsCollection.GetMetaData(int)` exists.
- `ItemMetaData` supplies the name, localization key, quality, tags, caliber, and an icon. `Item.Icon` is the inventory sprite; the native `ItemDisplay.Setup` assigns it to the UI image and uses Duckov's fallback sprite only when it is null.

Dynamic mod items are deliberately not enumerated: the installed API has a private dynamic dictionary and only exposes filtered search, so this exporter makes no false completeness claim for dynamic TypeIDs.

## Build and automated validation

PowerShell examples:

```powershell
./scripts/build.ps1 -DuckovPath 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
```

`DUCKOV_PATH` is accepted instead of the parameter. The gate restores and builds with warnings as errors, runs the game-independent tests, validates the installed native contracts, verifies the package inventory, confirms no game/Unity DLL was copied, and verifies that generated package output is ignored by Git.

The package output is repository-relative:

```text
artifacts/package/DuckovItemIconExporter/
  DuckovItemIconExporter.dll
  DuckovItemIconExporter.Core.dll
  info.ini
```

## Deployment

First run the inventory-only command:

```powershell
./scripts/deploy.ps1 -DuckovPath 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
```

It shows the exact three deployment files and checks only this target:

```text
E:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Mods\DuckovItemIconExporter
```

It does not write anything without `-Deploy`. If that exact directory exists, the script refuses to overwrite or back it up; inspect it and obtain explicit approval before any replacement. After approval, use `-Deploy -ReplaceExisting`; it verifies that the existing directory contains only the three exporter files and overwrites those files in place without a backup or deletion. Removal is simply deleting that exact exporter directory after the game is closed; no save or game asset repair is needed.

## Use and output

The user launches Duckov and enables the mod through Duckov's normal mod UI if needed. It logs with `[DuckovItemIconExporter]`, waits for the native collection, runs one export only, and logs the absolute directory and discovered/successful/unavailable/failed totals. It neither repeats across scene/raid/save transitions nor has a re-export hotkey.

Exports are never written to this repository. Each set is isolated under:

```text
Application.persistentDataPath\DuckovItemIconExporter\exports\<UTC timestamp>\
  icons\
  items.json
  items.csv
  index.html
  summary.txt
```

Rows are deterministic TypeID order and every distinct base-game TypeID has exactly one row. The record includes the stable TypeID, internal/localization/display names, quality, category/tags, caliber, sprite/texture names, native dimensions, filename, status, and reason. `NativeFallbackExported`, `NoIconAvailable`, and `Failed` remain explicit; a single bad icon does not stop the others.

To verify an actual export after Duckov exits:

```powershell
./scripts/verify-export.ps1 -ExportDirectory 'C:\path\reported\by\the\mod'
```

This checks manifest agreement, safe/unique TypeIDs and filenames, PNG signatures and positive dimensions, generated-file counts, status reasons, and gallery presence.

## Troubleshooting and visual check

- If the log reports that `ItemAssetsCollection` was unavailable, confirm the mod was enabled and wait until Duckov reaches a state where its base resources load. No empty output is intentionally produced.
- If activation fails, rebuild against the installed game version and rerun the native contract gate; an API mismatch should fail before deployment.
- If an item has `NativeFallbackExported`, its real `Item.Icon` was null and the PNG is Duckov's own fallback, not fabricated artwork.
- If an item is `NoIconAvailable` or `Failed`, use the manifest reason; no placeholder PNG is created.

After an approved deployment, compare representative output against the inventory: a weapon, ammunition, consumable, armor/equipment, totem, and visibly asymmetric item. Confirm that transparency, crop, orientation, and edges are correct. The isolated Unity camera renderer preserves the actual Sprite path, including atlased/packed and non-readable source textures, without making game textures writable.
