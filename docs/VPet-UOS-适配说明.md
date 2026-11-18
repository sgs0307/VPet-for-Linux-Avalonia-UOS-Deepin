# VPet 桌宠 · 统信 UOS 适配说明

本文记录"打开后和预想不一样"问题的排查结论、已做的修复，以及在统信 UOS 上编译运行与排错的方法。

---

## 一、为什么打开后只有一行「虚拟桌宠名字:」

排查后确认不是一个 bug，而是**整条链路上有 6 处断点**，任何一处都会导致宠物不显示：

| # | 位置 | 问题 |
|---|------|------|
| 1 | `PetLoader` | 全程没有被调用过，程序从未加载任何宠物资源 |
| 2 | `GameCore` | `Save` / `Controller` / `TouchEvent` 全是 null，只有空壳对象 |
| 3 | `Picture` | `LoadGraph` 和 `Run` 都是空实现，静态图永远不加载 |
| 4 | `GraphInfo` | 从文件构造时是简化版，所有动画都叫 `default`、类型都是 `Common`，按类型查找必然落空 |
| 5 | `UIElementAdapter` | `SetContent` 只接受 `Control`，而动画层传进来的是图片路径字符串，被静默丢弃 |
| 6 | `Main.xaml` | 界面上那行文字是 XAML 里写死的占位文本，不是数据绑定 |

此外还有两个必然在 Linux 上踩到的坑：

- **主窗口形态不对**：原来是 1000×600、带标题栏的普通应用窗口，不是透明桌宠。
- **计时器没有启动**：`EventTimer` 创建后既没绑 `Elapsed` 也没 `Start()`，宠物不会自己动、数值不会变化。

---

## 二、已做的修复

### 1. 补全启动链路（新增 `GameBootstrap.cs`）
- 自动定位 `mod` 目录：环境变量 `VPET_MOD` → 程序目录 → 向上 6 级 → 源码目录
- 解析 `mod/0000_core/pet/*.lps`，用 `PetLoader` 加载全部动画
- 创建存档（首次启动自动初始化数值）、绑定控制器、注册摸头/摸身体触摸区
- 启动主循环并立即播放默认动画

### 2. 主窗口改为真正的桌宠形态（`MainWindow.axaml` / `.cs` / `.Controller.cs`）
- 无边框、背景透明、始终置顶、不进任务栏
- 铺满整个屏幕工作区，宠物贴在屏幕底部居中
- 可拖拽（拖拽后自动夹在屏幕内，松手靠边）
- 右键菜单：数据面板 / 摸摸头 / 说说话 / 置顶 / 穿透 / 回底部 / 退出
- 窗口关闭时保存存档并释放位图缓存

### 3. 渲染管线（`PetImageElement.cs` 新增，`Picture` / `PNGAnimation` 重写）
- 新增 `PetImageElement`：把 `IUIElement` 落到真实 `Image` 控件上，负责解码位图、切回 UI 线程、缓存位图
- `Picture`：`LoadGraph` / `Run` 补全实现
- `PNGAnimation`：
  - 帧按**自然排序**（旧代码字典序会把 1,10,11,2 排错，动画顺序全乱）
  - 用 async 循环替代 `Thread.Sleep` + 递归 `Task.Run`（旧实现每帧新建线程，长时间运行会堆积线程卡死）
  - 空闲缓存清理不再清空帧索引（旧逻辑会让动画跑一段时间后永久失效）
- 显示元素/位图缓存只创建一次，避免每帧重建控件导致闪屏

### 4. Linux / 统信 UOS 专项适配
- **大小写**：Linux 的 ext4 区分大小写，`GetFiles("*.png")` 匹配不到 `.PNG`。改为按扩展名过滤（`Function.GetFilesByExtension`），并新增 `FindEntryCaseInsensitive` 做目录/文件名的不敏感查找
- **路径分隔符**：MOD 的 `info.lps` 多在 Windows 下编写，路径里是 `\`。新增 `Function.NormalizePath` / `CombinePath` 统一转换
- **只读程序目录**：UOS 上程序常装在 `/opt`、`/usr`，缓存和存档原来写在程序目录会直接失败。现统一改到 `~/.local/share/VPet/`
- **目录枚举顺序**：Linux 不保证顺序稳定，改为显式排序，保证每次启动表现一致
- **会话探测与降级**：运行时判断 X11 / Wayland
  - X11：用 XShape 改写窗口输入区域，实现"只有宠物能点，其余穿透"
  - Wayland：协议不允许客户端改写输入区域，自动关闭穿透，并把窗口退化为宠物大小的贴底小窗口，避免整屏出现不透明方块
  - 缺少 `libXext` 时同样自动降级，不会崩溃

### 5. 其他
- 补上 Avalonia 主题包 `Avalonia.Themes.Fluent` —— 缺失时 `MenuItem` / `Button` / `ProgressBar` 这类需要控件模板的控件不会渲染
- 右键菜单改为自绘（`Border` + `TextBlock`），不依赖主题也能用
- 对话气泡修正（原来只赋值文本却不显示控件），并加上 4 秒自动隐藏
- 修掉 `Tests/TestProgram.cs` 的编译错误（静态成员用实例访问、`PetLoader` 无无参构造）——它会导致 `VPet-Avalonia.Core.Migrated` 整个项目编译失败
- `GraphCoreAvalonia.Config` 从简化版改为真实解析 `touchhead` / `touchbody` / `touchraised` / `duration` / `work` / `move`
- `AddGraph` 补登记 `GraphsALL`，否则空闲缓存清理永远遍历不到任何动画

---

## 三、在统信 UOS 上编译运行

```bash
cd VPet-main
dotnet build VPet-Avalonia.App/VPet-Avalonia.App.csproj -c Release

# 资源已配置随程序输出到 mod/ 目录；若资源很大不想每次复制：
# dotnet build ... -p:CopyModResources=false

dotnet run --project VPet-Avalonia.App -c Release
```

需要 .NET 8 SDK 与 `libicu`（UOS 一般自带）。若报缺少 `libicu`，可临时用：
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run ...`

---

## 四、排错指南

启动时会往终端打印 `[VPet] ...`，按顺序检查：

| 现象 | 原因与处理 |
|------|-----------|
| `未找到 mod 目录` | 资源没跟随输出。确认输出目录下有 `mod/0000_core/pet/`；也可用 `VPET_MOD=/path/to/mod` 指定 |
| `动画加载完成, 共 0 组` | `info.lps` 或图片路径问题，看终端里 `资源不存在` / `未知动画类型` 的输出 |
| 窗口透明但看不到宠物 | 先确认上面"共 N 组"的 N 大于 0；再看是否有 `图片不存在` 输出 |
| 整屏一个不透明方块 | 当前是 Wayland 会话或合成器未开启透明。终端会打印 `检测到 Wayland 会话`；可切到 X11 会话，或在系统设置里开启窗口特效 |
| 鼠标穿透无效 | 终端会打印 `XShape 不可用` 或 `Wayland`；确认 `libxext6` 已安装、会话为 X11 |
| 宠物不动 | 主循环 15 秒一跳属正常节奏；若完全不动，检查是否有 `主循环异常` 输出 |
| 右键菜单不出现 | 必须在宠物本体区域（可交互区）上右键，透明穿透区域收不到事件 |

常用环境变量：
- `VPET_MOD=<路径>` 指定 MOD 资源目录
- `XDG_SESSION_TYPE=x11` 强制按 X11 处理（仅用于排查）

---

## 五、离线类型检查工具

本机（开发容器）的 MSBuild 因虚拟内存受限无法启动，项目里附带了直接用 Roslyn `csc` 的检查脚本：

```bash
bash .workbuddy/tools/typecheck/check.sh
```

它会在几秒内完成全量源码的语法/类型检查（需先有一次 `dotnet build` 产物提供依赖 dll）。

---

## 五、实机运行验证后追加的修复（2026-11-03 第二轮）

第一轮修复后在 UOS 实机运行，又发现并修复了 4 个只有跑起来才会暴露的问题：

| # | 现象 | 根因 | 修复 |
|---|------|------|------|
| 1 | 启动几秒后 `Stack overflow` 崩溃 | `Function.FindEntryCaseInsensitive` 的"递归兜底"会把**子目录的 info.lps** 错配给父目录，导致只有 Drink 一个目录的动画被加载、`Default` 呼吸动画缺失；`DisplayDefault→DisplayToNomal` 回调链同步无限递归 | 去掉递归兜底，改为逐段大小写不敏感解析相对路径；`DisplayDefault` 加重入保护 |
| 2 | 第二次 `Stack overflow` | `SayRndFunction` 写死返回 `"say"`，但根本不存在叫 say 的动画（实际叫 self/serious/shining/shy），开场白一触发就死循环 | 改为从 `GraphType.Say` 已加载动画中随机选取（与原版一致），并在 `Display` 层加"连续找不到动画"计数，超过 8 次自动打断回调链 |
| 3 | 日志全部重复打印两遍 | `GameBootstrap` 内部日志和调用方回调都写控制台 | 调用方不再重复传回调 |
| 4 | 主循环报 `Call from invalid thread` | `System.Timers.Timer` 在线程池线程触发，直接访问 Avalonia 控件 | 主循环整体 `Post` 到 UI 线程执行 |

### 验证结果

- `dotnet build -c Release` 通过（0 错误）
- 实机运行 60 秒：621 组动画全部加载（23 个类型），零异常，宠物正常渲染并播放呼吸动画
- 当前机器是 **Deepin + Wayland** 会话：按预期自动降级（无鼠标穿透，窗口由合成器决定位置）

### 关于 dotnet 命令找不到

本机 .NET SDK 装在 `$HOME/dotnet`，未加入 PATH。已在 `~/.bashrc` 追加（原文件备份为 `~/.bashrc.bak-20261103`）：

```bash
export DOTNET_ROOT=$HOME/dotnet
export PATH="$PATH:$HOME/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

新开终端即可直接使用 `dotnet`。当前终端先执行 `source ~/.bashrc`。

另外 `Avalonia.Themes.Fluent` 包此前因 nuget.org 源被禁用而还原失败，已通过
`dotnet restore --source https://api.nuget.org/v3/index.json` 拉入本地缓存，之后离线也能编译。

---

## 六、摸头动画卡死与数据面板打不开（2026-11-03 第三轮）

第二轮修复后实机运行又暴露两个用户可直接感知的问题：**摸头后动画永久卡住**、**右键菜单"数据面板"打开是空壳**。

### 1. 摸头动画卡死（三重根因叠加）

| # | 根因 | 修复 |
|---|------|------|
| ① | `TaskControl.PlayState` 只在 `Stop()` 时置 false，动画**自然播完不重置** → 下次重播时 SetContinue 挂在已退出的旧循环上 | ITaskControl 新增 `Finish()`（自然结束专用，不重置回调链）；PNGAnimation/Picture 所有结束路径改用 `Finish()` |
| ② | 动画结束回调在后台线程访问 `PetGrid.Tag` 抛 `Call from invalid thread`（日志定位到 `[touch_head]Touch_Head_NA`）→ 摸头后的回调链静默断裂，宠物停在最后一帧 | `Main.Display` 拆成两层：`Dispatcher.UIThread.Post` 到 UI 线程后执行 `DisplayOnUIThread`，endAction 也包装为 Post 回 UI 线程再调用；外层 catch 兜底 `DisplayToNomal` |
| ③ | 切换动画时不停旧循环 → 新旧两个循环并发写同一个 Image | 新增 `_currentGraph` 字段，`Display` 入口处 `!ReferenceEquals(_currentGraph, graph)` 时先 `_currentGraph.Stop(true)` |

顺带修复：`DisplayType` 初始值由默认 Common 改为 `GraphInfo("", GraphType.Default)`（否则启动开场白 `Say` 的类型判断失败）；`DispatcherAdapter` 的 Invoke 全部加 `CheckAccess()` 短路防死锁。

### 2. 数据面板打不开（双根因）

| # | 根因 | 修复 |
|---|------|------|
| ① | `ShowPanel()` 只切 `ToolBar.IsVisible`，但数值区 `BdrPanel` 默认 `IsVisible=False` → 显示出来是空壳 | ToolBar 新增 `TogglePanel()`：连 BdrPanel 一起切换 + `RefreshValues()` 实时填充数值 + 800ms DispatcherTimer 定期刷新 |
| ② | X11 穿透模式下菜单刚 `IsVisible=true` 时 Bounds 高度还是 0 → XShape 输入区域按 0 高度生成 → 菜单/面板**看得见点不到** | `HookLayoutRefresh()`：ContextPanel/ToolBar 的 `LayoutUpdated` 事件里在可见时重调 `UpdateClickThrough` 重算输入区域 |

另加菜单项 300ms 防抖 + `e.Handled = true`，防止 XWayland 事件重复派发导致一次点击触发两次。

### 3. 偶发断链：`找不到动画: A_Start Nomal`

根因：`Move` 类属性没有 `[Line]` 特性，LPSConvert 反序列化**不映射** vup.lps 里 `move` 行的 `graph` 字段 → 16 个 move 的动画名全是空串 → EventTimer 定时触发的移动动画连续 8 次找不到、触发自动打断保护。

