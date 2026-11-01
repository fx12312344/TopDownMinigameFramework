# JSON 内容包与独立第三方模组（1.0.1）

The Night Docks example is a separate game-only DLL plus JSON-derived localization CSV and seven original geometric PNGs. Run all commands below from the **repository root**, after configuring `PROBABLY_STOLEN_MANAGED` or `PROBABLY_STOLEN_ROOT` for your existing local installation. See the English [build/test overview](../README.md) and [third-party exclusions](../THIRD_PARTY_NOTICES.md). Offline 0.2.4 parsing is verified; live-game acceptance is still pending.

本目录不参与框架默认编译。`RaidDocks` 是独立 IMod、独立 DLL、独立 manifest；**DLL 只引用真实游戏 `Assembly-CSharp` 和 `netstandard`，不引用 RaidPrototype**。内容通过游戏原生支持的 `Localization/en.csv` 交给框架 ModLoader 桥，避免原版跨模组 DLL 扫描限制。它不修改、替换或给框架打补丁，不使用运行时代码生成。

## 构建与安装

1. 游戏侧安装支持 Localization 内容桥的框架版本。构建示例本身**不需要框架 DLL**，也不再接受 `-FrameworkDll` 参数。
2. 从仓库根目录运行下列命令；脚本接受 `-GameManaged` / `-GameRoot`，或使用 `PROBABLY_STOLEN_MANAGED` / `PROBABLY_STOLEN_ROOT`。没有个人机器默认路径；无配置时给出明确错误，不下载游戏。

```powershell
.\Examples\RaidDocks\build.ps1
```

3. 将 `Examples/RaidDocks/dist/RaidDocksExample` 文件夹与框架 `dist/RaidPrototype` 文件夹并排放入游戏 Mods 目录，退出并重启游戏，再启用两个模组。manifest 版本为 1.0.1，保留 `<Prerequisites><Mod>local.raidprototype</Mod></Prerequisites>`，确保框架先启用。
4. 等框架发现内容后，在准备界面选择“夜港仓库 24×16”，使用“港务卡宾枪”。它在内置两把枪之外新增第三把枪，并非修改默认枪。地图 loadout 只含新枪，不依赖内置内容 ID。

完整分发目录应包含：

```text
RaidDocksExample/
  RaidDocksExample.dll
  manifest.xml
  Localization/
    en.csv
  Textures/
    Items/
      dock_actor.png
      dock_carbine.png
      dock_circuit.png
      dock_crate.png
      dock_floor.png
      dock_medical.png
      dock_wall.png
```

**不要携带框架、游戏、Unity 或 Newtonsoft DLL**。`pack.json`、C# 源码和生成脚本也不是运行时依赖。发布时使用当前源码构建的游戏-only IMod DLL。

### 构建期 Localization 协议

`RaidDocks/pack.json` 是 UTF-8 **构建期输入**。`build.ps1` 调用 `generate-content-localization.ps1`，解析 JSON，校验 `apiVersion=1`、`pack.id` 与 `manifest.ID` **逐字符、区分大小写一致**，再 minify 成一行 JSON，写入 UTF-8 无 BOM 的 `Localization/en.csv`。只有一行数据，**不加标题行**，格式为：

```text
raid_framework_pack_v1,"{""apiVersion"":1,...}"
```

这里 `...` 仅表示文档省略，实际 CSV 必须包含完整合法 JSON。key 固定为 `raid_framework_pack_v1`；value 使用外层双引号，每个内部双引号翻倍。不能把逗号 JSON 当作未转义 CSV，也不要改语言为 zh：桥读取的入口是 `mod.Localization["en"]["raid_framework_pack_v1"]`。

