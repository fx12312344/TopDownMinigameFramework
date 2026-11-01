# Top-down Minigame Framework / 俯视射击小游戏框架

An experimental **0.2.4** mod framework for adding small top-down shooting sandboxes to **Probably Stolen / 深空当铺**. This is **not a standalone extraction shooter**, a finished game, or a production-stable SDK. It does not depend on a separate firearms mod.

本项目是为《深空当铺》扩展俯视射击小游戏的早期框架，不是完整独立搜打撤游戏，也不宣称正式稳定。公开内容包括原创框架源码、构建与测试工具、示例 JSON/CSV 和原创几何 PNG；原游戏 DLL、图像及反编译资料均不包含，也不属于本项目的 CC0 范围。Newtonsoft.Json 等第三方依赖不被重新授权。

## Scope and current status

- Main-menu entry, full-screen sandbox, keyboard/mouse input, pause/focus handling and session cleanup.
- Movement/collision, hitscan weapons, simple chasing enemies, loot boxes and optional extraction zones.
- API v1 content definitions: items, weapons, maps, enemy spawn attributes, palettes and PNG sprite keys. Built-in content uses the same registry as extensions.
- A separately compiled [Night Docks example](Examples/README.md): a 24×16 map, one additional carbine, two virtual loot items and seven original geometric test PNGs. The example DLL references the game, **not the framework DLL**.
- No real inventory integration, save settlement, persistent economy, multiplayer, general scripting, pathfinding or map editor. Item dimensions are metadata; the current inventory is a temporary list, not a grid inventory. The UI exposes the first two loadout weapons.

**0.2.4 verification boundary:** content is parsed explicitly with the locally supplied real Newtonsoft.Json assembly, not a Unity JSON substitute. The offline generated-CSV → loader → registry/model checks exercise **1 map / 1 weapon / 2 items / 1 skin**, including nested map arrays, loadout and palette. Empty packs and duplicate JSON keys are rejected. This demonstrates actual content parsing, not just a provider-registration log. It does **not** establish successful in-game CSV import, map selection, fonts, textures, input, rendering, mod ordering or enable/disable behavior. **Live-game verification of 0.2.4 remains pending.** The manifest currently targets game version `0.46D`; other versions may need adaptation.

## License — CC0-1.0