修复：`Move` 加 `[Line(ignoreCase:true)]` 的 `Graph` 属性 + `GraphName` 兜底属性；`GraphCore.Avalonia` 解析 move 行时手动补读 `line.Find("graph")`；`DisplayToMove` 改用 `move.GraphName`。

### 验证结果

- `dotnet build -c Release` 通过（0 错误）
- 摸头 4 次后双帧截图 md5 不同 → 动画正常恢复播放
- 60 秒运行（含 4 个 EventTimer 周期）grep "找不到\|异常\|失败" = 0
- 数据面板显示真实数值：Lv 1 / $1000 / 经验 3/100 / 体力 87 / 心情 68 / 饱腹 95 / 口渴 95
  （截图存于 `.workbuddy/验证截图-数据面板.png`）

### 本轮沉淀的 Avalonia 移植经验

1. **动画状态机必须有"自然结束"与"手动停止"两条路径**：自然结束不清回调链，手动停止清，混用会出现卡死或断链。
2. **所有涉及控件访问的回调一律 `Dispatcher.UIThread.Post`**：Avalonia 的线程检查比 WPF 严格，后台线程碰控件直接抛异常且会把回调链静默打断。
3. **LPSConvert 只映射带 `[Line]` 特性的属性**：移植原版类时逐属性核对特性，否则字段悄悄变空串。
4. **XShape 输入区域必须在 LayoutUpdated 之后重算**：`IsVisible=true` ≠ Bounds 已就绪。
5. **XWayland 下 xdotool 坐标不可信**：模拟点击验证行为时以程序事件日志为准，不要被截图偏差误导。

---

## 七、无法拖动（2026-11-03 第四轮）

### 现象

按住宠物拖动，宠物纹丝不动。

### 根因：会话类型探测把 XWayland 误判成"纯 Wayland"

`LinuxWindowNative.DetectSession()` 原来优先看 `XDG_SESSION_TYPE`。本机桌面是 Wayland 会话
（`XDG_SESSION_TYPE=wayland`、`WAYLAND_DISPLAY=wayland-0`），但 **Avalonia 实际初始化的是 X11 后端**
（通过 `DISPLAY=:0` 走 XWayland，崩溃栈里就是 `Avalonia.X11.AvaloniaX11Platform.Initialize`）。

误判导致程序走了"Wayland 降级路径"——窗口被缩成宠物大小的小窗：

1. **拖动必然失效**：`MoveWindows` 移动的是窗口内 Canvas 坐标，`SetPetPosition` 夹在
   `[0, _viewWidth - petW]` 区间；小窗口下 `_viewWidth == petW`，区间退化成 `{0}`，永远被夹回 (0,0)。
2. **数据面板只能叠在宠物身上**（上一轮验证截图里面板与宠物重叠正是降级形态的佐证）。
3. **鼠标穿透直接关闭**（而 XShape 在 XWayland 上明明可用，第三轮已在 X11 路径验证过）。

### 修复

1. **`DetectSession` 改按真实后端判断**：`DISPLAY` 存在即 X11（含 XWayland），
   只有完全没有 `DISPLAY` 时才走 Wayland 降级路径。
2. **拖动链路加固**：`MainGrid_PointerPressed` 里显式 `e.Pointer.Capture(MainGrid)`，
   `PointerReleased` 里释放。X11 + XShape 输入区域下拖动会离开宠物矩形，
   显式捕获保证 `PointerMoved` 全程不断流。
3. **新增 `VPET_SOFTWARE_RENDER=1` 环境变量**：强制 Skia 软件渲染，
   用于 GPU 驱动异常环境的诊断/兜底（生产行为不变）。

### 验证

- `dotnet build -c Release` 通过（0 错误）
- 实机模拟拖拽：从宠物中心 (1090,1170) 拖到 (450,550)，宠物从 (1090,1150) 移动到 (720,780)，
  完整跟随拖拽方向（截图存于 `.workbuddy/验证截图-拖动前.png` / `验证截图-拖动后.png`）
- 附带验证：拖拽起点打偏落在壁纸时事件正常穿透（XShape 生效）

### 本轮经验

**不要按桌面会话类型（XDG_SESSION_TYPE）判断窗口能力，要按实际初始化的渲染后端判断。**
Wayland 桌面 + XWayland 下，X11 的全屏透明窗口、XShape 输入区域、窗口定位全部可用；
按"Wayland"降级反而制造问题（拖动失效、面板叠宠物、无穿透）。

---

## 八、第一批玩法闭环：投喂 / 工作 / 商店 / 自动保存（2026-11-04 第五轮，v1.1.0）

### 本轮目标

原版 VPet 的核心玩法闭环是「赚钱 → 买吃的 → 喂 → 回数值 → 再生病/消耗 → 再赚钱」。
v1.0.0 只移植了数值系统和状态机，缺全部 UI 入口，玩法是断的。本轮把这条链补上。

### 新增能力

| 模块 | 行为 |
|---|---|
| 投喂 | 右键「投喂」列出现有食物（按 正餐/零食/饮料/药物 分组、按价格排序），点击后播放 A_Start → B_Loop×2 → C_End → Nomal，扣背包 + 加数值 + 气泡播报效果 |
| 食物数据 | `FoodLoader` 扫描 `mod/*/food/*.lps`，本轮载入 **122 种**食物；`ApplyChineseNames` 合并 `lang/zh-Hans/*.lps` 做中文名替换 |
| 背包 | `inventory.txt` 与存档同目录独立持久化（不污染原 LPS 存档格式）；首次启动发放 4 份新手补给 |
| 商店 | 「商店」面板：顶部金钱、商品列表（名称/价格/效果）、点击购买扣钱入包；钱不够给气泡提示 |
| 工作/学习/玩耍 | 「工作」「学习」菜单动态列出 `GraphConfig.Works`，显示类型与时长收益；再次点击同一项=停止；`WorkTimer` 每秒推进、按真实时间结算 |
| 睡觉 | 一键切换 `WorkingState.Sleep`，气泡提示，唤醒复位 |
| 自动保存 | 5 分钟定时保存（存档 + 背包），退出时再存一次 |

### 关键 Bug 与根因（三个都值得单独记）

#### 1. 投喂后数值完全不变 —— LinePutScript 大小写敏感

`food.lps` 里数值字段是首字母大写：

```
food#Steak#Meal#...
StrengthFood#300
StrengthDrink#-20
```

而取值代码写的是 `line.Find("strengthfood")`。`Line.Find` 是**严格区分大小写**的字符串比较，
于是所有食物效果全部读成 0 —— 动画照播、库存照扣、数值一点不动，非常迷惑。

修复：`Food.cs` 里不再用 `Find`，改成自写的 `FindSub`，遍历 `ISub` 并用
`StringComparison.OrdinalIgnoreCase` 比较。

#### 2. 启动后宠物躺着不动、动画 mode=Ill —— 存档解析方式错误

`GameSave.Load` 原来写的是 `new Line("vpet", text)`。这个构造函数把**整段文本塞进 Info**，
不产生任何 Sub 项，于是 `LPSConvert` 映射不到任何字段 → 所有数值为 0 → `Health = 0` →
`CalMode()` 判定为 `Ill` → 播放生病躺倒动画。表现就是"桌宠一启动就躺尸"。

修复：改用 `new LpsDocument(text)` 真正解析出带子项的行再逐字段取值。

**同一处还有第二次踩坑**：存档里字段名是驼峰 `strengthFood`，查找用了全小写
`strength`/`strengthfood`，结果饱腹口渴依然是 0。同样用忽略大小写的 `FindSub` 解决。

#### 3. 右键菜单"看得见、点不动" —— 输入区域自我触发死循环

`UpdateClickThrough()` 调用 `SetInputRegions()` → 触发控件重排 → `LayoutUpdated` →
又调回 `UpdateClickThrough()` → 无限循环。日志里 `输入区域 2 个` 每秒刷几十条，
主线程被彻底占满，X11 事件队列得不到处理，菜单画出来了但点不动。

修复两层：

1. **签名去重**：把矩形列表拼成字符串存 `_lastRegionKey`，区域没变就直接 return；
   再加 `_updatingInput` 防重入标志，`SetInputRegions` 期间屏蔽回调。
2. **高度兜底**：菜单刚 `IsVisible = true` 时 `Bounds.Height` 还是 0，登记出的输入区域高度为 0。
   用估算值兜底 `Math.Max(120, itemCount * 38 + 16)`，并在 `LayoutUpdated` 里重算修正。

> 顺带记录一次失败尝试：曾把「多矩形一次性 `ShapeSet`」改成「逐矩形 `ShapeSet` +
> `ShapeUnion`」，实测左键完全收不到、区域全失效，已回退。原始多矩形一次性 `ShapeSet`
> 在 v1.0.0 已被实机验证可用，不要再动。

### 测试方法改进：`VPET_SELFTEST=1` 自检模式

本轮在 `MainWindow` 里加了一个自检入口：设 `VPET_SELFTEST=1` 启动后，
`RunSelfTestAsync()` 会绕过不稳定的 UI 输入模拟，直接程序化跑一遍：

1. 食物加载数量 + 中文名抽查
2. 投喂（先把饱腹/口渴压到 50，再喂效果最大的食物，比对前后数值 + 库存）
3. 商店购买（比对金钱与持有数）
4. 工作 20 秒（比对金钱/经验/体力/计时）
5. 停止工作（状态复位）
6. 睡觉 / 唤醒
7. 存档读写全字段

这台机器（软件渲染 + 内存紧张）上 X11 左键事件本身就偶发丢，靠 xdotool 截图验证不可靠。
自检模式把"逻辑是否正确"和"环境是否能点"彻底解耦，是这轮效率提升最大的一件事。

### 验证结果（自检日志节选）

```
食物加载: 122 种
投喂 鸡祥如意: 饱腹 50.0->100.0, 口渴 50.0->45.0, 心情 60.0->100.0, 库存 1->0
购买 戒指糖: 金钱 1000.0->997.5, 持有 3
StartWork: ok=True State=Work NowWork=文案 TimerIsWorking=True
20 秒后: 金钱 997.50->998.02, 经验=0.1, 体力=99.9, 饱腹=49.9, 计时=0.32分
StopWork: State=Nomal NowWork=null TimerIsWorking=False
睡觉: State=Sleep → 唤醒: State=Nomal
读档: 萝莉斯 金钱=1000 等级=1 体力=100 饱腹=100 口渴=100 心情=60 健康=100
```

### 本轮经验

1. **LinePutScript 的字段查找一律忽略大小写**：原版 WPF 的 `Line.Find` 区分大小写，
   mod 作者写字段时大小写并不统一（数值首字母大写、存档驼峰），照抄必踩。
2. **`new Line(name, text)` 不是"解析"，是"整段塞进 Info"**：任何要 `LPSConvert` 的场合
   都必须用 `LpsDocument`。
3. **Avalonia 里 `LayoutUpdated` 是高频事件**：任何在它里面做副作用（改输入区域、改布局）
   的代码都必须自带去重与防重入，否则必然自激。
4. **功能逻辑用自检跑，UI 交互用人工点**：环境不可靠时不要在 UI 模拟上耗时间。

### 补充：发布复验时又揪出两个动画 bug（同日）

前四个功能逻辑自检全绿，但把产物真正装到 `~/.local/share/vpet` 跑时发现
日志里仍刷"连续多次找不到动画, 已打断回调链"，说明动画链是断的。继续挖出两处：

#### 4. `FindGraph` 缺少状态降级 —— 表情动画大面积查不到

移植版的 `FindGraph` 只做严格相等：`graph.GraphInfo.ModeType == mode`。而 mod 里很多表情
（`shy` / `serious` / `self` 等）只在 `Nomal` 下定义，宠物一旦进入 `Happy`
（心情 > 80 就是 Happy），这些动画全部查不到。

原版 `GraphCore.FindGraph` 有完整的降级链，移植时被简化掉了。现已按原版补齐：

```
精确匹配当前状态
  → 生病(Ill) 直接返回 null，不降级（生病就是要躺着）
  → mode+1（更"普通"的状态，Happy→Nomal→PoorCondition）
  → mode-1（更"高兴"的状态）
  → 任意非生病动画
```

> 枚举顺序容易搞反：`Happy=0, Nomal=1, PoorCondition=2, Ill=3`，
> 所以"向下兼容"是 `mode+1`，"向上兼容"是 `mode-1`。

#### 5. 进食动画的动作类型是 `Single`，不是 `A_Start`

`Eat/Nomal/info.lps` 里写的是：

```
FoodAnimation#eat:|mode#nomal:|a0#175,205,23,60,0,0.375:|...
```

名字 `eat` 没有 `_A/_B/_C` 后缀，`GraphInfo` 解析出的 `Animat` 就是 `Single`
（整段自己播完），`ModeType` 来自 `mode#nomal`。之前按 `A_Start` 去找，
`d3.TryGetValue(A_Start, ...)` 必然失败 → 投喂只是数值变了、没有任何进食画面，
并且触发的回调链会连续命中 null 直到被打断。

修复：`Display(graph, AnimatType.Single, () => DisplayToNomal())`。