原版按**首个逗号**切开 key/value，`ParseCsvValue` 先 `Trim()`，再剥除一对外部双引号、将 `""` 还原成 `"`，最后**无条件把两个字符反斜杠+n（`\n`）替换为实际 LF**。因此原始JSON文本中若含字面反斜杠+n序列（包括 `\\n` 或 `\n`），也可能被破坏；纯 `\u000A` 这样的Unicode转义本身不会被该CSV替换规则改写，但生成器会将它解码为实际换行并保守拒绝。生成脚本递归拒绝 JSON 中**任何字符串值或属性名**含实际 CR/LF，或含字面反斜杠+n；包括未知字段、数组深层字符串，不能仅校验 name。示例所有 name 都没有换行。当前协议不支持多行名称/描述/任意含该字面序列的字符串；不要以增加 CSV 引号尝试绕过。普通引号、逗号、首尾空格、中文及不含该序列的反斜杠均可保存。

生成后脚本按原版完整规则还原 value，再解析并比较 JSON；验证失败不写输出。修改 `pack.json` 后重新运行 `build.ps1` 并重新安装产物；只往分发目录放一份 JSON 无效。只生成资产可单独调用 `generate-content-localization.ps1`，但安装必须仍使用完整分发目录。

现有 7 张 64×64 PNG 是几何测试图形，不是下载或生成式美术；`build.ps1` 只逐字节复制，不重新生成。可替换 `Textures/Items` 下同名 PNG。`generate-textures.ps1` 仅保留为主动重画原始测试纹理的工具，不在构建链中调用。PNG 文件名使用英文。

## 加载与注册生命周期（禁止直接跨 DLL 调用）

示例 `Entry` 的 `Init`、`OnEnable`、`OnDisable` 都为空，只实现游戏的 `IMod`。**不要在第三方 DLL 中引用 `RaidPrototype.Content`，也不要从其 OnEnable 直接调用 `ContentLoader.RegisterJson`、从 OnDisable 直接调用 `ContentRegistry.Unregister`**：即使标记 `Private=false`，程序集扫描仍可能拒绝跨模组引用；复制框架 DLL 更不是解决方案。

框架内部 ModLoader 桥在资产加载与后续刷新中读取上述 Localization 载荷，按模组启停自动注册/注销；只有框架内部使用其 Loader/Registry API。桥采用 provider 受约束入口，原子检查内容包 ID 等于提供者 `manifest.ID`，再执行 Loader 的 schema/数值/依赖/引用验证。空白、非对象 JSON、解析失败、版本不兼容、重复 ID、缺失依赖、无效数值、未知内容引用都会拒绝，不留下半个包；失败不是已注册。示例不自行保存注册状态，启停、重复发现和框架重启由桥负责。

`requires` 是**内容包 ID**依赖，manifest `Prerequisites` 是**游戏模组 ID**依赖，两者不可混用。示例内容自含，`requires: []`；manifest 仅依赖 `local.raidprototype`。若其他包引用你的定义，先停用依赖包，再停用你的包；注册表会拒绝卸载仍被依赖的包。已有对局使用深拷贝会话快照，不受后续包卸载影响。

## Schema v1

JSON 属性名**大小写敏感**，统一 camelCase，根必须是对象。C# API 字段为 PascalCase，不可直接把其名称抄为 JSON。

- **必须显式写 `"apiVersion": 1`**。C# `ContentPack.ApiVersion` 虽然默认 1，JSON 不采用该默认值；缺失、0、2 等全部拒绝，避免未来版本含义漂移。
- **0.2.4改用游戏自带Newtonsoft显式解析**，不再经Unity JsonUtility自动映射。未知字段仍忽略；已知字段按精确大小写和标量类型读取。重复键（含嵌套/未知字段）、空内容包、版本类型不对会拒绝。不要使用$type或期待运行任意类型绑定，Loader从不执行自动类型反序列化。
- 根、定义和引用 ID 区分大小写。每个定义 ID 必须以 `packId + "."` 开头，如 `example.raiddocks.carbine`。每个 pack/定义均须提供非空 `nameZh`、`nameEn`。
- 缺失根数组/地图数组按空数组处理，不接受数组中的 `null`。数值/bool 字段不采用 C# DTO 构造器默认值：**除下面声明的颜色默认值外，应显式填写**，没有填写的数值按 0，bool 按 false；注册表会拒绝不合法 0。`hasExtraction=false` 时可省略 extractionPoint，但 extractionRadius/Seconds 仍须合法正数。
- 坐标、伤害、颜色必须为有限数；所有空间坐标是本地图世界单位，不是像素。障碍、出生点、箱子和撤离圆必须满足注册表边界/不重叠约束。