[![License: CC0-1.0](https://img.shields.io/badge/License-CC0--1.0-lightgrey.svg)](LICENSE)

To the extent possible under law, the contributors dedicate their original contributions in this repository to the public domain under **CC0 1.0 Universal**. You may copy, modify, redistribute and use those contributions commercially without attribution requirements. The work is provided without warranty. Read the full [CC0 legal text](LICENSE); third-party exclusions remain applicable.

## Repository and rights

Original project contributions (including the seven geometric PNGs) are dedicated under **CC0-1.0**; see [LICENSE](LICENSE). This does **not** dedicate or sublicense Probably Stolen, Unity, Newtonsoft.Json, Harmony/Cecil or any other third party's material. No game assemblies, extracted game artwork, decompiled game files or proprietary research dumps are distributed here. Small independently written CSV compatibility routines implement the format needed for interoperability; they grant no rights to the game. See [third-party notices and exclusions](THIRD_PARTY_NOTICES.md).

The legacy technical names remain `RaidPrototype` (assembly/install folder) and `local.raidprototype` (mod ID), so existing content prerequisites keep working. The public project name does not rename those identifiers.

## Build from this repository root

Prerequisites: Windows PowerShell/PowerShell, **.NET SDK 8**, and, only for game-dependent projects, access to the required assemblies from an **existing local game installation/reference directory**. This repository neither includes nor downloads the game. If those references are unavailable, the pure model tests below can still run without them.

Configure **one** of these environment variables in your current shell (replace the placeholder with your own existing location):

```powershell
$env:PROBABLY_STOLEN_MANAGED = 'C:\path\to\Probably Stolen\Probably Stolen_Data\Managed'
```

Or use `PROBABLY_STOLEN_ROOT` pointing at the existing game root; the helpers append `Probably Stolen_Data/Managed`. Build scripts accept explicit `-GameManaged` and `-GameRoot` parameters. MSBuild projects accept `-p:GameManaged=...` or `-p:GameRoot=...`; `GameManaged`/`GameRoot` environment properties are also supported. Resolution order is explicit/legacy `GameManaged`, `PROBABLY_STOLEN_MANAGED`, then root-derived Managed. Missing configuration or assemblies fails with an explanatory error, with no download or guessed personal path. A command-line MSBuild property only configures that build; for the loader test's runtime byte comparison, keep a Managed/root environment variable set as shown above.

```powershell
.\build.ps1
```

[Root build script](build.ps1) builds [the framework project](RaidPrototype.csproj), runs [pure model tests](Tests/Tests.csproj), and creates `dist/RaidPrototype` containing only the framework DLL and manifest. It does not build or install the example automatically.

```powershell
.\Examples\RaidDocks\build.ps1
```

[Example build script](Examples/RaidDocks/build.ps1) regenerates the localization CSV from [pack.json](Examples/RaidDocks/pack.json), builds the independent example DLL, and copies the existing PNGs unchanged into `Examples/RaidDocks/dist/RaidDocksExample`. The texture generator is opt-in; normal builds do not redraw artwork.

Optional installation: exit the game, then run [install.ps1](install.ps1) with `-GameRoot` or a configured root/Managed variable. It requires both output packages, backs up existing matching mod folders under the game root's `ModBackups`, copies and hash-checks output files. It changes the local game's Mods folders; it is not a build/test step and is not run automatically. Do not put source trees, test outputs, backups, `bin` or `obj` in Mods. Distribute only the two dedicated package folders, never copied game/Unity/Newtonsoft DLLs.

## Offline tests

These projects are console test harnesses: use **`dotnet run`**, not `dotnet test`. Commands are relative to this repository root.

| Test | What it establishes | Local game references |
|---|---|---|
| [Tests](Tests/Tests.csproj) | Pure model/registry behavior, limits and snapshots | None |
| [IntegrationTests](IntegrationTests/IntegrationTests.csproj) | Production entry/UI/bridge code under authored Unity/game test doubles, plus real Newtonsoft CSV content parsing | Newtonsoft.Json.dll |
| [LoaderValidation](Examples/Validation/LoaderValidation.csproj) | Real loader and model, generated CSV, schema/cost checks and byte-identical local Newtonsoft | Newtonsoft.Json.dll; environment variable also needed at runtime |
| [AssetValidation](Examples/Validation/AssetValidation.csproj) | CSV/manifest/PNG output and read-only PE metadata of the built example | Build example first; no game code execution |
| [ScannerTests](ScannerTests/ScannerTests.csproj) | Locally supplied unmodified game scanner against explicitly selected package folders | Game/scanner dependencies; version-specific |

```powershell
dotnet run --project .\Tests\Tests.csproj -c Release
```

```powershell
dotnet run --project .\IntegrationTests\IntegrationTests.csproj -c Release
```

```powershell
dotnet run --project .\Examples\Validation\LoaderValidation.csproj -c Release
```

Additional generator and built-output checks:

```powershell
.\Examples\Validation\test-content-localization.ps1
```

```powershell
dotnet run --project .\Examples\Validation\AssetValidation.csproj -c Release -- .\Examples\RaidDocks
```

**ScannerTests does not ship a Fixtures directory or old scanner fixture packages.** Select a package you built yourself, or any local mod package you are authorized to inspect; a package folder must contain its own manifest and declared assemblies. After the two builds above, for example:

```powershell
. .\Resolve-GamePaths.ps1
$managed = Resolve-LocalGameManaged
dotnet run --project .\ScannerTests\ScannerTests.csproj -c Release "-p:GameManaged=$managed" -- "$managed" .\dist\RaidPrototype .\Examples\RaidDocks\dist\RaidDocksExample
```

The scanner harness loads and calls locally supplied game scanner/parser code offline, checks the loaded game assembly hash, and avoids `ModLoader.Awake`. Unlike the metadata-only test, it **does execute game code**; inspect only trusted local references/packages. A pass does not prove the game has enabled either mod or that arbitrary mods are safe. Test doubles and scanner dependencies are never part of the mod distribution.

### Local verification snapshot

The clean public source copy was built and checked offline with .NET SDK 8.0.418 and existing local game references: framework and example builds had **0 warnings / 0 errors**; pure tests passed **126 checks**; integration harness passed **104/104 scenarios, 1,189 assertions**; loader validation passed **143 assertions**; localization generation passed **16 assertions**; output/metadata validation passed **33 assertions**. The original scanner accepted both generated packages with no unlisted DLLs, undeclared capabilities, hard flags or findings; its five CSV parser cases passed. Both documented environment fallbacks and the missing-configuration error were checked. These are results for the locally supplied version, not a guarantee for other installations or a substitute for pending in-game acceptance.

## Authoring and playing

Start with [AUTHORING.md](AUTHORING.md) (API/lifecycle, currently Chinese) and [Examples/README.md](Examples/README.md) (schema/build protocol). Copy the example sources, change manifest/pack/definition IDs, edit the map and supply your own PNGs under `Textures/Items`. `pack.id` must exactly equal the provider's manifest ID. Rebuild to convert JSON into the supported single-row `Localization/en.csv` payload. The framework reads the game's loaded localization/sprite assets; it does not scan arbitrary JSON directories or run content scripts. Independent providers must not reference/copy `RaidPrototype.dll` or bypass the scanner.

Enable the framework and optional example in the game, return to the main menu, and use the sandbox entry. In preparation, select a map/content pack. WASD moves; mouse aims; left click fires (hold only for automatic weapons); E opens nearby boxes; 1/2 switches the first two loadout weapons; B/Esc pauses/resumes. Losing focus pauses; exiting clears the in-memory session. Content changes affect new sessions, not existing deep-copied snapshots.

中文作者入口：[API 指南](AUTHORING.md)、[JSON/PNG 示例说明](Examples/README.md)、[示例入口](Examples/RaidDocks/Entry.cs)。修改 JSON 后必须重建 CSV；运行层不读任意磁盘 JSON，不自动接入真实库存与存档。

### Isolation is a design boundary, not a security guarantee

Production gameplay source does not call real inventory/save APIs, network or file IO, and does not change global `Time.timeScale`. Other game/mod code still runs in the same process and can have its own side effects. Automated tests and source-level isolation **are not proof that live game saves can never change**. Back up your own data before experimental mod use; no in-process mod framework is a permissions sandbox.