> 顺带补了一个坑：`Display(名字, 动作, Action<string>)` 这个重载找到 null 时是**静默**的
> （不像另一个重载会打印"找不到动画"），导致问题只能从"连续多次找不到动画"侧面看出。
> 已给它补上同样的日志。

### 复验结果

```
[TEST] 食物=122 背包=7 金钱=1000.0
[VPet] 投喂: 鸡祥如意 剩余 0
[TEST] 投喂 鸡祥如意: 饱腹 50.0->100.0, 口渴 50.0->45.0, 心情 60.0->100.0, 库存 1->0
[VPet] 购买: 戒指糖 花费 2.5 余额 997.5
[TEST] 购买 戒指糖: 金钱 1000.0->997.5, 持有 6
[TEST] 开始工作 文案: ok=True State=Work NowWork=文案 TimerIsWorking=True
[TEST] 工作 20 秒: 金钱 997.50->998.10, 经验=100.1, 体力=99.9, 饱腹=99.9, 计时=0.32分
[TEST] 停止后 State=Nomal NowWork=null TimerIsWorking=False
[TEST] 睡觉后 State=Sleep
[TEST] 唤醒后 State=Nomal
```

关键点：**整份日志里已经没有任何"找不到动画"**，投喂前后也不再打断回调链。

### 环境侧的三个硬坑（这台机器特有，别浪费时间）

1. **`/tmp` 是 10MB 的 tmpfs**：`dotnet publish -o /tmp/xxx` 必然
   `No space left on device`，还会因为 MSB3026 重试 10 次刷几百行日志。
   发布一律 `-o ~/...`。
2. **`DOTNET_GCHeapHardLimit` 不是越大越好**：本机可提交内存有限，
   `0x28000000`(640MB) / `0x40000000`(1GB) 直接
   `GC heap initialization failed with error 0x8007000E`；
   `0x1C000000`(448MB) 能起 CLR 但 Avalonia 初始化时 OOM；
   只有 `0x18000000`(384MB) 能完整跑完自检。调这个值要一档一档试。
3. **`publish/` 里的 dll 一定要核对时间戳**：本轮曾出现"编译成功、安装脚本也跑完，
   但 `publish/VPet-Avalonia.App.dll` 还是上一次的"——因为上一次 `build.sh` 在
   复制阶段失败（同样因为磁盘/内存），目录保留了旧产物，`install.sh` 照抄一遍旧文件。
   验证方式：比对 `publish/*.dll` 的 mtime 与源码编译时间。

---

## 九、v1.1.1：三个交互反馈的修复（2026-11-04 第六轮）

用户实机使用反馈三个问题，全部定位修复并自检通过：

### 1. 只能在一块屏拖动宠物

**根因**：窗口用 `ScreenFromVisual` 只对准了一块显示器（副屏）。桌宠是
"全屏透明窗 + XShape 输入区域"实现，窗口覆盖不到的屏幕上鼠标事件直接落到桌面，
拖到主屏就没有窗口接收 `PointerMoved`，拖动自然中断。

**为什么不用一个大窗口盖住所有屏（虚拟桌面并集）**：实测在 X11 + 混合缩放
（本机 1.0x 主屏 + 1.25x 副屏）下，WM 会把超大的 override-redirect 窗口钳制回
单屏区域，得不偿失。

**最终方案：窗口跟屏走**：

- `SetupWindowBounds`：窗口尺寸 = 宠物所在屏的逻辑尺寸，位置 = 该屏左上角；
- `MainGrid_PointerMoved` 拖动中检测指针出窗 → 调 `TrySwitchScreen`；
- `TrySwitchScreen`：**自己按物理矩形判断目标屏**（不用 `ScreenFromPoint`，
  窗口没真正移动前它返回旧屏），换屏时把宠物的物理坐标换算到新屏坐标系，
  保持视觉位置不跳变，然后重对准窗口。

**关键坑：不能用 `Position` 反推坐标**。Avalonia 的窗口位置/尺寸是异步生效的，
刚设完立刻读回来还是旧值。一律以 `_screen.Bounds`（物理矩形）为基准换算。

### 2. 二级菜单（工作/学习/投喂列表）打开后菜单整体收起

**根因**：菜单项的点击回调里无条件执行 `HideContextMenu()`。根菜单项
「工作」「学习」的语义是"展开子菜单"而不是"执行动作"，点一下就被隐藏了，
用户要再点一次右键才能看到子列表。

**修复**：`_keepMenuOpen` 标志 —— 展开子菜单的动作置位该标志，点击回调末尾
看到置位就不隐藏，改为 `RepositionContextPanel()`（子菜单条目数不同，
要重摆位置 + 重刷 XShape 输入区域）；点具体条目（选工作、买食物）仍然正常收起。

**顺带**：菜单面板 170 → 210 逻辑像素，中文条目不再截断。

### 3. 选择工作后宠物只站着，不播工作动画

**根因**：`StartWork` 只做了 `State/NowWork/WorkTimer.Start()` 接线，
从头到尾没调用过 `work.Display(host)`——原版 WPF 里开始工作会播放对应的
工作动画（`workone` 系列），移植版把这个调用丢了。

**修复**：`StartWork` 尾部接 `work.Display(this)`（Main 实现 `IDisplayHost`
的 `Display(string, AnimatType)`），开始播 A_Start 循环；停止/完成播 C_End
后 `DisplayToNomal()` 回默认。

### 自检结果（v1.1.1，关键行）

```
[TEST] 工作动画: 期望=workone 实际=workone/A_Start/Happy
[TEST] 停止后动画=workone/C_End
[TEST] 菜单: 根 11 项 → 点子菜单后 可见=True 项数=15
[TEST] 返回根菜单: 可见=True 项数=11
[TEST] 跨屏: 切换=True ... → 2160,0 宠物=(0,360)
[TEST] 换回: True → 0,0
[TEST] 存档往返: 金钱 990.42->990.42, 经验 500.70->500.70
```

### 本轮经验

1. **多屏的正确姿势是"窗口跟屏走"而不是"一个大窗口盖全部"**：后者在混合缩放的
   WM 钳制下不可行；换屏坐标换算要用屏幕物理矩形做基准，别信异步的 `Position`。
2. **菜单点击回调要区分"展开子级"与"执行动作"**：前者收起菜单就是 bug。
3. **移植功能时"状态接线"和"表现接线"都要查**：State/Timer 接了但 Display
   没接，逻辑全对、画面不动，自检要覆盖"当前动画名"而不仅是状态字段。

---

## 十、v1.1.2：跨屏拖动真正修好（2026-11-04 第七轮）

用户反馈「还是拖不过来」。1.1.1 的换屏只在**指针拖出窗口**时才触发，这条路走不通：

> 窗口的 XShape 输入区域只覆盖宠物本体。宠物被 `SetPetPosition` 夹在屏幕边缘后
> 就不再移动，用户继续把鼠标往外拖，指针立刻离开宠物 —— 而这个窗口在指针不在其
> 输入区域内时收不到 MotionNotify，换屏判断根本没有机会执行。
> 所以表现为「宠物顶在边界，怎么拖都过不去」。

### 改法：把触发点从"指针出窗"提前到"宠物顶到边缘"

拖动的每一帧，`MoveWindows` 之后检查宠物是否已经贴边（距离 < 4px），且这一帧的
位移还在往外——两者同时成立就换屏：

```csharp
if (delta.X > 0.5 && AtEdge(c.GetWindowsDistanceRight()))  sx = 1;
if (delta.X < -0.5 && AtEdge(c.GetWindowsDistanceLeft()))  sx = -1;
// y 方向同理
mw.TrySwitchScreenByDirection(sx, sy, cursor);
```

这条路径不依赖"指针离开宠物之后"的任何事件，因此不再受输入区域裁剪影响。
「指针出窗」的判断保留作兜底。

### 另外三处细节

1. **换屏后宠物不再瞬移到新屏最左**
   以前按"保持宠物物理位置"摆放，而宠物此时正卡在旧屏右边缘（整只在旧屏上），
   换算到新屏必然越界被夹成 0 —— 视觉上就是"啪"地跳到新屏最左边。
   现在按**指针在宠物上的相对位置不变**换算：`grab = 指针 - 宠物左上角`，
   换屏后 `宠物左上 = 指针新坐标 - grab`。宠物会跟着指针自然滑进新屏。
   代价是拖动瞬间宠物可以部分在窗口外（被裁掉一部分），为此加了 `FreeClamp`：
   拖动期间 `SetPetPosition(..., clamp: false)`，松手立即复位并夹回屏内。
   注意：`LayoutUpdated` 钩子里的 `SetPetPosition(_petLeft, _petTop)` 也要看
   `FreeClamp`，否则窗口尺寸一变就把位置夹回去了（这个坑踩过一次）。
2. **换屏冷却 400ms**：换屏瞬间宠物正好贴在新屏边界，同一方向的微抖会立即换回，
   表现为两屏之间来回跳。冷却由 `SwitchTo` 统一记录。
3. **换屏后第一帧丢弃**：窗口位置/尺寸变更后 Avalonia 报告的局部坐标仍用旧基准，
   该帧 delta 是坐标跳变（约一屏宽），会把宠物"弹"回去。加 `_dragRebasePending`
   跳过一帧并重新取基准。

### 验证（程序化自检，含窗口真实位置回读）

```
[TEST] 跨屏: 切换=True 0,0 2160x1440 → 2160,0
[TEST] 跨屏后窗口实际: Position=(2160,0) Bounds=2048x1152
[TEST] 换回: True → 0,0
[TEST] 换回后窗口实际: Position=(0,0)
[TEST] 边缘换屏: 切换=True 屏=2160 窗口实际=(2160,0)
[TEST] 松手换屏: 屏=2160 窗口实际=(2160,0)
```

关键点：这次不只验证内部状态，**还把 X 窗口的真实 Position/Bounds 回读打出来**，
确认 WM 真的执行了搬移（`Position=(2160,0) Bounds=2048x1152`，副屏 1.25 缩放）。

### 诊断开关（新增）

| 变量 | 作用 |
|---|---|
| `VPET_DEBUG_DRAG=1` | 打印按下 / 每帧拖动 / 换屏 / 窗口位置回读 |
| `VPET_NO_INPUT_SHAPE=1` | 关闭 XShape 输入区域裁剪，整窗可点（用于判断事件是否还投递） |
| `VPET_KEEP_STDOUT=1` | 启动器不把输出重定向到日志文件（测试用） |

另外启动器现在会把输出写到 `~/.local/share/VPet/vpet.log`（从桌面图标启动时没有终端，
原来日志全丢）。超过 2MB 轮转为 `.log.1`。

### 环境限制记录

这台机器上以 **root** 身份启动的测试实例，窗口能创建、能 xwininfo 查到、能被
xdotool 移动，但既不渲染也不接收鼠标事件（`xdotool getmouselocation` 在该位置
返回 root 窗口）。判断是 root 进程接入用户 Wayland/XWayland 会话的权限/合成问题，
不是程序缺陷 —— 用户从桌面图标启动的实例是正常的。所以涉及**真实鼠标交互**的
验证只能由用户完成，程序侧一律用 `VPET_SELFTEST` 覆盖。

## 十一、v1.1.3：拖动跟手（2026-11-04 第八轮）

### 现象

跨屏能拖过去了，但拖动时宠物**严重落后于鼠标**：指针已经移开一段距离，宠物才跟上来；
连续拖动时间越长越滞后，像在"追"指针。

### 根因：拖动时每帧都在改 X11 输入区域

`SetPetPosition` 每帧都被调用一次，它内部会走 `UpdateClickThrough()` 重新下发
XShape 输入区域（宠物本体的矩形）。这个方法本来有"签名去重"——矩形没变就不提交，
但**拖动时宠物位置每帧都在变，签名每帧都不同**，等于每帧一次：

```
XShapeCombineRectangles(ShapeInput, ...) + XFlush()
```

XWayland 对 shape 变更的处理远比普通绘制贵：它要重算区域、更新
`wl_surface.set_input_region`、让合成器重建 hit-test 数据。请求速率远超合成器的
消化能力，X 请求就在 socket 里堆着；`XFlush` 写满缓冲后 UI 线程直接阻塞在写上，
后续鼠标事件继续排队 —— 于是拖得越久，滞后越大。

顺带解释了上一轮为什么"指针离开宠物就收不到事件"：输入区域只有宠物本体那么大，
指针一离开，X 服务器就不再往这个窗口投递 MotionNotify。

### 修复

**1) 拖动期间冻结输入区域（关键）**

```csharp
// 按下: 一次性放大到整窗, 之后整个拖动过程零 X 请求
internal void BeginDragShape()
{
    _dragShapeActive = true;
    _lastRegionKey = "";
    LinuxWindowNative.ResetInputRegion(_platformHandle);
}

// 每帧: 直接返回, 不再提交
private void UpdateClickThrough()
{
    ...
    if (_dragShapeActive) return;
    ...
}

// 松手: 按宠物最终位置恢复精确区域(一次)
internal void EndDragShape()
{
    _dragShapeActive = false;
    _lastRegionKey = "";   // 强制重新提交
    UpdateClickThrough();
}
```

放大到整窗还有额外好处：拖动过程中指针在整个窗口里移动都能收到 MotionNotify，
不再受"离开宠物就断流"的限制。