### 加载成本边界

加载器先于 Newtonsoft token树解析检查输入：`MaxJsonCharacters = 1048576`（1 Mi 个 C# UTF-16 字符，不是 UTF-8 字节；包含首尾空白），`MaxJsonDepth = 32`（根对象计一层，对象与数组都计数）。扫描正确忽略字符串中的括号与转义引号/反斜杠；超过限制立即抛异常，不进入JSON解析器。该扫描是成本边界，不是完整 JSON 语法验证器。

Newtonsoft完成token树解析及显式字段读取后、分配定义数组前，加载器遍历 DTO 树检查每个数组大小：requires≤128，items/weapons/skins各≤4096，maps≤128，全部定义合计≤4096；每地图 enemies≤64、boxes≤128、obstacles≤256、loadout≤64，每箱 loot≤64。实际限额直接引用注册表常量。所有已识别数组元素（含根数组、依赖、loadout、loot）累计≤`MaxNestedNodes = 65536`；计数是每次调用独立局部状态，不用共享静态计数。未知字段仍忽略，但仍受输入长度/深度限制。token树和DTO数组自身由解析步骤分配，因此不宣称这一层能消除所有解析分配；1Mi 字符上限限制了先行解析规模。

### 根对象

| 字段 | 类型 / 含义 |
|---|---|
| apiVersion | 整数，显式 1 |
| id, nameZh, nameEn | 字符串；唯一包 ID、中英名称 |
| requires | 字符串数组，已注册的其他内容包 ID，无重复/自依赖 |
| items | Item 对象数组 |
| weapons | Weapon 对象数组 |
| maps | Map 对象数组 |
| skins | Skin 对象数组 |

### Item / Weapon

| 对象 | 字段 |
|---|---|
| Item | id/nameZh/nameEn；width/height/maxStack 为正整数；spriteKey 可省略 |
| Weapon | id/nameZh/nameEn；damage/cooldown/range 为正浮点；automatic 为 bool；spriteKey 可省略 |

物品 width/height 为虚拟背包尺寸，maxStack 为堆叠上限；不是游戏真实背包物品。武器 cooldown 以秒、range 以世界单位、damage 以虚拟生命值计。

### Map

| 字段 | 类型 / 含义 |
|---|---|
| id/nameZh/nameEn | 字符串 |
| width/height | 正浮点，地图宽高；示例 24、16 |
| playerSpawn | `{ "x": 2, "y": 2 }`，必填 |
| enemies | `{ position:{x,y}, health, speed, damage, attackSeconds, spriteKey? }[]` |
| boxes | `{ position:{x,y}, loot:[{itemId,count}], spriteKey? }[]` |
| obstacles | `{ minX, minY, maxX, maxY }[]`；可写 DTO 经 `new Rect(...)` 转 readonly Rect，不直接反序列化 readonly 字段 |
| hasExtraction | bool |
| extractionPoint | `{x,y}`；开启撤离时必填 |
| extractionRadius/extractionSeconds | 正浮点；世界半径、连续停留秒数 |
| skinId | 已注册或本包皮肤 ID |
| loadout | 非空且不重复的武器 ID 数组；跨包引用须在 requires 声明依赖 |

敌人 health/attackSeconds 须正数，speed/damage 可为 0；loot count 须为正整数且不超过物品 maxStack。详尽界限由 `ContentRegistry` 维护。

### Skin

id/nameZh/nameEn；以下 14 个颜色对象可单独省略：

`floor / grid / wall / wallEdge / player / enemy / crate / openCrate / exit / exitEdge / bullet / uiBackground / uiText / uiButton`

