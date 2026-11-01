# 作者指南 / Authoring API v1

This is the early **0.2.4 / API v1** authoring guide. Independent providers use the game's localization CSV bridge and must not reference/copy the framework DLL. See the English [project/build/test overview](README.md) and [third-party exclusions](THIRD_PARTY_NOTICES.md). Live-game verification remains pending; offline parsing is not an in-game acceptance test.

## 目标
给原游戏额外拓展俯视角射击小游戏。框架处理运行、射击、碰撞和画面挂载，作者提供物品、地图和视觉。它不依赖枪械主模组，不自动连接原游戏背包和存档。

## 最快开始
1. 以 [Examples/RaidDocks](Examples/RaidDocks) 为模板创建自己的项目，修改模组ID、内容包ID和所有定义ID。仓库中的项目导入根目录 [GameReferences.props](GameReferences.props)，脚本引用 [Resolve-GamePaths.ps1](Resolve-GamePaths.ps1)；移动到仓库外时同时保留这些构建辅助文件并更新相对路径，或自行配置等价的本地引用参数。
2. 编辑 pack.json，替换地图宽高、出生点、墙、敌人、箱子、撤离区、武器及战利品。
3. 把有权使用的PNG放在自己模组的 Textures/Items/，将各spriteKey改为你的manifest ID加冒号及PNG文件名（不含扩展名）。正常build只复制现有PNG，不运行重画脚本；generate-textures.ps1是主动重画原创测试图的工具，替换素材后不要误运行它。
4. 在仓库根目录按 [README 构建说明](README.md#build-from-this-repository-root) 设置已有本地引用的 PROBABLY_STOLEN_MANAGED 或 PROBABLY_STOLEN_ROOT，然后运行 [示例build.ps1](Examples/RaidDocks/build.ps1)。将生成的独立文件夹与RaidPrototype并排安装，在游戏里启用两者；不需要下载游戏或复制游戏DLL来使用不依赖游戏的纯模型测试。
5. 在框架准备界面切换地图；不修改框架源码即可游玩你的地图和武器。

JSON严格字段、默认值与预算见 [schema说明](Examples/README.md)。构建将JSON转换为原游戏可加载的CSV资源；框架不扫描磁盘JSON，内容包DLL只包含空的原游戏IMod入口。

## 独立内容模组必须使用数据桥
当前目标游戏的扫描器不认可仅靠Prerequisites声明的跨模组程序集引用。独立DLL直接引用RaidPrototype会被unlisted-reference拒绝。不要复制框架DLL、改游戏扫描器或用反射绕开。

独立模组只引用Assembly-CSharp，提供空IMod入口，并声明：
```xml
<Prerequisites><Mod>local.raidprototype</Mod></Prerequisites>
```

build.ps1把pack.json生成到Localization/en.csv：单行key为raid_framework_pack_v1，value为带正确CSV引号的紧凑JSON。由原游戏加载CSV和PNG；框架读取LoadedMod.Localization，使用同一ContentRegistry校验注册。pack.id必须精确等于提供者manifest.ID，定义ID仍以pack.id加点开头。示例不会引用或携带框架DLL。

框架在OnModAssetsLoaded及周期补扫导入启用的提供者；禁用、移除或替换数据时撤销对应内容及其依赖闭包。已有对局保持原快照。坏载荷不逐帧重解析，数据/提供者变化或注册集合变化时重试。跨包Requires仍按内容依赖处理；框架停用时尝试按依赖顺序清理。

原版CSV解析会把两个字符反斜杠+n转换成换行，因此生成脚本拒绝JSON字符串中的实际换行和字面反斜杠+n，避免损坏载荷。普通物品名称和描述可正常使用中文及逗号。修改pack.json后运行构建生成新CSV，无需把JSON编译到DLL；仍需游戏重载资源，不提供随意磁盘热重载。

ContentRegistry.Register和ContentLoader.RegisterJson继续作为框架内部/同程序集或离线工具API，独立内容模组不直接调用。默认包与数据桥进入同一注册器，仍无隐藏内容捷径。

## 内容注册与命名
- `ContentRegistry.ApiVersion = 1`，ContentPack.ApiVersion须等于1。JSON必须明确提供apiVersion。
- Pack Id由英文字母、数字、点、下划线、连字符组成。所有定义以 `pack.Id + "."` 开头；ID区分大小写且跨类型唯一。
- 每个包、物品、武器、地图和皮肤都有NameZh/NameEn；UI按游戏语言选择，非中文回退英文。
- Requires填写**内容包ID**，manifest Prerequisites填写**游戏模组ID**。跨包引用必须在Requires声明，且先注册依赖；缺失或重复依赖拒绝加载。
- Register先完整验证再原子写入，不允许部分成功。不覆盖已注册ID，修改内容应先卸载相关依赖再重新注册。
- Unregister未知ID为无操作；仍被其他包依赖则抛InvalidOperationException，需要先卸载依赖方。
- API返回的Packs/Maps/Weapons/Items/Skins及GetX是防御副本；修改返回值不会修改注册表。传入DTO应当视作提交后不可变，不支持调用方同时在其他线程改写输入。

## 地图与会话
坐标原点在左下角，单位不是像素；MapDefinition.Width/Height定义地图空间。矩形障碍用 RaidPrototype.Rect(minX,minY,maxX,maxY)。玩家、敌人、箱子和激活的撤离圆不能在墙内或地图外。

`new RaidModel(mapId)` 创建地图、皮肤、武器和战利品的私有深拷贝快照。运行中撤包或重新注册同ID不会改变已有对局；Reset也使用原会话快照。新对局才加载新内容。

- `Update(seconds, direction)`：移动方向归一化，0..60秒有限时间，内部子步防穿墙；Unity集成每帧最多推进0.05秒。
- `Fire(target)`：一次调用发射一次射线。CurrentWeapon.Automatic供输入层决定按住/单击。射线在最近障碍、活敌人、射程或地图边界停止。
- `SelectWeapon(index)`或`SelectWeapon(id)`选择本图Loadout；旧Pistol/Rifle枚举只为兼容示例，扩展作者不要依赖它表达第三武器。
- `Interact()`打开范围内未开箱子，添加定义的虚拟物资并按MaxStack拆堆；没有真实物品交易。
- `Map.HasExtraction=false`允许没有撤离目标的战斗沙盒；true时按ExtractionRadius/ExtractionSeconds自动检测。当前仍支持死亡失败，不提供自定义胜利脚本。
- `Inventory/GetItem`读取本局虚拟物品。Width/Height目前只是元数据，未做原版格子背包适配、物品装备或医疗效果。

## 视觉
SkinDefinition提供14个RGBA色及背景、墙、角色、敌人、箱子等spriteKey。PNG由原游戏的mod.Sprites查找，未找到时日志每key警告一次、保留几何回退。纹理和颜色分离于地图碰撞；换图不会改变墙体规则。

当前排序：背景为场地前方的同级UI图层；世界几何回退和墙/箱/实体sprite在中间；准星、枪火与血条在前景。地图背景按地图宽高letterbox到场地区域。角色sprite乘以对应Player/Enemy等颜色；彩色原图想保持原色则把对应tint设为全白。侧栏显示当前武器和首个战利品图标，缺图隐藏图标但保留文字。

这不是任意渲染器插件：首版只支持PNG和配色，不支持自定义Shader、骨骼动画或摄像机替换。所有sprite不拦截点击。

## 预算与报错
当前每图最多64敌人、128箱子、256障碍；注册表另检查模拟成本、数量、名字/ID、数值有限性、引用、物品堆叠和地图边界。地图每边2..10000世界单位，但非常大的地图会导致物体显示很小，不代表推荐使用。网格抽稀到每维最多约100条，限制UI网格顶点预算。参数详见ContentRegistry公开常量和校验异常。

JSON在解析前限1,048,576字符和深度32；转换前再查累计节点与数组预算。此机制防常见误配置，不是任意恶意C#模组的安全沙箱，也不保证所有最大合法内容在低配置机器上流畅。

## 当前不承诺的接口
任意行为脚本、AI策略插件、投射物策略、原版库存物品桥、持久化存档、地图编辑器、网络联机均未开放。不要调用内部类或给核心方法打补丁来假定长期兼容。发现表达不了的玩法，先提出明确API需求，不要把游戏存档改写逻辑嵌进示例。

框架0.2.4公开API v1仍处早期，不承诺正式稳定；未来破坏性变化将提升API版本，原版游戏更新也可能需要适配。升级前保留自己的源码和pack.json。0.2.4已经离线验证真实Newtonsoft解析生成CSV中的地图/武器/物品/皮肤及嵌套数组，但实机字体、资源发现、加载顺序、输入、UI与模组启停仍待验证。自动化结果和隔离设计不等于真实存档绝不变化的保证。