**2) 改绝对定位拖动**

原来是 `MoveWindows(delta)` 逐帧累加。累加式在宠物顶到屏幕边缘时，clamp 会把多出来的
位移吃掉；指针往回拖时，宠物要先等指针"补回"这段被吃掉的行程才动 —— 手感就是
"拖了一下才跟上"。

```csharp
// 按下时记下抓取偏移, 之后每帧直接用指针位置摆宠物
_grabOffset = 按下点 - 宠物左上角;

internal void DragTo(Avalonia.Point cursor, Avalonia.Vector grab)
{
    SetPetPosition(cursor.X - grab.X, cursor.Y - grab.Y);
}
```

越界时宠物稳定贴在边上，指针一回到屏内立刻跟随，不会"欠账"。
换屏后坐标基准变了，`_dragRebasePending` 的分支里会重算抓取偏移。

**3) 拖动坐标必须统一到窗口坐标系（否则跟随比例变一半）**

第一版 `DragTo` 用的是 `e.GetPosition(MainGrid)`。问题在于 **MainGrid 在宠物内部**——
`Main.axaml` 里是 `Viewbox → Grid(MainGrid, 500x500)`，宠物一动，这个坐标空间的原点
就跟着宠物一起动。于是：

```
cursor = P - L          // 指针相对宠物的位置(不是相对窗口)
grab   = cursor - L     // 又减了一次宠物位置
L_new  = cursor - grab = dn - (Ln-1 - L0)
```

`D_n = d_n - D_{n-1}` 是一个在 `d` 和 `0` 之间交替的振荡序列，平均值正好 **1/2**。
手感就是"鼠标走两步宠物走一步，拖得越远差越多"。

修法：整个拖动链路只用**窗口坐标**（`e.GetPosition(窗口)`），因为宠物位置
`Canvas.GetLeft(MainDisplay)` 本来就在窗口坐标里：

```csharp
_grabOffset = e.GetPosition(mw) - mw.PetTopLeft();   // 按下
mw.DragTo(e.GetPosition(mw), _grabOffset);           // 每帧
```

换屏判断、松手兜底（`FinishDragAt`）同样传窗口坐标，`SwitchTo` 里
`(c.X - _petLeft) * scale` 才是自洽的。

**4) 其余每帧开销**

- `LayoutPetUI` 只对**可见**的气泡/计时器/面板设坐标（对不可见控件设 Canvas 坐标
  也会让 Canvas 重新 arrange 全部子元素）；显示的那一刻由 `LayoutUpdated` 补位。
- 指针在窗口内时不再遍历显示器列表判断"要不要换屏"，只有真的越出窗口才走兜底路径。
- `PointerCaptureLost` 兜底：切窗口/弹窗/Alt+Tab 收不到 PointerReleased 时，
  自动复位 `FreeClamp` 并恢复输入区域 —— 否则残留的"整窗可点"会把桌面点击全吞掉。

### 验证

同一段模拟拖动（120 次位移）对比 X 请求次数与跟随比例：

```
[TEST] 拖动 X 请求: 120 次位移中 0 次 (应为 0), 起始整窗 1 次, 收尾恢复 1 次, 120 次位移耗时 0ms
[TEST] 拖动比例: 指针位移=300 宠物位移=300 (应为 300), 比例=1.00
```

修复前这一项会是 120 次（每帧一次）、比例 0.50。实机拖动也在日志里对上了：

```
[VPet] 拖动: 53 个移动事件 / 452ms 最大间隔 20ms 平均 8.5ms/事件 指针位移=(48,-151) 宠物位移=(48,-151)
```

（另一条 `指针位移=(-286,259) 宠物位移=(-286,114)` 的纵向差值来自宠物撞到屏幕底边被
夹住，属于正常 clamp。）

其余自检项（投喂/商店/工作/睡觉/存档往返/菜单/跨屏）全部保持通过，
见 `docs/verify-v1.1.3.log`。

### 新增诊断

每次拖动结束会输出一行汇总：

```
[VPet] 拖动: 86 个移动事件 / 1520ms 最大间隔 24ms 平均 17.7ms/事件
```

- `事件数 / 总时长` 偏小（比如 40Hz 以下）→ 问题在输入/合成层（合成器节流、XWayland）；
- `最大间隔` 很大（几百 ms）而事件来得并不稀疏 → UI 线程被别的东西占住了，
  对照当时是否有大型面板刷新、动画切换、自动保存。

### 本机编译注意（内存超额提交时的绕法）

系统 `Committed_AS` 超过 `CommitLimit`（本机实测 30.6G vs 26.9G）时，dotnet 默认 GC 会
直接 `GC heap initialization failed 0x8007000E`；单段 `DOTNET_GCHeapHardLimit` 又只有
384MB 能启动、再大就起不来，而 Roslyn 在 384MB 下编译 App 项目会 OOM。

绕法是把堆按段拆开限制（总和够用、但每段预留小）：

```bash
DOTNET_gcServer=0 DOTNET_EnableWriteXorExecute=0 \
DOTNET_GCHeapHardLimitSOH=0x18000000 \
DOTNET_GCHeapHardLimitLOH=0x08000000 \
DOTNET_GCHeapHardLimitPOH=0x04000000 \
$HOME/dotnet/dotnet build ... -c Release -m:1 --no-restore \
  -p:UseSharedCompilation=false
```

运行自检同理（SOH 256M + LOH 64M + POH 32M 可跑完整套）。

### 环境备注

本机以程序方式（`VPET_SELFTEST`）只能验证到"拖动期间不再发 X 请求"这一层；
真实鼠标手感仍需在桌面会话里验证。XWayland 下 `xdotool` 注入的事件不会投递到
本进程新建的窗口（测试实例的 X 视图只枚举到一块 2160x1440 / 一块 2560x1440 且坐标
均为 0,0），所以自动化只能覆盖到程序侧。

## 十二、v1.1.4：跨屏不再横跳（2026-11-04 第十轮）

### 现象

跨屏能拖过去，但拖动过程中窗口在两块屏之间连续横跳，宠物一会儿出现一会儿消失。
实机日志里 3 秒切了 10 次：

```
[VPet] 拖动换屏: → (2160,0) 宠物物理(1660,901)
[DBG] 换屏后回读: Position=(0,0) Bounds=2160x1440
[VPet] 拖动换屏: → (0,0)    宠物物理(2160,815)
[DBG] 换屏后回读: Position=(2160,0) Bounds=2048x1152
[VPet] 边缘换屏: 方向=(1,0) 距离 左-400 右2060 上721 下219 结果=True
```

两个特征：回读到的 `Position` 总是**比刚设置的值慢一拍**；边界距离出现 `-400` 这种越界值。

### 根因：换屏后的异步坐标系自激

1. **`TrySwitchScreen`（指针出窗路径）没有冷却**。`TrySwitchScreenByDirection` 有 400ms 冷却，
   这条没有——而换屏后最先被触发的恰恰是它。
2. **窗口搬家是异步的**。X11 的 Move/Resize 只是请求，要等 WM 回 `ConfigureNotify`，
   Avalonia 的 `Position`/`Bounds` 才更新。这几帧里 `e.GetPosition(窗口)` 仍是旧基准，
   差值约等于一整块屏的宽度。
3. **换屏落点允许负坐标**。按抓取偏移换算，宠物常被算到目标屏坐标系的负区间，
   而窗口只覆盖目标屏——宠物被裁成"闪没了"。

三者形成回路：换屏 → 坐标系没跟上 → 宠物摆到窗口外 → 边界距离异常 → 又判"顶边朝外拖"
→ 再换屏。一圈约 300ms，就是"鬼畜"的节奏。

### 修复

```csharp
// 1) 两条换屏路径共用冷却, 且要长于坐标系稳定时间
private const long SwitchCooldownMs = 600;

// 2) 换屏后等窗口真的到位, 而不是"丢一帧"
internal bool IsSwitchSettled()
{
    if (Environment.TickCount64 > _switchSettleDeadline) return true;  // 250ms 兜底
    if (Position.X != _switchTargetPos.X || Position.Y != _switchTargetPos.Y) return false;
    return Math.Abs(Bounds.Width - _switchTargetSize.Width) < 1.5
        && Math.Abs(Bounds.Height - _switchTargetSize.Height) < 1.5;
}

// 3) 落点夹进目标屏: 不允许负数, 否则宠物落在窗口外被裁
targetLeft = Math.Clamp(targetLeft, 0, Math.Max(0, target.Bounds.Width / ns - petW));

// 4) 指针真的越界才换屏
if (!mw.PointerOutside(cursor, 2)) return false;
```

等待期间 `PointerMoved` 只更新基准、不摆宠物、不判换屏——宠物原地不动，视觉上只是
极短的一次停顿（正常 WM 下 1~2 帧）。

### 为什么"宠物贴边就换屏"不行

指针在被拖的宠物身上，宠物贴到右边缘时指针还在本屏内。此时换屏，
`宠物物理 = 指针物理 - 抓取偏移` 会落在本屏，而窗口已经只管目标屏了——
宠物被裁掉。所以必须等指针也越过去，换屏后宠物才与新屏自洽。

### 验证

```
[TEST] 边缘换屏: 切换=True 屏=2160 窗口实际=(2160,0) 宠物=(0,360)
[TEST] 换屏冷却: 立即再换=False (应为 False) 落点=(0,360) 允许 0..1548 合法=True
[TEST] 拖动比例: 指针位移=300 宠物位移=300, 比例=1.00
```

自检第 8 项新增两个断言：换屏后立刻再请求必须被冷却挡住（`False`），
以及落点必须落在 `[0, 视口-宠物]` 区间内。完整日志见 `docs/verify-v1.1.4.log`。

## 十三、v1.1.5：换屏瞬间的"闪到最右"（2026-11-04 第十一轮）

### 现象

1.1.4 之后不再连续横跳，但跨屏那一下宠物会**跳到屏幕右缘再弹回来**。

### 根因：`SyncViewSize()` 把刚设好的视口尺寸改回了旧屏

换屏流程是 `ApplyScreen()`（设 Position/Width/Height 与 `_viewWidth`）紧跟
`SyncViewSize()`。后者用 `Bounds` 去"校正"视口尺寸——而 `Bounds` 是窗口实测值，
要等 WM 回 `ConfigureNotify` 才更新，此刻仍是旧屏的尺寸。于是：

```
ApplyScreen:  _viewWidth = 2048   (副屏逻辑宽)
SyncViewSize: _viewWidth = 2160   (Bounds 还是主屏的 2160)  ← 被改回去
```

宠物随即按 2160 摆放，左极限 1660；副屏只有 2048 宽，右边 112px 落在窗口外被裁掉，
视觉上就是"甩到最右边"。同时边界距离也按 2160 算成 `右=0`（误判贴边），
于是又触发一次换屏，宠物"闪回来"。

实机日志的直接证据：

```
[VPet] 拖动换屏: → (0,0) 宠物物理(2160,815)
[VPet] 边缘换屏: 方向=(-1,1) 距离 左1660 右-112 上815 下-163 结果=True
```

窗口已经在主屏，两个"越界"却是用副屏的 2048x1152 算出来的（`2048-1660-500=-112`）。

### 修复

```csharp
private void SyncViewSize()
{
    // 换屏事务未完成 → Bounds 还是旧屏尺寸，采信它会把 _viewWidth 改回去
    if (!IsSwitchSettled())
        return;
    ...
}

// 边界判定一律用设定的目标值，不读滞后的 Bounds
public double GetWindowsDistanceRight() => _viewWidth - Canvas.GetLeft(MainDisplay) - MainDisplay.Width;
public double GetWindowsDistanceDown()  => _viewHeight - Canvas.GetTop(MainDisplay)  - MainDisplay.Height;

internal bool PointerOutside(Point p, double margin)
    => p.X < -margin || p.Y < -margin || p.X > _viewWidth + margin || p.Y > _viewHeight + margin;
```

`IsSwitchSettled()` 也改成双条件（位置已离开旧值 **且** 尺寸已对上目标），
比"必须精确等于目标位置"更快收敛；兜底超时 250ms → 150ms。

### 经验：几何状态分两类

这个 bug 值得记下来，因为它会以各种形式复发：

- **意图值**（`_viewWidth` / `_viewHeight` / `_screen` / `_petLeft`）——本程序主动设定的，
  改完立刻可读，永远正确，坐标系换算只能用它；
- **实测值**（`Bounds` / `Position`）——来自 WM 的 `ConfigureNotify`，天然慢一拍，
  只适合用来判断"窗口到底动了没有"，不能参与坐标运算。

一旦把实测值混进几何计算，就会在换屏的那几帧里算出"凭空越界"的结果。

### 验证

```
[TEST] 跨屏: 切换=True 0,0 2160x1440 宠物=(560,450) → 2160,0 2048x1152 宠物=(0,360)
[TEST] 换屏后边界: 右=1548 期望=1548 一致=True 视口=2048x1152
```

自检新增 8c 项：换屏后 `GetWindowsDistanceRight()` 必须等于
`_viewWidth - 宠物宽 - 宠物左坐标`。修复前这一项差 112（正好是两屏逻辑宽之差）。

