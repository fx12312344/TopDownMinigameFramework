# Third-party notices and excluded material

## Scope of the project's CC0 dedication

The project's CC0-1.0 dedication applies only to original contributions whose contributors can dedicate those rights: framework source, authored test doubles and tools, documentation, example definitions, and the original geometric test PNGs. It does **not** relicense any game, engine, library or other third-party work. This notice is a scope/exclusions document, not a replacement license grant.

The seven `dock_*.png` files under [Examples/RaidDocks/Textures/Items](Examples/RaidDocks/Textures/Items) are original 64×64 geometric test sprites, reproducible by [generate-textures.ps1](Examples/RaidDocks/generate-textures.ps1). They are not extracted game artwork, downloaded art or generated-by-AI artwork. Normal builds copy them unchanged. Any replacement artwork added by a downstream author needs its own appropriate rights and notices.

## Probably Stolen / 深空当铺 and Unity

The original game and Unity assemblies, textures, fonts, audio, other assets, trademarks, and any decompiled game source/research dumps are **not included** in this repository and are **not CC0**. No rights to those materials are granted by this project. References such as `Assembly-CSharp.dll`, `UnityEngine.*`, `Unity.TextMeshPro` and game API names describe interoperability with an existing local installation only. The project is unofficial and is not endorsed by the game's developers or Unity.

There are small independently written CSV compatibility routines in the build/test tooling to implement the game's localization value format for interoperability. These are not a distribution of game decompilation files, and their inclusion grants no rights to the game or its code/assets. Authored Unity/game test doubles are narrow testing substitutes, not the original runtime and not evidence of full game compatibility.

Game-dependent builds require the user to provide their own existing local references. Build helpers neither fetch nor bundle those DLLs. The scanner test loads and executes the locally supplied original scanner/parser; its dependencies are private test inputs, not redistributable project assets.

## Newtonsoft.Json / Json.NET

The production loader uses **Newtonsoft.Json**, by James Newton-King and contributors. Json.NET is a separate third-party library distributed under its own MIT license; **it is not dedicated to CC0 or otherwise relicensed here**. This repository does not contain a copy of its binary or source. Builds resolve the DLL from the configured local game's Managed directory; some offline test projects copy it into their ignored build output so tests can run. That local copy is not a release artifact of this repository.

If you separately redistribute any Newtonsoft.Json binary/source, check the exact version's original license/copyright notices and satisfy those terms. Nothing in this repository removes those obligations or grants rights to redistribute the surrounding game.

## Harmony / Cecil and other scanner dependencies

[ScannerTests](ScannerTests/ScannerTests.csproj) references locally installed `0Harmony.dll` and `DseCecil.dll`, along with game and Unity assemblies. They are not included or relicensed. Harmony and Cecil-derived distributions have their own upstream copyright and license terms; identify the precise locally supplied build and retain its applicable notices before any redistribution. A game-bundled dependency's presence does not mean the game's bundle or modified versions can be redistributed under this project's dedication.

The .NET SDK/runtime and platform tools remain under their own terms. No third-party dependency should be added to a CC0 source/release package merely because a build/test copied it into `bin`.

## Distribution checklist

- Publish the intended original source/documents and geometric example PNGs with the project's license material.
- Do not publish game/Unity/scanner/Newtonsoft assemblies, extracted assets, decompiled files, private research, local installation paths, or build/test output directories.
- For playable mod packages, copy only the dedicated framework/example `dist` folders produced by the build scripts; verify they contain no bundled game/engine/library DLLs.
- Any added third-party code or artwork needs its own provenance, compatible terms and preserved notices. Do not label it CC0 unless its actual rightsholder has done so.

## 中文说明

CC0 仅适用于贡献者有权贡献的原创框架代码、文档、测试工具/替身、示例定义及七张原创几何 PNG。原游戏所有 DLL、图像、字体等资源以及反编译资料不随仓库发布，也非 CC0；本项目不授予这些材料的权利。少量为互操作独立编写的 CSV 兼容例程只实现输入格式，不等于发布游戏反编译资料或获得游戏授权。

Newtonsoft.Json/Json.NET 是独立第三方库，仍受其自身 MIT 许可及版权声明约束，不被本项目重新授权。Unity、游戏、Harmony/Cecil 相关程序集也保留各自权利。构建与扫描测试从已有本地安装读取引用，`bin` 中用于测试的副本不是公开发布内容。若替换示例 PNG 或新增第三方材料，应自行核实来源并保留相应许可与声明。
