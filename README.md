# Smart Quick Transfer（智能双击投放）

一个用于 Unity 游戏 **Probably Stolen**（版本 0.46）的独立 MOD。它拦截"双击物品"这一操作，把物品快速投放到**最近使用过的机器 / 工具 / 容器窗口**；从机器中取出物品时则回到存放区；没有可用窗口时投放到默认存放区，并播放一段物品飞行的视觉动画。

- **MOD ID**：`smart_quick_transfer`
- **版本**：1.3.4
- **程序集**：`SmartQuickTransfer.dll`
- **目标框架**：.NET Standard 2.1 / C# 7.3（Harmony 2.4.2）
- **依赖**：无。不需要任何其他 MOD 或前置框架
- **许可**：[MIT](LICENSE)

---

## 功能特性

- **双击快速转移**：双击窗口内的物品，自动投放到最近获得焦点的可用窗口，遵循游戏原生的槽位规则。
- **机器取出回库存**：从机器 / 工具窗口双击取出的物品直接回到存放区，不会误投到上一个活动窗口。
- **原生槽位匹配**：电池只进电池槽、模块只进模块仓、便签只进便签槽，堆叠与拆分全部复用游戏原生逻辑。
- **飞行动画**：转移成功时，一个临时图标沿弧线飞向实际落点并在落点短暂高亮；数据提交与动画表现完全分离。
- **不破坏原版交互**：默认保留机器 / 容器的双击开窗行为；单点、拖拽、右键、物品使用均不受影响。
- **全中文配置**：8 个配置项均带中文标签与说明，可按需开关任一行为。

---

## 行为规则

### 双击触发

只有"位于窗口内、可移动、可移除"的物品会被拦截。双击世界中的散落物、展示专用物品、被固定的物品均走原版逻辑。

| 物品类型 | 双击行为 | 控制开关 |
|---|---|---|
| 普通物品（电池、矿石、产物、零件…） | 快速转移 | `enabled` |
| 机器 / 工具本体 | 原版打开窗口 | `keep_machine_double_click`（默认开） |
| 背包、箱子等自带窗口的容器 | 原版打开窗口 | `keep_container_double_click`（默认开） |
| 可鉴定 / 可检视物品（`APPRAISAL_ITEM` / `INSPECTABLE_TAG` 标签，或带查看窗口） | 原版鉴定 / 查看行为，不转移 | 无（始终保留） |
| 彩票 / 刮刮乐（`LOTTERY_TICKET_TAG` / `SCRATCHER_TAG` / `LOTTERY` 类型） | 原版兑奖 / 刮开界面，不转移 | 无（始终保留） |
| 文档类（`DOCUMENT` 标签或类型，含治安部档案 `intel_sec`） | 全部交回原版：带窗口的档案盒双击开窗，治安部档案等纯文档双击进入阅读界面；不转移 | 无（始终保留） |
| 切换类物品（`MayToggleSlotItem` / `CanToggleSlotItem`，如灯具、水容器、外观切换） | 原版切换状态，不转移 | 无（始终保留） |
| 黑灯 / 暗灯（`black_lamp`，`BlackLampHelper.BLACK_LAMP_ID`） | 原版激活行为：切换标记目标（`ActivateSlotItem` → `CycleTarget`），不转移 | 无（始终保留） |
| 鱼饲料（工业鱼饲料 `industrial_fish_feed` / 沟流颗粒 `gutterflow_pellet`） | 原版拆分行为（`ActivateSlotItem` → `Split`，把一份拆成两半），不转移 | 无（始终保留） |

切换判据直接复用游戏的 `MayToggleSlotItem` / `CanToggleSlotItem`，仅对无自带窗口、非机器的物品生效。消耗品（药品、烟酒、食品、疫苗等）与使用类物品（拆弹、拆分、开机器窗口、对话选项、进房间等）以及可选中装备的工具，双击参与快速转移；兑奖 / 刮开、文档阅读、黑灯切换目标与鱼饲料拆分仍保留原版。带窗口的机器 / 容器仍由配置开关决定。

垃圾场（夜间地面）地面上的物品双击后进入该面板的**背包 / 左手 / 右手**口袋，不再投放到仓库；口袋都放不下时物品留在原位。

机器识别采用三重判据（`MACHINE_RECHARGABLE_TAG` / `STANDARD_MACHINE_TAG` 标签、`WindowStyle.Machinery` 样式、`MachineryPanelRow` 内容），任一命中即视为机器。