## 十四、v1.1.6：弹窗数值累加与投喂动画缩放（2026-11-05 第十二轮）

### 1. 摸头弹窗的数字会累加

`LabelDisplayShowChangeNumber(text, n1, n2)` 的合并策略是：新文案的哈希与上一条相同就
把数字加上去，否则重置。问题出在**弹窗消失时没有清零累积值**：

```csharp
// 修复前
if (--labeldisplaycount <= 0)
{
    labeldisplaytimer.Stop();
    Dispatcher.Invoke(() => LabelDisplay.IsVisible = false);
}
```

原版 WPF（`Main.xaml.cs`）在同一个位置有 `labeldisplaychangenum1 = 0;
labeldisplaychangenum2 = 0;`，移植时漏了。于是：

```
第一次摸头 → 弹窗"体力-2 心情+1"，面板 100→98 / 60→61
等弹窗消失   → 累积值仍是 2 / 1
第二次摸头 → 弹窗"体力-4 心情+2"，面板 98→96 / 61→62   ← 对不上
```

另外计时器的 `--labeldisplaycount` 原来在线程池线程执行，而新提示在 UI 线程执行，
两边会抢同一个计数（刚弹出的提示可能被上一条的收尾顺手清掉）。现在倒计时与清零都在
`Dispatcher.Invoke` 内完成。

顺带补了 `Main.ValueChanged`：摸头 / 摸身体 / 投喂改完数值立即通知 `ToolBar.RefreshValues()`，
数据面板不用再等 800ms 定时器，"面板慢半拍"也不会被误读成数值不一致。

### 2. 投喂动画把宠物缩成小点

`mod/0000_core/pet/vup/Eat/*/info.lps` 里 `eat` 是夹心动画：

```
PNGAnimation#eat_front_lay_2:   path#front_lay:      ← 前层：宠物吃东西的动作，1000x1000
FoodAnimation#eat:  a1#125,220,88,60,25,0.4375: ...   ← 中间层：食物图标，宽 60~90
                    front_lay#eat_front_lay_2: back_lay#eat_back_lay_2:
```

`a1#125,220,88,60,25,0.4375` 的含义是 `Time,MarginX,MarginY,Width,Rotate,Opacity` ——
那个 `60` 是**食物图标**的宽度。原版把整个夹心动画塞进固定的 500x500 容器（`FoodGrid`），
`Animation.Run` 只对中间层那个 `Image` 设 Width/Height。

移植版只有单个宠物图像元素，`Display(FoodAnimation)` 直接把这个 Width 贴到宠物本体上，
每帧 60~90px —— 宠物瞬间缩成小点，播完再弹回 500。

修复（`Main.DisplayFoodGraph`）：投喂时先 `FindGraph(food.GetGraph(), Single, mode)`，
如果是 `FoodAnimation` 就取它的 `Front_Lay` 单独播放。前层是完整 1000x1000 宠物帧，
尺寸与普通动画一致。自定义 mod 给的若是普通动画，仍走原路径。

已知取舍：目前看不到食物图标飞进嘴里（要复刻三层容器得给 `IUIElement` 抽象补上
父子/图层能力），已记在 `功能对比-未实现清单.md`。

### 验证

```
[TEST] 摸头弹窗: 第1次 实际 体力-2 心情+1 文案="体力-2 心情+1" |
       第2次 实际 体力-2 心情+1 文案="体力-2 心情+1"
[TEST] 投喂动画: 图=eat 前层=eat_front_lay_2 命中=True
       宠物尺寸 500x500 → 播放中 500x500 → 结束后 500
```

自检新增第 12、13 项，前者隔 2.6 秒连摸两次比对文案与实际增删，后者在投喂动画播放
中途采样 `PetElement.ImageControl.Width`。

## 十五、v1.1.7：投喂动画只剩一只手（2026-11-05 第十三轮）

v1.1.6 为绕开"宠物缩成小点"，把夹心动画改播**前层**，并假设前层是完整宠物帧。
实际效果是桌面上只剩一只手——图层选反了。

### 1. 用像素范围判定图层角色

画布都是 1000x1000，光看尺寸看不出来，得按非透明像素裁剪：

```bash
# eat/Happy 目录下
identify -format "%f %wx%h trim=%@\n" front_lay/*.png
identify -format "%f %wx%h trim=%@\n" back_lay/*.png
```

```
front_lay  _001_125.png 1000x1000 trim=259x147+359+447
front_lay  _003_125.png 1000x1000 trim=276x178+350+401
back_lay   _005_125.png 1000x1000 trim=342x939+321+33
back_lay   _016_125.png 1000x1000 trim=539x946+226+25
```

- `front_lay`：8 帧，内容只有 259x147，纵向起点 y≈447 —— 胸口/嘴部那一小块，是叠在
  食物**前面**的手
- `back_lay`：19 帧，内容 500x937 近乎满幅 —— 完整宠物本体（手本来就在里面）

`Drink/info.lps` 直接印证：Happy、PoorCondition 两档的 `drink_front_lay` 整行都是
`///` 注释掉的，只有后层在跑；共用的手部前层放在 `Drink/front_lay` 给 Nomal 档用。

### 2. 修复

`Main.DisplayFoodGraph()` 改为按 `Back_Lay` → `Front_Lay` 的顺序找可用图层，
命中哪个播哪个，全都没有才回待机：

```csharp
foreach (var lay in new[] { fa.Back_Lay, fa.Front_Lay })
{
    if (string.IsNullOrEmpty(lay)) continue;
    var g = Core.Graph.FindGraph(lay, layAnimat, mode);
    if (g == null || g is FoodAnimation) continue;
    Console.WriteLine($"[VPet] 食物动画: {foodName} 播放图层={lay}");
    Display(g, () => DisplayToNomal());
    return;
}
```

后层仍是 1000x1000 的正规帧序列，所以"不缩放"的收益保留；丢掉的依旧只是中间层
那颗食物图标。

### 3. 自检脚本的两处修补

**(a) 摸头读数的假异常。** 第 12 项先直接把 `Strength` 写成 80，但 `StoreStrength`
还留着 108。`FunctionSpend` 每 15s 调一次 `StoreTake()`（把 `StoreStrength` 的 1/10
返还给体力，与原版同一套算法），两次摸头之间正好赶上一次返还，读数就成了
"体力--9"（体力倒涨 9 点）。现在测试前把 `StoreStrength` / `StoreStrengthFood` /
`StoreStrengthDrink` 一并清零，只测摸头本身。

**(b) 低内存机器跑不完自检。** 本机 `Committed_AS` 长期高于 `CommitLimit`
（39.8G vs 26.9G），自检第 7~9 项要反复解码 2160x1440 / 2048x1152 位图，
必定 OOM 在拖动段末尾，后面的投喂/摸头项永远跑不到。新增
`VPET_SELFTEST_LIGHT=1`：跳过跨屏 / 边缘换屏 / 松手换屏三项，其余照跑。

### 验证

```
[VPet] 食物动画: 鸡祥如意 播放图层=eat_back_lay_2
[VPet] 食物动画: 戒指糖 播放图层=eat_back_lay_2
[TEST] 摸头弹窗: 第1次 实际 体力-2 心情+1 文案="体力-2 心情+1" |
       第2次 实际 体力-2 心情+1 文案="体力-2 心情+1"
[TEST] 投喂动画: 图=eat 后层=eat_back_lay_2 命中=True(必须是后层, 前层只是手部小块)
       宠物尺寸 500x500 → 播放中 500x500 → 结束后 500
[TEST] 自检结束
```

日志见 `docs/verify-v1.1.7.log`（轻量模式，跨屏三项跳过，沿用 v1.1.5/1.1.6 的记录）。

## 十六、v1.2.0：设置面板 / 随机说话 / 角色面板（2026-11-05 第十四轮）

原版这三块分别是 `winGameSetting`、聊天 MOD 的文本池、`winCharacterPanel`。
移植版之前：参数全硬编码、只会说一句"你好呀~"、只有一份简化数据面板。

### 1. 面板为什么不能是真窗口

桌宠是铺满全屏的透明窗，靠 XShape 把"宠物本体 + 展开的菜单/面板"挖成可点区域，
其余部分让点击穿透到桌面。所以面板不能开独立 Window——那样会挡住整个桌面。

做法与商店一致：往 `PetCanvas` 里塞 `Border`，再在 `UpdateClickThrough()` 里把它算进
输入区域。新增的面板统一从 `VisibleOverlays()` 暴露，`UpdateClickThrough` 里一段循环
就全带上了，不用每加一个面板改一次。

面板刚 `IsVisible=true` 时 `Bounds` 还是 0，输入区域会算成 0 高（面板看得见点不动），
所以 `CreateOverlayPanel` 里挂了 `LayoutUpdated` 重算，和菜单/商店的处理一致。

### 2. 控件全部手绘

项目没有引入 Avalonia 主题包，需要控件模板的东西（`MenuItem`、`CheckBox`）在无主题
环境下不渲染。所以开关做成 `Border + TextBlock`，点一下换文案和底色；数值用
`[−] 值 [+]` 的步进器。`Button` / `ProgressBar` / `ScrollViewer` / `TextBlock` 是可用的
（数据面板已经在用）。

### 3. 设置项与落盘

`GameSettings`（`~/.local/share/VPet/settings.txt`，纯 `key=value`）：

```
enablefunction=1
logicinterval=15
interactioncycle=30
autosaveminutes=5
sayenabled=1
sayperiod=4
autostart=0
```

读取时逐键容错：认不出的键忽略、越界值夹到范围内、文件坏了回默认值。
改动即时生效——`ApplySettings()` 里同时更新 `IController.EnableFunction`、
`InteractionCycle`、`EventTimer.Interval`，并重建自动保存定时器。

开机自启写 `~/.config/autostart/vpet.desktop`，`Exec` 优先指向安装脚本生成的
`vpet.sh`（它带好了 `DOTNET_ROOT`，直接指向 apphost 在部分环境下会找不到运行时）。

### 4. 说话池的三层选择

`SpeechPool.PickIdle(save)` 的判定顺序是有意设计的：

```
生病   → 说"头晕/有药吗"
饱腹<30 → 说"饿了"
口渴<30 → 说"好渴"
体力<25 → 说"累了"
深夜/清晨 → 应景一句
以上都不是 → 闲聊池
```

节流两层：先按 `SayPeriod` 数个周期，再 `Rnd(3)` 拦一道，避免说话像闹钟。
选句时最多重试 6 次避开上一句。

**说话优先于随机动作**：`EventTimer_Elapsed` 的待机分支里先说，说成了就 `return`，
不再走移动/待机那套 switch —— 说话会切到 `Say` 类动画，再叠一个会被打断。

台词可通过 `~/.local/share/VPet/speech.txt` 扩充（首次运行自动生成带注释的模板）。

### 5. 陪伴统计与存档分开

`CharacterStats` 存在 `stats.txt`，与 `save.lps` 分开：删档重开不会把"摸头 300 次、
陪你 12 小时"这些记录一起清掉。计数点是摸头/摸身体回调、`EatFood`、主循环周期。

### 验证

```
[TEST] 设置面板: 可见=True 控件块=12 写入=25 重读=25 一致=True 文件=True
[TEST] 说话池: 12 次抽取去重=8 句(应 >1), 当前句="要不要摸摸我的头？",
       低饱腹 20 次里提到饿=5 次(应 >0)
[TEST] 角色面板: 可见=True 区块=22 摸头计数 0->1 统计落盘=True 陪伴=0 秒
[TEST] 菜单: 根 13 项 → 点子菜单后 可见=True 项数=15
[TEST] 自检结束
```

注意：面板类自检（P1~P3）被安排在整轮最前面。它们要现建控件树，而本机
`Committed_AS` 长期高于 `CommitLimit`，排在末尾会在堆接近硬上限时 OOM。

## 十七、v1.2.1：升级安装把桌宠变成「僵尸」（2026-11-05 第十五轮）

### 现象

升级到 1.2.0 后，左边屏幕上留着一只不动的桌宠：拖不动、点不动、右键也没反应，
但窗口还在、画面还在（定格在最后一帧）。

### 定位

三个证据合起来就能定性：

```bash
# 1. 两次截图零差异 = 不是待机动画，是画面根本不刷新
import -window root a.png; sleep 3; import -window root b.png
compare -metric AE a.png b.png null:      # 0

# 2. 点击后日志不增长 = 进程没在处理事件
stat -c %s ~/.local/share/VPet/vpet.log   # 前后一致

# 3. 日志末尾正好卡在安装那一刻
tail -2 ~/.local/share/VPet/vpet.log
# vpet.sh：行 57: 24545 总线错误            （核心已转储）

coredumpctl list | tail -1
# ... 24545 ... present  .../.old-1.1.7/VPet-Avalonia.App     ← signal 7 = SIGBUS
```

### 根因

`install.sh` 用 `cp -a "$SRC"/. "$INSTALL_DIR"/` 覆盖安装。`cp` 覆盖同名文件不是「替换」，
而是**打开目标文件并 truncate 同一个 inode**，再往里写。而正在运行的桌宠早就把这些程序集
`mmap` 成只读映射了（`Avalonia.*.dll`、`VPet-Avalonia.Core.Migrated.dll`、`libSkiaSharp.so`…）。
文件长度被截断的瞬间，进程访问落在文件末尾之外的页 → 内核发 **SIGBUS**。