每个颜色对象格式 `{ "r": 0.2, "g": 0.6, "b": 0.7, "a": 1 }`，四个通道都须明确写出，范围 0–1；若某颜色**整个对象省略或 null**，Loader 使用安全可见的内置色并设 alpha=1，绝不把整个缺省 skin 直接变为全零透明。若只写 r/g/b、不写 a，显式Loader会将缺失 a 读作0，故不要省略通道；如果确实需要透明，显式写 a=0。

可选 sprite 键：`backgroundKey / playerKey / enemyKey / crateKey / wallKey / itemKey`。未提供 sprite 或查找失败时，框架保持颜色几何渲染；skin 的颜色同时作纹理 tint，测试 PNG 使用白/灰图形以便着色。

## 第三方 PNG 与 SpriteKey

- 将 PNG 放在独立模组的 `Textures/Items/`。**不是 Textures 根目录**：游戏会把 Items PNG 制作为 `mod.Sprites`，框架视觉桥才可取用。不要把 JSON 伪装成 TextAsset 放进 Textures；原版加载器只加载已支持的 PNG/CSV。
- 键格式 `模组manifest ID:PNG文件名去扩展名`，例如 `example.raiddocks:dock_crate`。
- PNG 名称须唯一，建议只用英文字母、数字、下划线，不带 `.png`、目录、冒号；前缀是**模组** ID，当前 Localization 桥协议强制包 ID 也等于该提供者 manifest ID。
- 使用新 PNG 不需要第三方素材 DLL，也不需要引用框架程序集。JSON 和 PNG 都仅描述沙盒内容，不操作原版物品或存档。

## 验证边界

先设置与根README相同的本地Managed/root环境变量并运行完整 [示例build.ps1](RaidDocks/build.ps1)，再从仓库根目录分别运行（Loader运行时也用环境变量核对真实Newtonsoft字节）：

```powershell
.\Examples\Validation\test-content-localization.ps1
```

```powershell
dotnet run --project .\Examples\Validation\AssetValidation.csproj -c Release -- .\Examples\RaidDocks
```

```powershell
dotnet run --project .\Examples\Validation\LoaderValidation.csproj -c Release
```

- `test-content-localization.ps1`：真实生成脚本的安全测试，包括任意深层字符串/属性名含 CR、LF、字面 `\n` 拒绝，provider ID 大小写不匹配拒绝，拒绝不覆盖已有输出，以及引号/逗号/Unicode/空格/安全反斜杠还原。
- `AssetValidation.csproj`：严格解码实际生成及分发 CSV，以原版 `ParseCsvValue` 规则还原并用严格 JSON 解析器验证，与源 pack 做结构化比较；检查 manifest、版本、依赖和 PNG；用 `PEReader` **只读 ECMA-335 metadata** 检查真实 DLL 的 AssemblyRef 恰好只有 `Assembly-CSharp`、`netstandard`，Entry 的 IMod 来源确为游戏 DLL，三个生命周期方法 IL 只有 `ret`。不会加载或执行游戏程序集，也不用替身冒充示例编译。
- [LoaderValidation.csproj](Validation/LoaderValidation.csproj)：链接真实 ContentLoader/Content/RaidModel 等框架源码及与游戏逐字节一致的Newtonsoft DLL，验证 DTO 转换、schema、成本限额、失败原子性和会话快照。**不链接示例 Entry 或假的 IMod**；Entry不是注册方。

两个 .NET 验证项目通过 `Validation/Directory.Build.props` 按项目名隔离 bin/obj，避免共享 apphost 使增量运行错用另一个程序。ModLoader 桥的启停、资产发现、重启用和 provider 所有权测试在框架集成测试中负责，不属于本目录的纯 Loader 测试。真实Newtonsoft的离线测试也不是完整Unity运行验收，metadata测试不代表游戏已启用模组。实际游戏纹理发现、加载排序、CSV 导入、桥启停与 UI 仍需游戏内验收；测试源和替身不会编进发布 DLL。