### 目标窗口选择

1. 确定物品来源窗口（优先按 UI 树，其次按物品图的 `parentInventory → PixelWindow` 链）。
2. 若来源为机器 / 工具窗口，直接走"回库存"路线。
3. 否则按游戏自身的 `focusStamp`（每次 `Show` / `ToFront` 递增）从新到旧扫描可见窗口。
4. 排除来源窗口、不可见窗口、无物品容器的窗口；默认只把可移动窗口视为目标。
5. 取第一个命中的窗口作为唯一目标。**目标一旦确定但不接受该物品，物品留在原位**，不会回退到其他窗口或存放区。

### 目的地

区域名称与游戏界面一致（源码字段见括号）：

| 游戏界面区域 | 源码字段 | 所在位置 |
|---|---|---|
| 仓库 | `invElement` / `invWindow` | 屏幕底部 24×10 大格子（游戏 Ctrl 快速转移的目标） |
| 桌面 | `backInvinvElement` / `backInvWindow` | 屏幕中部 18×4 柜台（游戏 Alt 快速转移的目标） |
| 展示柜 | `showcaseElement` / `showcaseWindow` | 左侧 7×5 陈列格 |
| 前台柜台 | `frontInvinvElement` / `frontInvWindow` | 桌面之上，仅部分场景显示 |
| 夜间地面 | `afterhourInventory` / `afterhourWindow` | 店外 / 打烊后的地面面板 |

| 场景 | 目的地 |
|---|---|
| 物品是当前顾客想买的（最高优先级） | **桌面**柜台，便于直接成交；桌面无可用槽位时再按下面规则处理 |
| 来源是机器 / 工具窗口 | `machine_output_area`，默认**仓库**，满则退到桌面 |
| 来源是垃圾场（夜间地面）的地面物品 | **背包 / 左手 / 右手**口袋，放不下则留在原位 |
| 有可用目标窗口 | 该窗口（原生槽位规则） |
| 无可用目标窗口 | `fallback_area`，默认 `自动`：店内 仓库 → 桌面 → 展示柜 → 前台柜台；店外 夜间地面优先 |

顾客需求判定复用游戏原生 `StoreClient.IsClientBuyingThisItem`（限玩家持有的物品），由 `customer_demand_desk_priority` 控制，默认开启。

> 游戏里没有"随身背包"这个常驻面板：背包是玩家身上的容器物品（`backpack_small/medium/large`），打开的是它自己的窗口，窗口内双击按容器规则处理。

---

## 安装

1. 关闭游戏。
2. 把编译产物整个 `SmartQuickTransfer` 文件夹复制到游戏的 Mods 目录（Steam 正式版：`<游戏安装目录>/Mods/SmartQuickTransfer/`）。
3. 目录结构必须是：

```
Mods/
└── SmartQuickTransfer/
    ├── manifest.xml
    └── SmartQuickTransfer.dll
```

4. 启动游戏 → 主菜单「模组」→ 找到 **Smart Quick Transfer** → 启用。

卸载：在模组菜单关闭开关，或直接删除 `Mods/SmartQuickTransfer` 文件夹。MOD 不写存档、不修改游戏 DLL。

---

## 配置

游戏内：主菜单「模组」→ Smart Quick Transfer 齿轮按钮。配置文件由游戏写入
`%USERPROFILE%/AppData/LocalLow/<公司>/Probably Stolen/ModConfigs/smart_quick_transfer.json`。

| 分组 | 键 | 默认 | 说明 |
|---|---|---|---|
| 常规 | `enabled` | 开 | 双击快速转移总开关 |
| 常规 | `keep_machine_double_click` | 开 | 保留机器双击开窗 |
| 常规 | `keep_container_double_click` | 开 | 保留容器双击开箱 |
| 动画反馈 | `play_flight_animation` | 开 | 播放转移飞行动画 |
| 动画反馈 | `animation_duration` | 0.24 | 飞行动画时长（秒），范围 0.1–0.6 |
| 目标选择 | `include_store_panels` | 关 | 常驻存放面板（仓库/桌面/展示柜/前台柜台/夜间地面）也可作为目标 |
| 目标选择 | `customer_demand_desk_priority` | 开 | 当前顾客想买被双击的物品时，优先投放到桌面柜台；桌面放不下时回退到原有规则 |
| 目标选择 | `fallback_area` | 自动 | 无目标窗口时的去向：自动 / 仓库 / 桌面 / 展示柜 / 前台柜台 / 夜间地面 |
| 目标选择 | `machine_output_area` | 仓库 | 从机器取出物品的去向：自动 / 仓库 / 桌面 / 展示柜 / 前台柜台 |