窗口对象还没销毁，X 就一直保留那块画面，于是成了「看得见、摸不着」的僵尸。
这也解释了为什么之前几轮升级没出事：那时要么先手动 `mv` 走了 `VPet-Avalonia.App`/`.dll`，
要么装完就重启了，没赶上实例还在 mmap 的时间窗。

### 修复

```bash
# 装之前先结束运行中的实例
running_pids() {
  pgrep -f -- "$INSTALL_DIR/VPet-Avalonia.App" 2>/dev/null || true   # 也命中 .dll 回退启动
  pgrep -f -- "$INSTALL_DIR/vpet.sh" 2>/dev/null || true
}
kill -TERM $pids          # 走正常退出流程，存档会保存
# 6 秒内没退再 kill -KILL

# 拷贝改成 rsync：默认「写临时文件 + rename 替换」，被换掉的是目录项，
# 旧 inode 仍留给正在运行的进程，不会 SIGBUS
rsync -a "$SRC"/ "$INSTALL_DIR"/
```

`pgrep -f "$INSTALL_DIR/VPet-Avalonia.App"` 用前缀而非全名，是为了同时覆盖
apphost 直启（`.../VPet-Avalonia.App`）和回退方式（`dotnet .../VPet-Avalonia.App.dll`）。

### 代价与验证

升级会主动关掉正在跑的桌宠（否则新版本不生效），装完需要重新双击图标。

```
[install] 检测到运行中的实例: 24545
[install] 先结束它们(正常退出会保存存档)
[install] 安装完成
（升级后 pgrep 结果为空，无僵尸窗口残留）
```

已清掉本次留下的僵尸：`xdotool windowkill <win>`（发 XKillClient，2 秒内窗口消失）。
顺带一提，`/var/lib/systemd/coredump` 里已积了 3.0G / 17 条 VPet 转储
（大部分是自检 OOM 的 signal 6），可用 `coredumpctl rm VPet-Avalonia.App` 回收。

## 十八、v1.2.2：统计成就 / 捏脸 / 物品栏（2026-11-06 第十六轮）

原版这三块的实现分别是 `Statistics.cs`（Steam 成就）、WPF 的长按事件（捏脸/摸身体）、
`winInventory.xaml.cs`（完整物品栏）。移植版这次做的是精简但可用的一版。

### 1. 捏脸：长按机制是移植版一直缺失的底座

原版 WPF 里 `TouchArea.IsPress` 区分「立即触发」与「长按触发」，捏脸/摸身体走后者。
移植版之前**只实现了立即触发那条路**——`IsPress` 区域永远不会被调用，捏脸实际上是死的。

这次在 `Main.axaml.cs` 补上长按：

- `PointerPressed` 命中 `IsPress` 区域时不再立即触发，改记 `_pendingLongPress` 并起
  `DispatcherTimer`（600ms）
- `PointerMoved` 期间位移超过 12px（设计坐标）视为拖动，取消长按
- `LongPressTick` 时指针仍按住（`isPress`）才 `DoAction()`
- 捏脸动画三段式：`A_Start → B_Loop`（按住期间循环，靠 `IsPressNow` 判断）`→ C_End`
  松手收尾回待机，与原版 `DisplayPinch` 同构

捏脸区从 `GraphConfig` 的 `pinch` 行解析（`px/py/sw/sh`），注册进 `TouchEvent` 头部。
数值效果等同摸头：体力 -2、心情 +1（需开启功能消耗且体力 ≥10、心情未满）。

### 2. 统计与成就：本地化替代 Steam

成就定义内置 12 条（摸头/捏脸/投喂/陪伴/干活/购买/等级/金钱/睡觉的梯度阈值），
解锁状态写 `achievements.txt`（与存档分离，删档不清）。检查入口两个：

- 计数点（购买/投喂/工作完成/摸头/捏脸）后调 `CheckAchievementsAndNotify()`
- 主循环每 20 个周期自查一次（陪伴时长类靠这个累积）

解锁时气泡播报 + `Console` 日志。等级/金钱峰值由 `CharacterStats.ObserveSave`
持续观察记录（`MaxLevel/MaxMoney`），避免「曾经有钱过」被后续花销抹掉。

### 3. 物品栏：背包可视化

`MainWindow.InventoryPanel.cs`：分类过滤（正餐/饮料/零食/药品/礼品）、使用（走
`FeedFood` 同一条路，有吃东西动画）、丢弃、总价值统计。数据层复用现成的
`Inventory`（`inventory.txt`），没有另起炉灶。

### 面板管理

两个新面板照旧塞 `PetCanvas` 里的 `Border`，并加进 `VisibleOverlays()` 与
`ShowOverlay` 的互斥数组——不进这两个名单的面板要么点不到（XShape 输入区域没
收纳它），要么会和别的面板叠在一起互相挡。

### 验证

`docs/verify-v1.2.2.log`（轻量自检，面板项前置）：

```
[TEST] 物品栏: 可见=True 区块=5 过滤正餐后=3 背包件数=4
[TEST] 捏脸: 长按区=2个(应>0) 动画=pinch/A_Start(应pinch/A_Start) 计数 0->1
[TEST] 成就: 解锁=1条(应1, 顺毛大师) 落盘=True 面板可见=True 面板区块=38 总数=12
[VPet] 成就解锁: 小有积蓄
```

（捏脸自检里体力没变是正常的：测试时心情已满 100，数值分支要求
`Feeling < FeelingMax` 才生效。）

## 十九、v1.2.3：拖动跟手与位图缓存（2026-11-16 第十七轮）

这一版的主体改动来自外部优化（下称 dsh），方向是对的，但踩了 X11 的几何竞态。
评审补了四处，其中「点一下宠物就跳左上角」是必现 P0。

### 1. 拖动为什么原来黏手

移植版是「全屏透明窗 + 窗内挪宠物」的架构（见第三节）。拖动时每帧要做两件事：

- 把整个 `PetCanvas` 重绘一遍（2160×1440）
- 重算输入区域并下发 `XShapeCombineRectangles`

ARM 上这两步加起来就吃掉了帧预算，指针跑了、宠物还在原地。dsh 改成：

1. `PointerPressed` 后等位移超过 4px，调 `XResizeWindow` 把窗口缩到宠物大小 +4px 内边距
2. 拖动期间每帧只 `XMoveWindow`（窗口在屏幕上的位置），窗口内容一动不动
3. 松手时 `XResizeWindow` 还原成落点所在屏的尺寸，`XMoveWindow` 摆到落点，
   再 `SetPetPosition` 把宠物放到指针处

因为窗口内容不变，宠物自然跟着窗口走，帧预算只剩一次 `XMoveWindow`。

### 2. 几何事务：为什么必须挡 Bounds

`Window.Width/Height` 和 `Native` 的 `XResizeWindow` 都是**异步生效**的——调用返回时
`Bounds` 还是旧值。而 `MainWindow` 的 `LayoutUpdated` 里挂着 `SyncViewSize`，它拿
`Bounds` 覆盖 `_viewWidth/_viewHeight`，`SetPetPosition` 再用这两个值 clamp 宠物坐标。

于是还原时出现这个顺序：

```
XResizeWindow(整屏)   → 返回
SetPetPosition(落点)  → clamp 用的是 _viewWidth=508（旧的）
LayoutUpdated         → Bounds 终于变成 2160x1440，但宠物已经被夹到 (8,8)
```

现场表现：点一下宠物（哪怕不拖），它直接跳到屏幕左上角，之后再也不回来。

修法沿用换屏那条路已经有的思路——`ExpectWindowGeometry()` 在缩窗/还原前登记一个
几何事务（目标位置 + 目标尺寸 + 超时），`IsSwitchSettled()` 在事务落定前返回 false，
`SyncViewSize` 与 clamp 都让路。超时兜底避免 WM 不回 `ConfigureNotify` 时把拖动卡死。

### 3. 抓取偏移要用按下时的位置

第一版修复只在「移动超过阈值」时缩窗，但抓取偏移是用**当前**指针减窗口原点算的——
那时指针已经跑出去几十像素了，缩完窗宠物会往左上跳一下。改为按下时就记下指针相对
宠物的偏移，缩窗时用这个偏移反算窗口原点：

```
origin = pointer - grabOffset
```

### 4. 跨 DPI 落点

落点逻辑坐标 = (宠物屏幕物理坐标 - 屏幕物理原点) / **落点所在屏的** Scaling。
原来一律除以 `RenderScaling`（当前窗口的缩放），从 1.0 主屏拖到 1.25 副屏时，
越靠右偏得越多（最右差 640px）。

### 5. 位图缓存

原来缓存上限 1200 张、满了整体 `Clear()`：峰值 1GB+，清空瞬间还要 GC 一遍。
改成 LRU 96 张，淘汰即 `Dispose`。`PutCache` 遇到并发重复解码（同一个路径被两个
线程解了两次）时，返回该显示的那张、释放多余的那张——否则每次漏约 1MB 原生内存。

### 6. 落点屏判定：不能用「中心命中」

`EndSmallWindowDrag` 原来按「宠物中心落在哪块屏」选目标屏。而 `MoveWindowPhysical`
的 clamp 只要求窗口留 60px 在屏内——也就是说窗口**允许大半出屏**，拖到屏幕顶部时
宠物的中心必然跑到屏外，中心判定全部落空，`target` 保持原屏，宠物就被硬拽回去了。

现场表现：从主屏往副屏顶部拖，松手后宠物跑回主屏右下角。

改成取「宠物矩形与各屏重叠面积最大」的那块。重叠为 0 的屏直接跳过，
一块都没命中才回落到出发屏。

### 7. 长按与拖动阈值的冲突（v1.2.2 的捏脸其实是坏的）

拖动判定阈值 4px，长按判定要按住 600ms。这两者在同一个指针上抢事件：

```
按下（命中 pinch 区） → 起 600ms 定时器
手指漂移 5px（必然发生） → PointerMoved 判成拖动 → CancelLongPress() → 缩窗拖动
```

结论是**真机上捏脸永远触发不了**。v1.2.2 的自检「捏脸计数 0→1」是程序里直接调
`DoAction()` 拿到的，绕过了手势，所以没暴露。

现在：

- 长按候选区（`_pendingLongPress != null`）的拖动阈值放宽到 `LongPressMoveTolerance`（20px）
- 长按兑现后立刻把 `_smallDragCandidate` 置 false，这一次按下不再转成拖动

### 已知遗留

- 淘汰时 `Dispose` 理论上可能打到正在显示的位图。当前只有一个 `PetImageElement`
  在用缓存，不会命中；将来 `PetGrid2` 共用缓存就要改成引用计数。
- 拖动期间气泡/面板会被裁到 508×508 的小窗口里，贴到宠物脸上。
- 缓存按张数（96）而非字节限制，不同尺寸立绘的内存占用差别较大。

### 验证

`docs/verify-v1.2.3.log`（轻量自检 27 项全过）+ 实机逐项确认：

| 用例 | 操作 | 结果 |
| --- | --- | --- |
| 单击不缩窗 | 按下→松开，无位移 | 窗口几何 0 次变化（2160x1440 恒定），`小窗口` 日志 0 次 |
| 长按不缩窗 | 按住 850ms | 窗口几何 0 次变化，长按不再被拖动掐掉 |
| 同屏拖动 | 按住横拖 | 窗口跟随，宠物落在松手处 |
| 跨屏拖动 | 1.0 主屏 → 1.25 副屏 | `宠物屏幕=(3075,435) 屏=(2160,0 缩放=1.25) 逻辑=(732,348)`，反算物理 3075 ✓ |

> 注：实机拖动用例是自动化脚本 + 真实鼠标同时操作同一台机器，指针多次被手动移走
> （日志里能看到指针坐标自行折返）。落点换算已按日志核对精确，屏幕选择的边界场景
> （宠物中心出屏）靠代码推演 + 重叠面积算法保证，未取得干净的自动化复现。

## 二十、v1.2.4：缩窗拖动的代价（2026-11-16 第十八轮）

v1.2.3 给拖动上了「按下缩窗、整窗跟指针」的优化。用户升级后反馈「拖动会往左上角闪一下」，
录屏逐帧看之后确认：这条优化在 ARM 上不划算，默认关掉。

### 1. 怎么把「闪一下」量出来

这种问题靠截图抓不到（一帧 16~50ms），要用录屏 + 逐帧分析：

```bash
# 录 4 秒，20fps；同时用 xdotool 做拖动
ffmpeg -f x11grab -video_size 2160x1440 -framerate 20 -i :0 -t 4 -y rec.mp4
ffmpeg -i rec.mp4 -vf fps=20 frames/f%03d.png -y

# 逐帧看「宠物矩形」在哪、还在不在（床是纯白，夜空是深蓝，80% 阈值刚好分开）
for f in frames/*.png; do
  convert "$f" -crop 2160x1440+0+0 +repage -colorspace Gray \
          -threshold 80% -format "%@" info:
done
```

宠物躺床上的 bbox 约 448×149。正常帧稳定在这个尺寸，异常帧会暴露出来：

- `46x45+879+842` / `778x519+819+842` —— 只剩局部，说明表面正在重建
- `0x0+2160+1440` —— 宠物完全消失

同时统计左上角 700×700 区域的平均亮度（`-format "%[fx:mean]"`），宠物闪到那里会让
均值突增。v1.2.3 修复前那一版能测到突增，修复后 68 帧恒定在 0.22998。

### 2. 两段式几何：左上角那一帧

`Width/Height` 和 `Position` 是两个独立 X 请求。Avalonia 的 Resize 还要等
`ConfigureNotify` 回来，所以中间一定有一帧「窗口已经缩成 508×508、原点还在 (0,0)」。
宠物画布此时在窗口 (4,4)，屏幕上就是左上角。

改成一次 `XMoveResizeWindow`（`LinuxWindowNative.MoveResizeWindow`）原子落地，
再同步 Avalonia 的 `Width/Height/Position`（值已一致，setter 变成等值操作）。
这一条确实修掉了——录屏 68 帧左上角亮度完全不变。

### 3. 表面重建才是真正的墙

原子请求只解决「位置」，解决不了「内容」。X11 的窗口 resize 不会帮你缩放旧内容，
只会按新尺寸裁一刀；要等客户端重绘。而 2160×1440 的透明窗口表面在 ARM 上重建要
200~750ms。实测：

| 阶段 | 异常帧数 | 时长 | 表现 |
| --- | --- | --- | --- |
| 缩窗 | 7 帧 | 350ms | 宠物只剩一个脑袋 |
| 还原 | 15 帧 | 750ms | 宠物完全消失 |

这说明「缩小窗口」这个方向本身在这台机器上不可行。哪怕把原子请求、几何事务、
重绘催促都做对，那 200~750ms 的空窗依然存在。

### 4. 回到全屏窗口，为什么这次不黏手

因为「黏手」的真正原因在更早的版本就修掉了：

```csharp
// PointerPressed
mw0.BeginDragShape();   // 输入区域临时放大到整窗，拖动期间不再逐帧下发 XShape
```

逐帧 `XShapeCombineRectangles` 是同步 X 往返，那才是拖动滞后的元凶。全屏窗口拖动
只需要移动画布（一次 `Canvas.SetLeft`），实测日志：

```
[DBG] 拖动: 10 个移动事件 / 1516ms  指针位移=(300,0) 宠物位移=(300,0)
```

指针走多少，宠物就走多少，1:1。

### 5. 结论

- 默认：全屏窗口 + `BeginDragShape`（无缩窗，无表面重建，无闪）
- `VPET_SMALLWIN=1`：缩窗拖动作为可选（原子几何，不再闪左上角，但仍有 200~750ms 空窗）

保留缩窗实现而不是删掉，是因为换屏路径（`ApplyScreen`）也在用 `MoveResizeWindow`，
而且万一以后换了性能更好的渲染后端，这条路径可以重新评估。

## 二十一、v1.2.5：APNG 动画（2026-11-17 第十九轮）

### 1. 之前为什么"不支持"

`APNGAnimation` 一直是个占位类：`startup()` 里把 APNG 当成一张静态图登记，
`_framePaths` 只有一条、时长固定 100ms。所以遇到 APNG 的动作，桌宠会定格在第一帧，
看起来像卡住。原版 WPF 是用 SkiaSharp 自己解析 APNG 的，移植时没跟过来。

### 2. 解码链路

```
文件 → 拆块(IHDR/acTL/fcTL/fdAT) → 逐帧合成到画布 → 每帧写一个 PNG
     → APNGAnimation 按帧时长 SetContent(path) → PetImageElement 解码显示
```

关键点：

- **APNG 的帧数据是"补丁"不是"整图"**。`fcTL` 给出这一帧的矩形
  （`width/height/x_offset/y_offset`）和处置/混合方式，`fdAT` 里是这块矩形自己的
  PNG 图像数据。要解码得先拼一个"只有这块矩形那么大"的 PNG（IHDR 换成帧尺寸、
  带上 PLTE/tRNS 等附属块、数据当 IDAT），再贴回画布。
- **处置方式（dispose_op）决定下一帧的底子**：`NONE` 留着、`BACKGROUND` 把这矩
  形清成透明、`PREVIOUS` 恢复到这一帧画上去之前（所以要在绘制前对该矩形做快照）。
- **混合方式（blend_op）**：`SOURCE` 直接替换（含透明像素，用 `SKBlendMode.Src`），
  `OVER` 正常 alpha 叠加（`SrcOver`）。
- 首帧之前的 `IDAT` 是"默认图"，不属于动画序列，不能当第一帧。

### 3. 为什么写到磁盘而不是直接传位图

`IUIElement.SetContent` 的抽象里，位图对象是 Avalonia 类型（属于 App 层），
Core 层只认路径。写文件的好处是白拿两件事：显示层的 LRU 位图缓存、后台线程解码。
代价是每帧一次 PNG 编码（只在第一次合成时付出）。

缓存目录按 `路径 + mtime + 帧数 + 目标宽度` 哈希，落在 `~/.local/share/VPet/cache/apng_*`，
30 天没访问自动清。500×500×24 帧实测：

| 场景 | 耗时 |
| --- | --- |
| 首次（合成 24 帧 + 编码） | 599ms |
| 再次启动（读现成帧图） | 37ms |

因为解码在 `Task.Run` 里跑，首次播放会等它就绪（`EnsureReadyAsync`），不会卡 UI 线程。

### 4. 识别与回退

- `.png` 里带 `acTL` 块 → 按 APNG 处理。MOD 在 `info.lps` 里写 `picture` 也行，
  `Picture.LoadGraph` 会先探一下块头再转派给 APNG。`.apng` 扩展名同样接受。
- 写到 `apnganimation` 却是普通 PNG → 退回静态 `Picture`（以前是什么都不显示）。
- 真的解码失败 → 把首帧当静态图播 1 秒并打日志，至少不会让那个动作变成一片空白。

### 5. 验证方法（可复现）

```bash
# 交叉验证：ffmpeg 自己解一遍，和我们合成的帧逐像素比
ffmpeg -vsync 0 -i test.apng ref/ref_%03d.png
compare -metric AE ours/out_000.png ref/ref_001.png null:      # 0

# 命令行单测（不开窗口）
VPET_APNG_TEST=/path/test.apng VPET_APNG_OUT=/tmp/out dotnet VPet-Avalonia.App.dll
VPET_APNG_TEST=SYNTH dotnet VPet-Avalonia.App.dll              # 内置合成用例
```

`docs/apng-test/apng_dispose.apng` 是手工构造的 4 帧用例，专门覆盖
`BACKGROUND`/`PREVIOUS`/`OVER` 三种容易做错的组合；`test_ffmpeg.apng` 是 ffmpeg
编码的常规动图。两者与 ffmpeg 解码结果比对：前者 `AE=0`，后者 `AE≤32`（120×90×6 帧，
差异全在圆形边缘，RMSE 5.36e-05）。

实机演示：`VPET_APNG_DEMO=<文件>` 启动，会把该动图循环播在桌宠身上（截两帧能看到
橙色圆点在移动），仅用于人工确认。

### 6. 仍未覆盖

- APNG 的 `num_plays`（播放次数）只记录不生效：循环与否仍由 `info.lps` 的 `loop` 决定，
  与 PNGAnimation 保持一致，避免不写 `loop` 的 APNG 把状态机卡在循环里。
- 帧数超过位图缓存上限（96 张）时，每轮循环会重新解码一遍磁盘上的帧图；
  50 帧以内的 MOD 动图不受影响。

## 二十二、v1.3.0：把四个简版功能补齐（2026-11-18 第二十轮）

### 0. 起因

差距清单里从 v1.1.0 / v1.2.0 起就一直挂着四个「简版完成」：商店、物品栏、
角色面板、设置面板。功能都能用，缺的都是原版里天天会点的操作——商店没有分类和
数量选择、物品栏没有排序和收藏、角色面板没有报告页、设置面板没有数据管理。
这一轮把这四块补上，顺带踩到一个和「控件数量」有关的坑。

### 1. 商店：分类 / 搜索 / 排序 / 批量购买

原实现（v1.1.0）是把 122 种食物铺成一列 `Border + TextBlock`，点整行买一个。
现在每行要塞下名称、类型+效果摘要、单价、已持有数量、三个购买按钮，
于是面板被拆成三段：

```
┌ 标题 ────────────────── 固定（header）
│ 搜索框
│ [全部][正餐 37][饮料 23][零食 23][药品 10][礼品 21]
│ 排序 [默认][价格][名称][性价比]  [全部商品 / 只看买得起]
├ 列表 ────────────────── 滚动（body）
│ 炸鸡腿  正餐·+40经验+40饱腹…   $18.5   [×1][×5][×10]
│ …
├ 翻页 ────────────────── 固定（footer）
│        [上一页] 1/11 [下一页]
└ 关闭
```

**为什么搜索框必须放在滚动区外面**：塞进 `ScrollViewer` 里，往下翻两屏就找不到了，
而搜索恰恰是「找不到东西」时才用的。为此给面板容器加了 `header`（固定头部）和
`footer`（固定底部）两个 `out` 参数，`ScrollViewer.MaxHeight` 里减掉它们的高度，
否则固定区 + 滚动区一起把「关闭」按钮顶出面板（第一版就是这样，翻页条整个看不见）。

**搜索为什么支持数字**：桌宠跑在 X11 上，中文输入依赖用户自己的输入法（fcitx/ibus）。
我们没有为此引入任何输入法依赖，但不能保证每台机器都能在桌宠窗口里输入中文——
所以「输入纯数字 = 价格上限」作为退路（`SelectFoods` 里 `double.TryParse` 优先），
IME 用不了也能快速筛出便宜货。

**排序里的「性价比」**：`(饱腹 + 口渴 + 0.5×心情 + 体力) / 价格`。
122 种食物里价格跨度很大，按价格排只能找到贵的/便宜的，按性价比排才找得到「划算」的。

**批量购买的降级**：点 ×10 但钱只够买 3 份时，买 3 份而不是直接拒绝，
气泡里写清「钱只够买 3 份，花了 $X，余额 $Y」。余额为 0 时一件也买不到
（老实现只看「点得动」，钱不够和钱够长得一样）。

### 2. 分页：122 行控件把进程推到 OOM

第一版商店是全量渲染：122 行 × 约 9 个控件 = 1100+ 个 `TextBlock`/`Button`。
在内存紧张的机器上（本机 `Committed_AS` 常年在 `CommitLimit` 之上），
**打开商店直接 Aborted**，日志末尾只有一行 `Out of memory.`：

```
[VPet] 准备打开面板: shop
[VPet] 已打开面板: shop
Out of memory.        ← 面板建完了，但进程随即被杀
```

排查过程值得记一笔：因为 `catch (Exception)` 把 `OutOfMemoryException` 也接住了，
失败时连「自检失败」这行都打不出来（拼接异常消息又要分配内存），
表现成「自检里这一块完全没有任何输出，后面的步骤却照常跑」。最后是靠
「P8 一行日志都没有 + 末尾 Out of memory」两条对上才定位到。

修法两条：

- **每页 12 条**：`ShopPageSize = 12`，峰值控件量降到 1/10（实测列表控件 13 个：
  1 个头部 + 12 行）。翻页条固定在 footer，页码同时显示在列表头部（`第 1/11 页`）。
- 面板高度从 560 提到 600，一页 12 行刚好不用滚（`MaxHeight = 600 - 100 - 84 - 46`）。

买完**不重建列表**：`BuyFood` 之后只跑一遍 `_shopRowRefresh`（每行一个闭包，
更新「已有 N」「买得起」）。重建整张列表会把滚动位置和当前页一起打回原位，
买第 5 页的东西时会跳得莫名其妙。

### 3. 物品栏：搜索 / 排序 / 收藏 / 一次用多份

- **收藏**落 `favorites.txt`（每行一个食物名），不动 `inventory.txt` 的
  「名称\t数量」两列格式。收藏项永远排在最前（排序之后再 `OrderByDescending(收藏)`），
  物品用光后 `Favorites.Prune` 自动清掉失效条目。
- **排序**：数量 / 单价 / 名称 / 类型，都是内存排序，背包规模（几十种）不需要索引。
- **一次用多份**：`用` 吃 1 份；`全` 最多连用 10 份。关键是**吃不掉的要退回背包**：
  先从背包取出最多 N 份，`EatFood(food, count)` 内部一边吃一边判断「饱腹/口渴/健康
  是否已满」，满了提前停，返回实际吃掉的份数（`LastEatUsed`），调用方把差额 `Add` 回去。
  这样「点全用」不会因为数值溢出而白扔 9 份。

**`EatFood(food, count)` 为什么只播一次动画**：连着调 N 次 `Display()` 会让动画回调链
互相打断，最后停在某个中间帧（宠物保持着吃东西的姿势不动）。所以数值按份数叠加、
动画只播一次、气泡写「吃掉了 N 份」。

### 4. 角色面板：陪伴年报

顶部加「相伴第 N 天」（`FirstSeen` 到今天）。新增「陪伴年报」区块：互动总数
（摸头+捏脸+投喂+购买）、睡得最久、完成工作次数、**最常吃的东西**、最高等级、
最有钱时、成就进度。