> 1.2.1 及更早使用 `desktop_target` / `machine_return_target`，且"随身背包 / 后场仓库"标签与面板实际位置不符。旧键不再读取；旧标签按物理去向归一化（随身背包 → 仓库、后场仓库 → 桌面）。

---

## 从源码构建

源码依赖游戏安装目录下的托管程序集（`Assembly-CSharp.dll`、`0Harmony.dll`、Unity 各模块等），这些文件**未包含在仓库中**，需提供游戏路径。

使用 .NET SDK：

```powershell
pwsh ./build.ps1 -GameManagedDir "C:\path\to\Probably Stolen_Data\Managed"
```

使用 Roslyn 命令行编译器：

```powershell
pwsh ./build.ps1 -CompilerDirectory "C:\path\to\roslyn\bincore"
```

产物输出到 `../release/`，并自动复制 `manifest.xml`。也可直接用 `dotnet build SmartQuickTransfer.csproj -c Release -p:GameManagedDir="..."`。

---

## 代码结构

```
.
├── ModEntry.cs                         IMod 入口、日志、配置初始化
├── AssemblyInfo.cs                     程序集元数据
├── SmartQuickTransfer.csproj           工程文件（netstandard2.1）
├── manifest.xml                        MOD 清单
├── build.ps1                           构建脚本（SDK / Roslyn 两种模式）
├── Core/
│   ├── TransferSettings.cs             ModConfig 封装（中文标签）
│   ├── TransferOutcome.cs              转移结果枚举
│   ├── WindowActivityManager.cs        来源窗口识别、MRU 目标选择、窗口资格判定
│   └── ItemTransferManager.cs          统一转移流程（含机器取出分流）
├── Transfer/
│   ├── ContainerTransfer.cs            容器 / 机器窗口投放 + UI 刷新
│   ├── MachineSlotResolver.cs          原生槽位校验 + 机器识别
│   └── DesktopTransfer.cs              库存 / 回退区解析与投放
├── Visuals/
│   ├── FlightCapture.cs                起点快照（图标、尺寸、世界坐标、相机、原容器）
│   ├── FlightDriver.cs                 逐帧驱动：贝塞尔曲线 + 缩放 + 落地高亮
│   └── TransferAnimation.cs            Ghost 创建与启动
└── Patches/
    └── ItemDoubleClickPatch.cs         Harmony Prefix（唯一补丁）
```

唯一的 Harmony 补丁：

```csharp
[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), "DoubleClickAction")]
```

Prefix 返回 `false` 时只跳过"打开窗口 / 使用物品"的最终动作，双击判定状态仍由原版正常清理，不会卡住后续点击。

---

## 日志

日志级别只使用 **info / error** 两种。游戏默认不带控制台（`DevConsoleStripper` 会在没有开发标记时销毁 `QuantumConsole`），两种取日志方式：

1. 在游戏根目录（`Probably Stolen.exe` 所在目录）新建空文件 `dev.txt`，重启游戏，按 **F8** 打开控制台，输入 `SmartQuickTransfer` 过滤。
2. 直接读 Unity 日志：`%USERPROFILE%\AppData\LocalLow\<公司名>\Probably Stolen\Player.log`。

```
[SmartQuickTransfer] moved 1 x 'battery_basic' from 'Recharger [machine_recharger]' into 'Recharger [machine_recharger]' (machinery/tool window); item now in 其它容器.
[SmartQuickTransfer] taken out of a machine; moved 2 x 'scrap_metal' from 'Recycler [machine_recycler]' into 仓库; item now in 仓库.
[SmartQuickTransfer] target 'Recharger' has no slot that accepts 'sword_rusty'; item kept in place.
[SmartQuickTransfer] flight canvas created at sorting order 30000.
[SmartQuickTransfer] flight animation started (612 px, icon 32x32, layer 30000, start 512,388 -> end 480,120).
```

- `item now in …` 是移动完成后回读的真实区域，必须与目的地一致，否则说明发生了二次移动。
- `start x,y -> end x,y` 是屏幕像素坐标，用于判断异常出在起点、终点还是首帧。

---

## 许可

本项目以 **MIT License** 开源，详见 [LICENSE](LICENSE)。