「最常吃」没有给 122 种食物各记一个计数器（那要给 `stats.txt` 加 122 行，
还随 MOD 增删变化），用的是单计数器求众数的 Misra-Gries：

```
和当前 Top 同名 → 计数 +1
不同名         → 计数 -1
计数减到 0     → 换人
```

数据倾斜时（人总是反复吃那几样）结果就是真众数，代价是两行文件。
食物名可能带 `=` 或换行，写盘时做了最小转义（`\=` / `\n`）。

### 5. 设置面板：行为与数据管理

- **气泡停留** 1~15 秒，直接改 `MessageBar.AutoHideDelay`
- **自己走动**开关：`MainLogic` 的随机动作里 `case 0/1/2` 本来固定 `DisplayMove()`，
  现在按开关改成 `DisplayIdel()`
- **立即备份存档**：`save.lps / inventory.txt / stats.txt / achievements.txt /
  settings.txt / favorites.txt` → `backup/<yyyyMMdd-HHmmss>/`，只保留最近 8 份
- **打开数据目录**：`xdg-open`
- **清除陪伴统计**：二次点击确认（桌宠面板没有系统对话框，一问一答要另开窗口，
  二次点击最省事），只清 `stats.txt` 的计数与时长，保留「初次见面」那天
- **关于**：新增 `AppInfo.Version`，面板里显示版本 / 食物数 / 背包件数

### 6. 验证

自检新增 P8~P10（`SelfTestPanelsAsync`）：

```
[TEST] 商店: 全部=122项 正餐=37(应37) 全是正餐=True 价格升序=True 名称升序=True 搜索"炸鸡腿"=1条
[TEST] 商店分页: 页大小=12 列表控件=13(应≤14) 全量=122 底部翻页条=True 面板高=591
[TEST] 商店批量: 买5份 钱 1000.0->987.5(应扣12.5) 件 2->7(应+5); 余额0再买5 件 7->7(应不变)
[TEST] 物品栏: 排序单价降序=True 收藏置顶=True 文件=True
[TEST] 物品栏工具栏: 搜索框=True 分类按钮=6 排序按钮=6
[TEST] 连续使用: 鸡祥如意 库存 3->2 饱腹 50->100(上限 100)
[TEST] 设置新增项: 气泡 9 秒重读=9 生效=True 走动=关重读=True 备份=True(4 个文件) 清统计=True
[TEST] 年报数据: 众数跟踪=True(苹果派×3) 名字转义=True 版本=1.3.0
```

两个新调试后门：

| 环境变量 | 作用 |
|---|---|
| `VPET_SELFTEST_ONLY=panels` | 只跑商店/物品栏/设置这三块自检（全量在本机内存紧张时容易半途 OOM） |
| `VPET_SHOW_PANEL=shop\|items\|char\|settings\|ach` | 启动 1.5 秒后自动打开指定面板，配合 `import -window root` 截屏核对布局 |

实机截图（`docs/shot-shop*.png`）确认了 `TextBox` 在 FluentTheme 下能正常渲染
（有边框、Watermark、光标），分类角标、排序按钮、行内效果/价格/购买按钮的排版都正常。

**关于测试环境**：本机 `Committed_AS` 长期高于 `CommitLimit`、swap 已满，
频繁起 GUI 进程容易撞上内核 OOM killer（表现为进程 `Aborted`、日志停在启动阶段）。
这不是程序缺陷——发布版常驻运行时的内存占用与 v1.2.5 并无差别（面板是按需创建的）。

### 7. 这一轮仍未覆盖

- **音量/音效**：整个移植版没有音频子系统（原版有 `SoundPlayer` + wav 资源），
  做音量滑块没有意义。若后续接音频，需要先引入播放库 + 资源扫描。
- **主题/配色**：所有面板用硬编码深色（`#F22B2B2B`），没有主题系统。
- **存档管理器的多档切换**（#9）：只做了「备份当前存档」，没有多存档列表与切换。
- **商店按需加载**：分页解决了控件数量，但没有做真正的虚拟化列表；
  若将来 MOD 提供上千种食物，需要换成 `VirtualizingStackPanel`。

## 二十三、v1.3.1：工作面板（2026-11-18 第二十一轮）

### 0. 先分清哪部分是"简版"

「工作」在差距清单里一直标着 ✅ 已完成，但用户体感是简版，值得把账算清：

| 部分 | 状态 |
|---|---|
| 数值结算（效率、体力/饱腹/口渴/心情消耗、健康、金钱/经验产出） | ✅ 逐行照搬 `MainLogic.FunctionSpend` |
| 进度条面板（`WorkTimer`，300×200，停止/折叠按钮） | ✅ 有，样式照搬 xaml |
| 工作动画（A_Start → B_Loop → C_End） | ✅ 有，自检验证 `workone/A_Start/Happy` |
| **选择入口** | ❌ 只是右键菜单里一串纯文本 |

所以这一轮改的不是数值，是**"选之前能不能看清代价"**。

### 1. 旧入口的问题

```
右键 → 工作 / 学习 →
    [工作] 文案 (60分, 约8/分)
    [工作] 烧烤 (30分, 约12/分)
    [工作] 清屏 (90分, 约16/分)     ← 其实要 Lv.10
    [学习] 学习 (45分, 约80/分)
```

- 看不到消耗：烧烤要喝水 5/分、清屏要食物 5 + 水 5 + 心情 2.5，列表里一个字没有
- 看不到等级门槛，点下去才弹「等级不足, 需要 Lv.10」
- 十几个条目按 lps 顺序排，常用的没有置顶
- 干活期间想看看还剩几分钟、已经赚了多少，只能盯着进度条那个超大字号

### 2. 新面板

```
┌ 工作 / 学习 ────────────────────────────┐
│ [工作 5] [学习 4] [玩耍 4]   [全部工作]  ★ 固定头
│ 正在进行: 清屏 0.1/90 分 (剩 89.9 分) 已得 0 $
├─────────────────────────────────────────┤
│ ☆ 文案            60 分 · 约 10.4 $/分          [开始]
│   消耗 食物3.5/水2.5/心情1
│ ☆ 烧烤            30 分 · 约 15.6 $/分  需要 Lv.5  [Lv.5]   ← 压暗
│ ☆ 清屏  进行中    90 分 · 约 20.8 $/分                  [停止]  ← 绿色高亮
├─────────────────────────────────────────┤
│ 体力 100  饱腹 100  口渴 100  心情 60    ★ 固定底
└ 关闭
```

- **分类**：`Work.WorkType` 三类，带数量角标；数量为 0 的类不出现
- **排序**：收藏置顶 → 等级门槛升序 → 时长升序（新手先看到能做的）
- **收藏**：`workfavorites.txt`，和食物的 `favorites.txt` 分开放。理由很实际——
  食物和工作可能同名（都有「学习」这种名字），混一个文件里会互相污染
- **等级不足**：整行 `Opacity=0.45`，按钮位置直接写 `Lv.N` 并 `IsEnabled=false`，
  不再是"点下去才告诉你"
- **进行中**：`running` 要**先算**，否则刚开完的项会因为等级判定被显示成锁定态
  （诊断口为了绕过门槛临时改过 `LevelLimit`，这个坑就是这么暴露的）
- 底部数值低于 25% 转橙色 + 「状态偏低, 干活效率会打折」

### 3. 进度条配色：`Work` 类漏了六个字段

原版 `vup.lps` 的每条 work 都带自己的配色：

```
work:|Name#烧烤:|...:|Background#8D93A1:|ButtonBackground#4C505B:|Foreground#F4F4F6:|
work:|Name#学画画:|...:|Background#b4a647:|ButtonBackground#827717:|Foreground#f0f4c3:|
```

移植时 `Work` 类只保留了业务字段，`BorderBrush/Background/ButtonBackground/
ButtonForeground/Foreground` 全丢了——而 `LPSConvert` **只映射带 `[Line]` 特性的属性**，
没这个属性就等于 MOD 里写了也读不到，进度条永远是同一套天蓝。

补上这五个字段（外加 `Left/Top/Width`）后，`WorkTimer.ApplyStyle(work)` 在 `Start()` 里应用。
颜色解析要容错：lps 里写的是 `0290D5` 这种不带 `#` 的 RRGGBB，也可能是空串或写错，
统一走 `ToBrush` 兜底，颜色错了退回天蓝而不是抛异常。

实测：开始「清屏」后 `进度条底色=#ff413d39`，与 `Background#413d39` 对得上。

### 4. 顺手修的两个 bug

**完成提示的数字是错的。** 原代码写：

```csharp
gain = info.work?.Type == Work.WorkType.Work
    ? $"\n最后一分钟收入 {info.count:0.#}"     // ← 错
```

`info.count` 来自 `WorkTimer.GetCount`，是 `addmoney` 的**逐秒累加值**，也就是整段工作的
总产出，不是最后一分钟。改成「本次收入 $X」/「本次共获得 X 经验」，中断时「已经结算 X」。

**列表行不跟状态走。** 从菜单或诊断口开始工作后，面板行还停在"锁定/开始"的样子——
因为只有 `RefreshWorkStatus()`（顶部状态行）在定时器里刷，列表行没人重建。
修法是比对「是否在干活 + 在干什么」的签名，变了才整表重建；每秒重建 13 行没必要，
还会把滚动位置打回顶部：

```csharp
var sig = WorkTimer.IsWorking ? "R:" + (WorkTimer.CurrentWork?.Name ?? "") : "-";
if (sig != _workRunSignature) { _workRunSignature = sig; RefreshWorkPanel(); }
else if (WorkTimer.IsWorking) RefreshWorkStatus();
```

### 5. FinishBonus 为什么没有真的发钱

`FinishBonus` 字段的注释是「完成奖励倍率」，`winWorkMenu` 里也显示成 `x1.15`，
看着像"做完多给 15%"。但把原版翻遍，它只出现在三处：

1. `ExtensionFunction.Get()` —— 算「收益效率估值」时 `1 + FinishBonus / 2`
2. `winWorkMenu` 的 `tbBonus` —— 纯展示
3. `EvaluationStatistics_WorkEnd` —— 统计口径反除（中断时 `totalYield /= 1 + FinishBonus`）

**逐秒结算的 `addmoney` 里没有它**。也就是说原版承诺了一个"完成奖励"，实际并没有发。
移植版这里选择不擅自发放：数值一致性是这个项目的底线，宁可少一个亮点，
也不要让存档里的钱和原版跑出偏差。第一版我在气泡里写了「+15% 完成奖励」，
发现对不上之后删掉了。

### 6. 验证

自检 P11（`SelfTestPanelsAsync` 尾部）：

```
[TEST] 工作面板: 工作=5 学习=4 玩耍=4 分类行数=6(应 6) 只含该类=True 收藏置顶=True
                高等级拦截=True 收益估算=True 进度条配色=True 进行中行=True 停止后复原=True
```

- `分类行数=6`：5 个「工作」+ 1 条脚注
- `收藏置顶`：收藏最后一项，检查列表第一行的名字是不是它（不是看 Opacity 那种间接判断）
- `高等级拦截`：造一个 `LevelLimit=999` 的工作，`StartWork` 必须返回 false
- `进度条配色`：`WorkTimer.StyledBackground` 读实际 `Border.Background`，比对 `413D39`
- `进行中行` / `停止后复原`：把列表里所有 Button 的 `Content` 和文本捞出来，
  检查出现「进行中」「停止」→ 停掉后变回「开始」

全量轻量自检 29 项通过，工作相关：`工作动画 期望=workone 实际=workone/A_Start/Happy`、
`工作 6 秒 金钱 997.50->998.10`、`停止后动画=workone/C_End`。

### 7. 截图方法的一个坑

用 `import -window <桌宠窗口id>` 抓图，桌面背景和宠物都会变成纯黑——因为这个窗口是
全屏透明窗，内容靠合成器与桌面叠加，单抓窗口拿不到合成结果。之前 `import -window root`
能得到正常画面（含桌面壁纸与宠物），但双屏 4720×1440 全屏抓取在本机内存紧张时容易
把进程推到 OOM。这次的做法是：

```bash
xdotool search --class VPet              # 拿窗口 id
import -window <id> shot.png             # 只抓面板那一层(宠物透明层会丢, 但面板是实的)
convert shot.png -crop 560x620+565+370   # 按面板实际位置裁
```

面板本身不透明，抓窗口足够核对布局；要连宠物一起看还是得用 `root`。

### 8. 仍未做

- **日程自动轮换**：原版 winWorkMenu 可以设「工作 45 分 / 休息 15 分」循环，
  到点自动切换。要做需要给 `MainLogic` 加快捷状态机，且和"打工中被打断"的现有逻辑对齐
- **代理套餐**（Agency/Package）：订阅制抽成，涉及存档新增结构，优先级低
- **工作倍率**：原版 `Work.Double(value)` 生成 ×2/×5 版本（消耗乘 `0.5+0.4×value`，
  等级门槛 `(LevelLimit+10)×value`）。入口是 `winWorkMenu` 的倍率选择，
  实现不难，但需要先想清楚"倍率版本"在存档 / 统计里怎么归属
