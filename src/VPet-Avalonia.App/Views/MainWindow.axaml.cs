using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.App.Native;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 桌宠主窗口
    /// 铺满整个屏幕、背景全透明、始终置顶, 宠物贴在屏幕底部,
    /// 透明区域鼠标穿透, 只有宠物本体可以拖拽和互动
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// 宠物在画布中的位置(逻辑像素)
        /// </summary>
        private double _petLeft;
        private double _petTop;

        /// <summary>
        /// 视口尺寸(逻辑像素)
        /// 窗口尺寸刚设置完时 Bounds 还没完成布局, 直接用它会得到 0,
        /// 导致宠物被夹到左上角, 所以这里单独记录
        /// </summary>
        private double _viewWidth = 1920;
        private double _viewHeight = 1080;

        /// <summary>
        /// 是否启用鼠标穿透
        /// </summary>
        private bool _clickThroughEnabled = true;

        /// <summary>
        /// 原生窗口句柄(X11 下为 Window id)
        /// </summary>
        private IntPtr _platformHandle = IntPtr.Zero;

        /// <summary>
        /// 当前会话类型
        /// </summary>
        private LinuxWindowNative.SessionType _sessionType = LinuxWindowNative.SessionType.Unknown;

        public MainWindow()
        {
            InitializeComponent();
            BuildContextMenu();
            HookLayoutRefresh();
            LoadAppIcon();
            InitializeGame();
        }

        /// <summary>
        /// 设置窗口图标(沿用原版 VPet 图标), 让任务栏/窗口切换器里能认出来
        /// </summary>
        private void LoadAppIcon()
        {
            try
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://VPet-Avalonia.App/assets/icon/vpet.png"));
                Icon = new WindowIcon(new Avalonia.Media.Imaging.Bitmap(stream));
            }
            catch
            {
                // 图标加载失败不影响桌宠运行
            }
        }

        /// <summary>
        /// 菜单刚设置 IsVisible=true 时 Bounds 尚未布局(高度为0),
        /// 此时计算出的 XShape 输入区域高度为 0 → 点击全部穿透到桌面,
        /// 表现为"右键菜单看得见但点不动"。
        /// 挂 LayoutUpdated, 布局真正完成后重算输入区域
        /// </summary>
        private void HookLayoutRefresh()
        {
            ContextPanel.LayoutUpdated += (s, e) =>
            {
                if (ContextPanel.IsVisible)
                    UpdateClickThrough();
            };
            // LayoutPetUI 会跳过不可见的控件(拖动时省一次无谓的 arrange),
            // 所以显示的那一刻要补一次定位, 否则气泡/计时器会停在旧坐标上
            MessageBar.LayoutUpdated += (s, e) =>
            {
                if (MessageBar.IsVisible)
                    LayoutPetUI();
            };
            WorkTimer.LayoutUpdated += (s, e) =>
            {
                if (WorkTimer.IsVisible)
                    LayoutPetUI();
            };
        }

        #region 初始化

        private void InitializeGame()
        {
            var dispatcher = new DispatcherAdapter(Dispatcher.UIThread);
            // GameBootstrap 内部日志已经带 [VPet] 前缀输出到控制台, 这里不再重复打印
            var core = GameBootstrap.Initialize(dispatcher);

            if (core == null)
            {
                Console.WriteLine("[VPet] 游戏核心初始化失败, 桌宠无法显示");
                return;
            }

            MainDisplay.Core = core;
            core.Controller = this;

            // 设置与统计: 之前这些值全写死在代码里(计算开关/间隔/自动保存/说话),
            // 现在从 ~/.local/share/VPet/settings.txt 读, 设置面板改完即时生效
            Settings = GameSettings.Load();
            Stats = CharacterStats.Load();
            Achievements.Load();
            SpeechPool.LoadUserSpeech();

            MainDisplay.MessageBar = MessageBar;
            MainDisplay.WorkTimer = WorkTimer;
            // 数值一变就让角色面板跟上, 不等它自己的定时器
            MainDisplay.ValueChanged += () =>
            {
                if (_charPanel is { IsVisible: true })
                    UpdateCharacterPanel();
            };

            RegisterTouchAreas(core);

            // 启动主循环与默认动画 (先应用设置, 主循环才会用对间隔)
            ApplySettings();
            MainDisplay.StartTimers();
            MainDisplay.DisplayNomal?.Invoke();
            MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.Greeting));

            // 工作结束结算(恢复状态 + 气泡播报)
            WorkTimer.E_FinishWork += OnWorkFinished;

            // 定期自动保存, 崩溃/强杀不丢进度
            StartAutoSave();
        }

        /// <summary>
        /// 注册摸头/摸身体的触摸区域
        /// </summary>
        private void RegisterTouchAreas(GameCore core)
        {
            if (core.Graph is not GraphCoreAvalonia gca)
                return;

            var cfg = gca.GraphConfigInternal;
            core.TouchEvent.Clear();

            // 摸头
            if (cfg.TouchHeadSize.Area() > 0)
            {
                core.TouchEvent.Add(new TouchArea(cfg.TouchHeadLocate, cfg.TouchHeadSize, () =>
                {
                    MainDisplay.DisplayTouchHead();
                    Stats.CountTouch();
                    // 大概三次里有一次会应一句话
                    if (Settings.SayEnabled && Function.Rnd.Next(3) == 0)
                        MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.TouchHead));
                    return true;
                }));
            }
            // 摸身体(长按)
            if (cfg.TouchBodySize.Area() > 0)
            {
                core.TouchEvent.Add(new TouchArea(cfg.TouchBodyLocate, cfg.TouchBodySize, () =>
                {
                    MainDisplay.DisplayTouchBody();
                    Stats.CountTouch();
                    if (Settings.SayEnabled && Function.Rnd.Next(2) == 0)
                        MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.TouchBody));
                    return true;
                }, true));
            }
            // 捏脸(长按, 对应原版 MainWindow: 有 pinch 行就 Insert(0), 区域在脸颊)
            // 动画目录 vup/Pinch 是 A/B/C 三段式, 播放逻辑在 Main.DisplayToPinch
            if (cfg.PinchSize.Area() > 0)
            {
                core.TouchEvent.Insert(0, new TouchArea(cfg.PinchLocate, cfg.PinchSize, () =>
                {
                    MainDisplay.DisplayToPinch();
                    Stats.CountPinch();
                    if (Settings.SayEnabled)
                        MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.Pinch));
                    CheckAchievementsAndNotify();
                    return true;
                }, true));
            }
        }

        #endregion

        #region 窗口布局

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _sessionType = LinuxWindowNative.DetectSession();
            SetupWindowBounds();
            LayoutPetUI();
            InitializeClickThrough();

            // 无鼠标环境下的功能自检: VPET_SELFTEST=1 启动时直接跑一遍投喂/商店/工作
            if (Environment.GetEnvironmentVariable("VPET_SELFTEST") == "1")
                _ = RunSelfTestAsync();

            // APNG 实机演示: VPET_APNG_DEMO=<文件> 时直接把这段动图播在桌宠身上,
            // 不给 MOD 也能肉眼/截屏确认 APNG 真在动
            var apngDemo = Environment.GetEnvironmentVariable("VPET_APNG_DEMO");
            if (!string.IsNullOrEmpty(apngDemo))
                _ = RunApngDemoAsync(apngDemo!);

            // 诊断后门: VPET_SHOW_PANEL=shop|items|char|settings|ach|work
            // 启动几秒后自动打开对应面板, 方便截屏核对布局(不然得先模拟右键菜单点击)
            var showPanel = Environment.GetEnvironmentVariable("VPET_SHOW_PANEL");
            if (!string.IsNullOrEmpty(showPanel))
                _ = ShowPanelLaterAsync(showPanel!);

            // 诊断后门: VPET_WORK_DEMO=<工作名|1> 启动即开始一项工作,
            // 用来截屏核对进度条配色与面板里的"进行中"行(把面板和进度条一起看)
            var workDemo = Environment.GetEnvironmentVariable("VPET_WORK_DEMO");
            if (!string.IsNullOrEmpty(workDemo))
                _ = RunWorkDemoAsync(workDemo!);
        }

        /// <summary>
        /// 延迟开始一项工作(VPET_WORK_DEMO), 只用于截屏核对
        /// </summary>
        private async System.Threading.Tasks.Task RunWorkDemoAsync(string name)
        {
            await System.Threading.Tasks.Task.Delay(2200);
            try
            {
                var all = AllWorks();
                var work = name == "1"
                    ? all.OrderBy(w => w.LevelLimit).FirstOrDefault()
                    : all.FirstOrDefault(w => w.Name == name) ?? all.OrderBy(w => w.LevelLimit).FirstOrDefault();
                if (work == null)
                {
                    Console.WriteLine("[VPet] 工作演示: 没有可用的工作");
                    return;
                }
                // 演示用: 临时把等级门槛清掉, 免得低等级存档选不了
                var bak = work.LevelLimit;
                work.LevelLimit = 0;
                var ok = MainDisplay.StartWork(work);
                work.LevelLimit = bak;
                Console.WriteLine($"[VPet] 工作演示: {work.Name} ok={ok} " +
                                  $"进度条底色={WorkTimer.StyledBackground}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 工作演示失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟打开指定面板(VPET_SHOW_PANEL), 只用于截屏核对
        /// </summary>
        private async System.Threading.Tasks.Task ShowPanelLaterAsync(string name)
        {
            await System.Threading.Tasks.Task.Delay(1500);
            Console.WriteLine($"[VPet] 准备打开面板: {name}");
            try
            {
                switch (name.Trim().ToLowerInvariant())
                {
                    case "shop": ShowShop(); break;
                    case "items": ShowInventoryPanel(); break;
                    case "char":
                        ShowCharacterPanel();
                        // 配合 VPET_CHAR_TAB=0|1|2 直接切到概览/记录/年报, 方便逐个截屏核对
                        var tabEnv = Environment.GetEnvironmentVariable("VPET_CHAR_TAB");
                        if (int.TryParse(tabEnv, out var tabIdx) && tabIdx is >= 0 and <= 2)
                            SwitchTab(tabIdx);
                        break;
                    case "settings": ShowSettingsPanel(); break;
                    case "ach": ShowAchievementsPanel(); break;
                    case "work": ShowWorkPanel(); break;
                    default: Console.WriteLine("[VPet] 未知面板: " + name); break;
                }
                Console.WriteLine($"[VPet] 已打开面板: {name}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 打开面板失败: " + e.Message);
            }
        }

        /// <summary>
        /// 把指定 APNG 循环播在桌宠上, 并打印帧数/解码耗时(配合截屏验证)
        /// </summary>
        private async System.Threading.Tasks.Task RunApngDemoAsync(string file)
        {
            await System.Threading.Tasks.Task.Delay(2500);
            try
            {
                var anim = new VPetAvalonia.Core.APNGAnimation(null!, file,
                    new VPetAvalonia.Abstractions.GraphInfo("apng_demo"), isLoop: true);
                anim.Run(MainDisplay.PetElement, null);
                for (int i = 0; i < 25; i++)
                    await System.Threading.Tasks.Task.Delay(200);
                Console.WriteLine($"[APNGDEMO] 文件={System.IO.Path.GetFileName(file)} 帧数={anim.FrameCount} " +
                                  $"就绪={anim.IsReady} 失败={anim.IsFail} 缓存命中={anim.FromCache} " +
                                  $"解码={anim.DecodeMs}ms 播放状态={anim.Control?.PlayState}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[APNGDEMO] 异常: " + e.Message);
            }
        }

        /// <summary>
        /// 功能自检 (仅 VPET_SELFTEST=1 时执行, 不影响正常使用)
        /// </summary>
        private async System.Threading.Tasks.Task RunSelfTestAsync()
        {
            await System.Threading.Tasks.Task.Delay(3000);
            var core = MainDisplay.Core;
            if (core == null)
            {
                Console.WriteLine("[TEST] 核心为空, 自检中止");
                return;
            }

            // 本机内存紧张(Committed_AS 长期高于 CommitLimit), 多屏切换那一段要反复解码
            // 2160x1440 / 2048x1152 的位图, 容易把进程推到 OOM, 后面的投喂/摸头项就跑不到。
            // 设 VPET_SELFTEST_LIGHT=1 可跳过该段, 单独验证其余功能。
            var light = Environment.GetEnvironmentVariable("VPET_SELFTEST_LIGHT") == "1";

            // VPET_SELFTEST_ONLY=panels: 只跑商店/物品栏/设置的增强项。
            // 全量自检会连着建七八个面板, 本机内存紧张时经常跑到一半 OOM,
            // 有了这个开关就能单独验证新面板而不受前面步骤影响。
            if (Environment.GetEnvironmentVariable("VPET_SELFTEST_ONLY") == "panels")
            {
                Console.WriteLine("[TEST] 只跑面板增强自检");
                await SelfTestPanelsAsync(core);
                Console.WriteLine("[TEST] 自检结束");
                return;
            }

            Console.WriteLine($"[TEST] 食物={core.Foods.Count} 背包={core.Inventory.TotalCount} 金钱={core.Save?.Money:F1}");

            // 1. 投喂 (先把数值压到 50, 否则满值看不出恢复效果)
            var dropFood = core.Save!.StrengthFood - 50;
            if (dropFood > 0) core.Save.StrengthChangeFood(-dropFood);
            var dropDrink = core.Save.StrengthDrink - 50;
            if (dropDrink > 0) core.Save.StrengthChangeDrink(-dropDrink);
            // 背包里只有新手补给的小零食, 塞一份饱腹效果最大的食物来验证数值恢复
            var bigFood = core.Foods.OrderByDescending(f => f.StrengthFood + f.StrengthDrink).FirstOrDefault();
            if (bigFood != null)
                core.Inventory.Add(bigFood.Name, 1);

            var food = core.Foods.Where(f => core.Inventory.Count(f.Name) > 0)
                .OrderByDescending(f => f.StrengthFood + f.StrengthDrink)
                .FirstOrDefault();
            if (food != null)
            {
                var sf0 = core.Save!.StrengthFood;
                var sd0 = core.Save.StrengthDrink;
                var fl0 = core.Save.Feeling;
                var n0 = core.Inventory.Count(food.Name);
                FeedFood(food.Name);
                await System.Threading.Tasks.Task.Delay(1200);
                Console.WriteLine($"[TEST] 投喂 {food.Name}: 饱腹 {sf0:F1}->{core.Save.StrengthFood:F1}, 口渴 {sd0:F1}->{core.Save.StrengthDrink:F1}, 心情 {fl0:F1}->{core.Save.Feeling:F1}, 库存 {n0}->{core.Inventory.Count(food.Name)}");
            }
            else
            {
                Console.WriteLine("[TEST] 背包里没有可投喂的食物");
            }

            // ---- 面板类 (P1~P3) ----
            // 放在最前面跑: 这几个要现建控件树, 而自检跑到后面堆已经接近硬上限
            // (本机 Committed_AS 长期高于 CommitLimit), 排在末尾会 OOM 跑不到。

            // P1. 设置面板: 改动是否落到 settings.txt, 重新读取是否一致
            try
            {
                ShowSettingsPanel();
                await System.Threading.Tasks.Task.Delay(300);
                var visible = _settingsPanel?.IsVisible == true;
                var rows = _settingsBody?.Children.Count ?? 0;

                var beforeInterval = Settings.LogicIntervalSec;
                var probe = beforeInterval == 25 ? 20 : 25;
                Settings.LogicIntervalSec = probe;
                Settings.Save();
                var reread = GameSettings.Load().LogicIntervalSec;
                Settings.LogicIntervalSec = beforeInterval;
                Settings.Save();

                if (_settingsPanel != null)
                    _settingsPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 设置面板: 可见={visible} 控件块={rows} " +
                                  $"写入={probe} 重读={reread} 一致={reread == probe} " +
                                  $"文件={System.IO.File.Exists(GameSettings.SettingsPath)}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 设置面板自检失败: " + e.Message);
            }

            // P2. 说话文本池: 抽得出句子、不是单句复读、能按处境选词
            try
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < 12; i++)
                    seen.Add(SpeechPool.Pick(SpeechPool.Scene.Idle));

                var idleLine = SpeechPool.PickIdle(core.Save);

                // 把饱腹压到 20, 看会不会说"饿"这一类
                var foodBak = core.Save!.StrengthFood;
                core.Save.StrengthFood = 20;
                var hungryHit = 0;
                for (int i = 0; i < 20; i++)
                {
                    var l = SpeechPool.PickIdle(core.Save);
                    if (l.Contains("饿") || l.Contains("肚")) hungryHit++;
                }
                core.Save.StrengthFood = foodBak;

                Console.WriteLine($"[TEST] 说话池: 12 次抽取去重={seen.Count} 句(应 >1), " +
                                  $"当前句=\"{idleLine}\", 低饱腹 20 次里提到饿={hungryHit} 次(应 >0)");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 说话池自检失败: " + e.Message);
            }

            // P3. 角色面板: 打开后内容是否真的填出来了, 统计能否落盘
            try
            {
                var touch0 = Stats.TouchCount;
                Stats.CountTouch();
                Stats.MarkDirty();
                Stats.SaveIfDirty();

                ShowCharacterPanel();
                await System.Threading.Tasks.Task.Delay(400);
                var charVisible = _charPanel?.IsVisible == true;
                var blocks = _charBody?.Children.Count ?? 0;
                var statFile = System.IO.File.Exists(CharacterStats.StatsPath);

                if (_charPanel != null)
                    _charPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 角色面板: 可见={charVisible} 区块={blocks} " +
                                  $"摸头计数 {touch0}->{Stats.TouchCount} 统计落盘={statFile} " +
                                  $"陪伴={CharacterStats.HumanDuration(Stats.PlaySeconds)}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 角色面板自检失败: " + e.Message);
            }

            // P4. 物品栏: 打开有内容, 分类过滤真的起作用
            try
            {
                ShowInventoryPanel();
                await System.Threading.Tasks.Task.Delay(400);
                var invVisible = _invPanel?.IsVisible == true;
                var rows0 = _invBody?.Children.Count ?? 0;
                _invFilter = Food.FoodType.Meal;
                RefreshInventoryPanel();
                var rowsMeal = _invBody?.Children.Count ?? 0;
                _invFilter = (Food.FoodType)(-1);
                RefreshInventoryPanel();
                if (_invPanel != null)
                    _invPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 物品栏: 可见={invVisible} 区块={rows0} " +
                                  $"过滤正餐后={rowsMeal} 背包件数={core.Inventory.TotalCount}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 物品栏自检失败: " + e.Message);
            }

            // P5. 捏脸: 动画能切过去 + 触摸区注册了长按区 + 统计计数
            try
            {
                var pressAreas = core.TouchEvent.Where(a => a.IsPress).ToList();
                var pinchGraph = core.Graph is GraphCoreAvalonia gP
                    ? gP.FindGraph("pinch", GraphInfo.AnimatType.A_Start, core.Save!.Mode) : null;

                var str0 = core.Save!.Strength;
                core.Save.StoreStrength = 0; core.Save.StoreStrengthFood = 0; core.Save.StoreStrengthDrink = 0;
                var pinch0 = Stats.PinchCount;
                MainDisplay.DisplayToPinch();
                await System.Threading.Tasks.Task.Delay(600);
                var pinchAnim = $"{MainDisplay.DisplayType.Name}/{MainDisplay.DisplayType.Animat}";
                Stats.CountPinch();
                Stats.MarkDirty();
                Stats.SaveIfDirty();
                var pinch1 = Stats.PinchCount;

                Console.WriteLine($"[TEST] 捏脸: 长按区={pressAreas.Count}个(应>0) 动画={pinchAnim}(应pinch/A_Start) " +
                                  $"计数 {pinch0}->{pinch1} 体力 {str0:F1}->{core.Save.Strength:F1}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 捏脸自检失败: " + e.Message);
            }

            // P6. 成就: 阈值到达解锁 + 落盘 + 面板能列出来
            try
            {
                var touch0 = Stats.TouchCount;
                Stats.TouchCount = 10;
                var fresh = Achievements.CheckAll(Stats, core.Save);
                var achFile = System.IO.File.Exists(Achievements.AchPath);
                var achHasTouch = achFile &&
                    System.IO.File.ReadAllLines(Achievements.AchPath).Any(l => l.Trim() == "touch_10");
                Stats.TouchCount = touch0;

                ShowAchievementsPanel();
                await System.Threading.Tasks.Task.Delay(400);
                var achVisible = _achPanel?.IsVisible == true;
                var achRows = _achBody?.Children.Count ?? 0;
                if (_achPanel != null)
                    _achPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 成就: 解锁={fresh.Count}条(应1, {string.Join(",", fresh.Select(x => x.Name))}) " +
                                  $"落盘={achHasTouch} 面板可见={achVisible} 面板区块={achRows} " +
                                  $"总数={Achievements.Defs.Length}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 成就自检失败: " + e.Message);
            }

            // P7. APNG: 现场合成 4 帧 APNG, 走一遍 解析→合成→缓存复用→播放 全链路
            try
            {
                var apngOk = ApngTestTool.RunSynthetic(out var apngDetail);
                Console.WriteLine($"[TEST] APNG: {apngDetail}{(apngOk ? "" : " (断言未通过)")}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] APNG 自检失败: " + e.Message);
            }

            // P8~P10. 商店 / 物品栏 / 设置 的增强项(抽成独立方法, 便于单跑)
            await SelfTestPanelsAsync(core);

            // 2. 商店购买
            var cheap = core.Foods.Where(f => f.Price > 0).OrderBy(f => f.Price).FirstOrDefault();
            if (cheap != null)
            {
                var m0 = core.Save!.Money;
                BuyFood(cheap);
                await System.Threading.Tasks.Task.Delay(300);
                Console.WriteLine($"[TEST] 购买 {cheap.Name}: 金钱 {m0:F1}->{core.Save.Money:F1}, 持有 {core.Inventory.Count(cheap.Name)}");
            }

            // 3. 工作
            if (core.Graph is GraphCoreAvalonia gca && gca.GraphConfigInternal.Works.Count > 0)
            {
                var work = gca.GraphConfigInternal.Works[0];
                var ok = MainDisplay.StartWork(work);
                Console.WriteLine($"[TEST] 开始工作 {work.Name}: ok={ok} State={MainDisplay.State} NowWork={MainDisplay.NowWork?.Name} TimerIsWorking={WorkTimer.IsWorking}");
                await System.Threading.Tasks.Task.Delay(600);
                // 验证工作动画真的切过去了(之前 Work.Display 被静默忽略, 宠物只会站着)
                Console.WriteLine($"[TEST] 工作动画: 期望={work.Graph} 实际={MainDisplay.DisplayType.Name}/{MainDisplay.DisplayType.Animat}/{MainDisplay.DisplayType.ModeType}");
                var money0 = core.Save!.Money;
                await System.Threading.Tasks.Task.Delay(6000);
                Console.WriteLine($"[TEST] 工作 6 秒: 金钱 {money0:F2}->{core.Save.Money:F2}, 计时={WorkTimer.WorkMinutes:F2}分");
                // 等过一个完整的 15 秒 EventTimer 周期, 数值结算发生在那里
                await System.Threading.Tasks.Task.Delay(14000);
                Console.WriteLine($"[TEST] 工作 20 秒: 金钱 {money0:F2}->{core.Save.Money:F2}, 经验={core.Save.Exp:F1}, 体力={core.Save.Strength:F1}, 饱腹={core.Save.StrengthFood:F1}, 计时={WorkTimer.WorkMinutes:F2}分");
                WorkTimer.Stop();
                await System.Threading.Tasks.Task.Delay(500);
                Console.WriteLine($"[TEST] 停止后 State={MainDisplay.State} NowWork={MainDisplay.NowWork?.Name ?? "null"} TimerIsWorking={WorkTimer.IsWorking}");
                await System.Threading.Tasks.Task.Delay(400);
                Console.WriteLine($"[TEST] 停止后动画={MainDisplay.DisplayType.Name}/{MainDisplay.DisplayType.Animat}");
            }
            else
            {
                Console.WriteLine("[TEST] 没有可用的工作配置");
            }

            // 4. 睡觉
            ToggleSleep();
            Console.WriteLine($"[TEST] 睡觉后 State={MainDisplay.State}");
            ToggleSleep();
            Console.WriteLine($"[TEST] 唤醒后 State={MainDisplay.State}");

            // 5. 存档往返: 写盘再读回。
            // 历史 bug: 用 LPSConvert.SerializeObject 写 ToFloat 字段会被放大 10^9,
            // 读回来被 setter 夹到上限所以"看着正常", 但 money/exp 没上限 → 越存越大
            try
            {
                var money0 = core.Save!.Money;
                var exp0 = core.Save.Exp;
                var str0 = core.Save.Strength;
                var food0 = core.Save.StrengthFood;
                core.Save.Save();
                var text = System.IO.File.ReadAllText(GameSave.SavePath);
                core.Save.Load();
                Console.WriteLine($"[TEST] 存档往返: 金钱 {money0:F2}->{core.Save.Money:F2}, 经验 {exp0:F2}->{core.Save.Exp:F2}, " +
                                  $"体力 {str0:F1}->{core.Save.Strength:F1}, 饱腹 {food0:F1}->{core.Save.StrengthFood:F1}");
                Console.WriteLine($"[TEST] 存档内容: {text}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 存档往返失败: " + e.Message);
            }

            // 6. 菜单: 打开根菜单后点「投喂」, 应该停留在子菜单而不是被关掉
            //    (工作菜单改成了独立面板, 子菜单只剩投喂这类列表)
            try
            {
                ShowContextMenu(new Avalonia.Point(200, 200));
                var rootCount = ContextItems.Children.Count;
                InvokeMenuItem(ShowFeedMenu);
                var childCount = ContextItems.Children.Count;
                Console.WriteLine($"[TEST] 菜单: 根 {rootCount} 项 → 点子菜单后 可见={ContextPanel.IsVisible} " +
                                  $"项数={childCount} 高度={ContextPanel.Bounds.Height:F0}");
                InvokeMenuItem(RestoreRootMenu);
                Console.WriteLine($"[TEST] 返回根菜单: 可见={ContextPanel.IsVisible} 项数={ContextItems.Children.Count}");
                HideContextMenu();
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 菜单自检失败: " + e.Message);
            }

            // 7. 跨屏: 窗口是否跟着宠物换到另一块显示器
            if (light)
            {
                Console.WriteLine("[TEST] 跨屏/边缘换屏/松手换屏: 轻量模式跳过 (VPET_SELFTEST_LIGHT=1)");
            }
            else
            try
            {
                var before = $"{_screen?.Bounds.X},{_screen?.Bounds.Y} {_viewWidth:0}x{_viewHeight:0} 宠物=({_petLeft:0},{_petTop:0})";
                var moved = TrySwitchScreen(new Avalonia.Point(_viewWidth + 300, _viewHeight / 2));
                Console.WriteLine($"[TEST] 跨屏: 切换={moved} {before} → {_screen?.Bounds.X},{_screen?.Bounds.Y} " +
                                  $"{_viewWidth:0}x{_viewHeight:0} 宠物=({_petLeft:0},{_petTop:0})");
                await Task.Delay(600);
                Console.WriteLine($"[TEST] 跨屏后窗口实际: Position=({Position.X},{Position.Y}) Bounds={Bounds.Width:0}x{Bounds.Height:0}");

                if (moved)
                {
                    await Task.Delay(700); // 避开换屏冷却(600ms)
                    var back = TrySwitchScreen(new Avalonia.Point(-300, _viewHeight / 2));
                    Console.WriteLine($"[TEST] 换回: {back} → {_screen?.Bounds.X},{_screen?.Bounds.Y} {_viewWidth:0}x{_viewHeight:0}");
                    await Task.Delay(600);
                    Console.WriteLine($"[TEST] 换回后窗口实际: Position=({Position.X},{Position.Y})");

                    // 8. 边缘换屏(真实拖动路径): 宠物顶到右边缘 + 指针在宠物中部继续右拖
                    await Task.Delay(700);
                    FreeClamp = true;                          // 模拟拖动中
                    SetPetPosition(_viewWidth - 20, _petTop, clamp: false); // 顶到最右(允许超界)
                    var cursorAt = new Avalonia.Point(_viewWidth - 30, _petTop + MainDisplay.Height / 2);
                    var byDir = TrySwitchScreenByDirection(1, 0, cursorAt);
                    await Task.Delay(600);
                    Console.WriteLine($"[TEST] 边缘换屏: 切换={byDir} 屏={_screen?.Bounds.X} " +
                                      $"窗口实际=({Position.X},{Position.Y}) 宠物=({_petLeft:0},{_petTop:0})");

                    // 8b. 换屏冷却: 紧接着再请求一次, 必须被挡住。
                    //     v1.1.4 之前这条路径没有限流, 加上换屏后窗口坐标系是异步更新的,
                    //     旧坐标会被再次判成"出窗" → 连续横跳(实机 3 秒切了 10 次)
                    var again = TrySwitchScreenByDirection(1, 0, cursorAt);
                    var maxL = Math.Max(0, _viewWidth - MainDisplay.Width);
                    Console.WriteLine($"[TEST] 换屏冷却: 立即再换={again} (应为 False) " +
                                      $"落点=({_petLeft:0},{_petTop:0}) 允许 0..{maxL:0} " +
                                      $"合法={_petLeft >= -0.5 && _petLeft <= maxL + 0.5}");

                    // 8c. 换屏后边界距离必须基于目标屏尺寸(_viewWidth/_viewHeight),
                    //     不能读 Bounds —— 那还是旧屏宽度, 会凭空判出"越界", 触发反向换屏。
                    //     这正是"闪到最右边又闪回来"的成因。
                    var dr = GetWindowsDistanceRight();
                    var expectR = _viewWidth - MainDisplay.Width - _petLeft;
                    Console.WriteLine($"[TEST] 换屏后边界: 右={dr:0} 期望={expectR:0} " +
                                      $"一致={Math.Abs(dr - expectR) < 1} 视口={_viewWidth:0}x{_viewHeight:0}");

                    FreeClamp = false;
                    SetPetPosition(_petLeft, _petTop);

                    // 9. 松手兜底: 指针落在另一块屏上
                    await Task.Delay(500);
                    FinishDragAt(new Avalonia.Point(_viewWidth + 200, _viewHeight / 2));
                    await Task.Delay(600);
                    Console.WriteLine($"[TEST] 松手换屏: 屏={_screen?.Bounds.X} 窗口实际=({Position.X},{Position.Y}) 宠物=({_petLeft:0},{_petTop:0})");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 跨屏自检失败: " + e.Message);
            }

            // 10. 拖动跟手: 模拟一次拖动里的大量位移, 确认不再逐帧下发 XShape。
            //     每帧一次 XShapeCombineRectangles 会让合成器反复重建输入区域,
            //     UI 线程被 X 请求拖住 → 宠物严重落后于鼠标
            try
            {
                var c0 = LinuxWindowNative.InputRegionCalls;
                FreeClamp = true;
                BeginDragShape();
                var c1 = LinuxWindowNative.InputRegionCalls;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 120; i++)
                    SetPetPosition(200 + i * 3, 300 + i * 2);
                sw.Stop();
                var c2 = LinuxWindowNative.InputRegionCalls;
                EndDragShape();
                var c3 = LinuxWindowNative.InputRegionCalls;
                FreeClamp = false;
                SetPetPosition(_petLeft, _petTop);
                Console.WriteLine($"[TEST] 拖动 X 请求: 120 次位移中 {c2 - c1} 次 " +
                                  $"(应为 0), 起始整窗 {c1 - c0} 次, 收尾恢复 {c3 - c2} 次, " +
                                  $"120 次位移耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 拖动跟手自检失败: " + e.Message);
            }

            // 11. 拖动比例: 指针走 N, 宠物必须走 N。
            //     历史 bug: 抓取偏移用 MainGrid 坐标(原点跟着宠物跑)、定位用窗口坐标,
            //     两者混用会形成反馈回路, 跟随比例退化成 1/2 —— "鼠标走两步宠物走一步"
            try
            {
                FreeClamp = true;
                BeginDragShape();
                var p0 = PetTopLeft();                                   // Vector
                var cursor0 = new Avalonia.Point(p0.X + 250, p0.Y + 250);
                var grab = cursor0 - p0;
                const double step = 5;
                const int frames = 60;
                for (int i = 1; i <= frames; i++)
                    DragTo(new Avalonia.Point(cursor0.X + step * i, cursor0.Y), grab);
                var moved = PetTopLeft() - p0;
                EndDragShape();
                FreeClamp = false;
                SetPetPosition(_petLeft, _petTop);
                Console.WriteLine($"[TEST] 拖动比例: 指针位移={step * frames:0} 宠物位移={moved.X:0} " +
                                  $"(应为 {step * frames:0}), 比例={(step * frames > 0 ? moved.X / (step * frames) : 0):0.00}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 拖动比例自检失败: " + e.Message);
            }

            // 12. 摸头弹窗文案 vs 实际数值。
            //     历史 bug: 弹窗消失时没有把累积值清零, 第二次摸头会接着上次的数字累加,
            //     弹窗写"体力-4 心情+2"而面板只掉了 2 点 / 涨了 1 点
            try
            {
                core.Save!.Strength = 80;
                core.Save.Feeling = 50;
                // FunctionSpend 每 15s 会调 StoreTake(): 把 StoreStrength 的 1/10 返还到体力。
                // 直接写 Strength 而不清 StoreStrength, 两次摸头之间会被后台"偷偷回血"污染读数,
                // 出现"体力--9"这种假异常。这里把储存量清零, 只测摸头本身的效果。
                core.Save.StoreStrength = 0;
                core.Save.StoreStrengthFood = 0;
                core.Save.StoreStrengthDrink = 0;
                core.Save.Strength = 80;
                core.Save.Feeling = 50;
                var s0 = core.Save.Strength;
                var f0 = core.Save.Feeling;
                MainDisplay.DisplayTouchHead();
                await Task.Delay(150);
                var t1 = MainDisplay.LabelDisplayText.Text;
                var s1 = core.Save.Strength;
                var f1 = core.Save.Feeling;
                // 等弹窗自然消失(LabelDisplayShowChangeNumber 默认 2000ms)
                await Task.Delay(2600);
                MainDisplay.DisplayTouchHead();
                await Task.Delay(150);
                var t2 = MainDisplay.LabelDisplayText.Text;
                var s2 = core.Save.Strength;
                var f2 = core.Save.Feeling;
                Console.WriteLine($"[TEST] 摸头弹窗: 第1次 实际 体力-{s0 - s1:F0} 心情+{f1 - f0:F0} 文案=\"{t1}\" | " +
                                  $"第2次 实际 体力-{s1 - s2:F0} 心情+{f2 - f1:F0} 文案=\"{t2}\" " +
                                  $"(文案应分别是 -2/+1, 不能累加)");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 摸头弹窗自检失败: " + e.Message);
            }

            // 13. 投喂动画: 尺寸不能被食物图标帧宽污染, 且必须播【后层】(完整宠物本体)。
            //     历史 bug 1: 直接播 FoodAnimation 会把中间层食物图标帧宽(60~90px)贴到宠物身上
            //     历史 bug 2: 只播前层(Front_Lay, 259x147 的手部小块) → 桌面上"只剩一只手"
            try
            {
                var feedable = core.Foods.FirstOrDefault(f => core.Inventory.Count(f.Name) > 0);
                if (feedable != null)
                {
                    var mode = core.Save!.Mode;
                    var fa = core.Graph?.FindGraph(feedable.GetGraph(), GraphInfo.AnimatType.Single, mode) as FoodAnimation;
                    var layAnimat = fa?.GraphInfo?.Animat ?? GraphInfo.AnimatType.Single;
                    IGraph? back = fa?.Back_Lay is { Length: > 0 } bl
                        ? core.Graph?.FindGraph(bl, layAnimat, mode)
                        : null;

                    var w0 = MainDisplay.PetElement.ImageControl.Width;
                    var h0 = MainDisplay.PetElement.ImageControl.Height;
                    FeedFood(feedable.Name);
                    await Task.Delay(300);
                    var wMid = MainDisplay.PetElement.ImageControl.Width;
                    var hMid = MainDisplay.PetElement.ImageControl.Height;
                    await Task.Delay(600);
                    var wEnd = MainDisplay.PetElement.ImageControl.Width;
                    Console.WriteLine($"[TEST] 投喂动画: 图={feedable.GetGraph()} 后层={fa?.Back_Lay ?? "<无>"} " +
                                      $"命中={back != null}(必须是后层, 前层只是手部小块) " +
                                      $"宠物尺寸 {w0:0}x{h0:0} → 播放中 {wMid:0}x{hMid:0} → 结束后 {wEnd:0} " +
                                      $"(播放中必须仍是 500)");
                }
                else
                {
                    Console.WriteLine("[TEST] 投喂动画: 背包无食物, 跳过");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 投喂动画自检失败: " + e.Message);
            }

            Console.WriteLine("[TEST] 自检结束");
        }

        /// <summary>
        /// 面板增强自检(商店/物品栏/设置新增项)。
        /// 单独成方法: 内存紧张时可 VPET_SELFTEST_ONLY=panels 只跑这一段,
        /// 不必先建完前面七八个面板才发现 OOM。
        /// </summary>
        private async System.Threading.Tasks.Task SelfTestPanelsAsync(GameCore core)
        {
            // P8. 商店增强: 分类/搜索/排序/批量购买/钱不够降级
            try
            {
                _shopFilter = (Food.FoodType)(-1);
                _shopQuery = "";
                _shopSort = 0;
                _shopOnlyAffordable = false;

                ShowShop();
                await System.Threading.Tasks.Task.Delay(500);
                var shopVisible = _shopPanel?.IsVisible == true;
                var toolsBuilt = _shopSearch != null && (_shopFilterRow?.Children.Count ?? 0) > 0;

                RefreshShopList();
                var allCount = LastShopOrder.Count;

                _shopFilter = Food.FoodType.Meal;
                RefreshShopList();
                var mealCount = LastShopOrder.Count;
                var mealExpect = core.Foods.Count(f => f.Type == Food.FoodType.Meal);
                var mealOnly = LastShopOrder.All(n => core.Foods.First(f => f.Name == n).Type == Food.FoodType.Meal);

                _shopFilter = (Food.FoodType)(-1);
                _shopSort = 1;
                RefreshShopList();
                var prices = LastShopOrder.Select(n => core.Foods.First(f => f.Name == n).Price).ToList();
                var priceAsc = prices.Zip(prices.Skip(1), (a, b) => a <= b).All(x => x);

                _shopSort = 2;
                RefreshShopList();
                var nameAsc = LastShopOrder.SequenceEqual(LastShopOrder.OrderBy(x => x, StringComparer.Ordinal));

                var probe = core.Foods.First(f => f.Name.Length >= 2).Name;
                _shopQuery = probe;
                _shopSort = 0;
                RefreshShopList();
                var searchCount = LastShopOrder.Count;
                var searchHit = searchCount > 0 && LastShopOrder.All(n =>
                    n.IndexOf(probe, StringComparison.OrdinalIgnoreCase) >= 0);
                // 回到全量: 下面分页断言要基于 122 项
                _shopQuery = "";
                RefreshShopList();

                // 批量购买 5 份
                var bulkFood = core.Foods.Where(f => f.Price > 0).OrderBy(f => f.Price).First();
                var moneyBak = core.Save!.Money;
                if (core.Save.Money < bulkFood.Price * 5)
                    core.Save.Money = bulkFood.Price * 5 + 10;
                var m0 = core.Save.Money;
                var b0 = core.Inventory.Count(bulkFood.Name);
                BuyFood(bulkFood, 5);
                await System.Threading.Tasks.Task.Delay(300);
                var m1 = core.Save.Money;
                var b1 = core.Inventory.Count(bulkFood.Name);

                // 余额不足时不能白拿
                core.Save.Money = 0;
                var b2 = core.Inventory.Count(bulkFood.Name);
                BuyFood(bulkFood, 5);
                var b3 = core.Inventory.Count(bulkFood.Name);
                core.Save.Money = moneyBak;

                if (_shopPanel != null)
                    _shopPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 商店: 可见={shopVisible} 工具栏={toolsBuilt} 全部={allCount}项 " +
                                  $"正餐={mealCount}(应{mealExpect}) 全是正餐={mealOnly} " +
                                  $"价格升序={priceAsc} 名称升序={nameAsc} 搜索\"{probe}\"={searchCount}条 命中={searchHit}");
                Console.WriteLine($"[TEST] 商店分页: 页大小={ShopPageSize} 列表控件={_shopBody?.Children.Count ?? 0}" +
                                  $"(应≤{ShopPageSize + 2}) 全量={LastShopOrder.Count}" +
                                  $" 底部翻页条={(_shopFooter?.Children.Count ?? 0) > 0}(应 True)" +
                                  $" 面板高={(_shopPanel?.Bounds.Height ?? 0):F0}");
                Console.WriteLine($"[TEST] 商店批量: 买5份 钱 {m0:F1}->{m1:F1}(应扣{bulkFood.Price * 5:F1}) 件 {b0}->{b1}(应+5); " +
                                  $"余额0再买5 件 {b2}->{b3}(应不变)");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 商店自检失败: " + e.Message);
            }

            // P9. 物品栏增强: 排序/收藏置顶/连续使用
            try
            {
                ShowInventoryPanel();
                await System.Threading.Tasks.Task.Delay(400);

                _invSort = 1;
                _invQuery = "";
                _invFilter = (Food.FoodType)(-1);
                RefreshInventoryList();
                var ip = LastInventoryOrder.Select(n => core.Foods.First(f => f.Name == n).Price).ToList();
                var invPriceDesc = ip.Zip(ip.Skip(1), (a, b) => a >= b).All(x => x);

                // 收藏"原本排在最后"的那一项, 它应该跳到第一
                var target = LastInventoryOrder.LastOrDefault();
                var wasFav = target != null && Favorites.Contains(target);
                var starTop = false;
                if (target != null)
                {
                    if (!wasFav)
                        Favorites.Toggle(target);
                    RefreshInventoryList();
                    starTop = LastInventoryOrder.FirstOrDefault() == target;
                    if (!wasFav)
                        Favorites.Toggle(target);   // 还原
                    RefreshInventoryList();
                }

                // 连续使用: 塞 3 份最顶饱的东西, 把饱腹压到 50, 看会不会连吃且不超上限
                var bulkFood = core.Foods.Where(f => f.StrengthFood > 0)
                    .OrderByDescending(f => f.StrengthFood).FirstOrDefault();
                var useInfo = "无可用食物";
                if (bulkFood != null)
                {
                    core.Inventory.Add(bulkFood.Name, 3);
                    core.Save!.StoreStrengthFood = 0;
                    core.Save.StrengthFood = Math.Min(core.Save.StrengthFood, 50);
                    var n0 = core.Inventory.Count(bulkFood.Name);
                    var f0 = core.Save.StrengthFood;
                    UseAll(bulkFood);
                    await System.Threading.Tasks.Task.Delay(600);
                    var n1 = core.Inventory.Count(bulkFood.Name);
                    useInfo = $"{bulkFood.Name} 库存 {n0}->{n1} 饱腹 {f0:F0}->{core.Save.StrengthFood:F0}" +
                              $"(上限 {core.Save.StrengthMax:F0})";
                }

                if (_invPanel != null)
                    _invPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 物品栏: 排序单价降序={invPriceDesc} 收藏置顶={starTop} " +
                                  $"收藏数={Favorites.Count} 文件={System.IO.File.Exists(Favorites.FilePath)}");
                Console.WriteLine($"[TEST] 物品栏工具栏: 搜索框={_invSearch != null} " +
                                  $"分类按钮={_invFilterRow?.Children.Count ?? 0}(应 6) " +
                                  $"排序按钮={_invSortRow?.Children.Count ?? 0}(应 6=标题+4按钮+收藏提示) " +
                                  $"列表控件={_invBody?.Children.Count ?? 0}");
                Console.WriteLine($"[TEST] 连续使用: {useInfo}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 物品栏自检失败: " + e.Message);
            }

            // P10. 设置新增项: 气泡时长 / 自己走动 / 存档备份 / 统计清零逻辑
            try
            {
                var bubbleBak = Settings.BubbleSeconds;
                Settings.BubbleSeconds = 9;
                ApplySettings();
                var applied = MainDisplay.MessageBar?.AutoHideDelay == 9000;
                Settings.Save();
                var rereadBubble = GameSettings.Load().BubbleSeconds;

                var moveBak = Settings.AllowRandomMove;
                Settings.AllowRandomMove = false;
                Settings.Save();
                var rereadMove = GameSettings.Load().AllowRandomMove;

                Settings.AllowRandomMove = moveBak;
                Settings.BubbleSeconds = bubbleBak;
                Settings.Save();
                ApplySettings();

                var bk = SaveBackup.Create(out var bkFiles);

                // 清统计不清存档: 用临时对象验证 Clear 的字段范围
                var tmp = new CharacterStats { TouchCount = 5, FeedCount = 5, TopFood = "X", TopFoodCount = 3 };
                var seenBak = tmp.FirstSeen;
                tmp.Clear();
                var cleared = tmp.TouchCount == 0 && tmp.FeedCount == 0 && tmp.TopFoodCount == 0
                              && tmp.TopFood == "" && tmp.FirstSeen == seenBak;

                // 众数跟踪 + 名字转义往返
                var mo = new CharacterStats();
                for (int i = 0; i < 5; i++) mo.CountFeed("苹果派");
                for (int i = 0; i < 2; i++) mo.CountFeed("怪味豆");
                var topOk = mo.TopFood == "苹果派";
                var escOk = CharacterStats.Unescape(CharacterStats.Escape("a=b\\c\nd")) == "a=b\\c\nd";

                Console.WriteLine($"[TEST] 设置新增项: 气泡 9 秒重读={rereadBubble} 生效={applied} " +
                                  $"走动=关重读={rereadMove == false} 备份={bk != null}({bkFiles}个文件) 清统计={cleared}");
                Console.WriteLine($"[TEST] 年报数据: 众数跟踪={topOk}({mo.TopFood}×{mo.TopFoodCount}) 名字转义={escOk} " +
                                  $"版本={AppInfo.Version}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 设置新增项自检失败: " + e.Message);
            }

            // P11. 工作面板: 分类/收藏置顶/等级门槛/收益估算/进度条配色
            try
            {
                // 先打开面板, 走的是真实入口(建面板 + 填列表)
                ShowWorkPanel();
                var all = AllWorks();

                var workN = all.Count(w => w.Type == Work.WorkType.Work);
                var studyN = all.Count(w => w.Type == Work.WorkType.Study);
                var playN = all.Count(w => w.Type == Work.WorkType.Play);

                // 分类过滤: 切到「工作」后列表里只该有工作类
                _workFilter = Work.WorkType.Work;
                _workOnlyStar = false;
                RefreshWorkPanel();
                var rows = _workBody?.Children.Count ?? 0;
                // 行数 = 该类工作数 + 1 条脚注(空列表时是 1 条提示)
                var rowsOk = workN == 0 || rows == workN + 1;

                // 收藏置顶: 收藏最后一项, 它应该跑到第一位
                var last = all.LastOrDefault(w => w.Type == Work.WorkType.Work);
                var starTop = false;
                if (last != null)
                {
                    WorkFavorites.Toggle(last.Name);
                    RefreshWorkPanel();
                    // 核对"第一行的名字"是不是刚收藏的那项
                    starTop = FirstRowName(_workBody) == last.Name;
                    WorkFavorites.Toggle(last.Name);   // 还原
                    RefreshWorkPanel();
                }

                // 等级门槛: 造一个高等级要求的工作, 检查 StartWork 被拦下
                var blocker = new Work { Name = "自检-高等级", Type = Work.WorkType.Work, Time = 1, LevelLimit = 999 };
                var blocked = !MainDisplay.StartWork(blocker);

                // 收益估算: 文案(工作)与经验(学习)口径不同, 但同一 MoneyBase 下应为正
                var moneyW = new Work { Name = "自检-赚钱", Type = Work.WorkType.Work, MoneyBase = 10, Time = 60 };
                var studyW = new Work { Name = "自检-学习", Type = Work.WorkType.Study, MoneyBase = 80, Time = 45 };
                var estOk = EstimatePerMin(moneyW) > 0 && EstimateTotal(moneyW) > EstimatePerMin(moneyW)
                            && EstimatePerMin(studyW) > 0 && TypeUnit(moneyW) == "$" && TypeUnit(studyW) == "经验";

                // 进度条配色: 棕色工作应该把进度条染成棕色, 不是固定天蓝
                var brown = new Work { Name = "烧烤", BorderBrush = "8D93A1", Background = "413d39", Foreground = "F4F4F6" };
                WorkTimer.ApplyStyle(brown);
                var styled = WorkTimer.StyledBackground?.ToString()?.Contains("413D39", StringComparison.OrdinalIgnoreCase) == true;

                // 进行中: 开始一项工作后面板该显示「进行中」+「停止」,
                // 停掉之后该恢复成「开始」
                var runningOk = false;
                var stopRestore = false;
                var firstWork = all.FirstOrDefault(w => w.Type == Work.WorkType.Work && w.LevelLimit <= core.Save!.Level);
                if (firstWork != null)
                {
                    var startOk = MainDisplay.StartWork(firstWork);
                    RefreshWorkPanel();
                    var lineTexts = RowTexts(_workBody);
                    runningOk = startOk && lineTexts.Contains("进行中") && lineTexts.Contains("停止");
                    WorkTimer.Stop(reason: FinishWorkInfo.StopReason.MenualStop);
                    RefreshWorkPanel();
                    var after = RowTexts(_workBody);
                    stopRestore = !after.Contains("进行中") && after.Contains("开始");
                }

                // 关掉面板, 别让后续步骤被挡
                if (_workPanel != null)
                    _workPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 工作面板: 工作={workN} 学习={studyN} 玩耍={playN} " +
                                  $"分类行数={rows}(应 {workN + 1}) 只含该类={rowsOk} " +
                                  $"收藏置顶={starTop} 高等级拦截={blocked} 收益估算={estOk} 进度条配色={styled} " +
                                  $"进行中行={runningOk} 停止后复原={stopRestore}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 工作面板自检失败: " + e.Message);
            }

            // P12. 角色面板(v1.4.0 重做): 页签切换 / 属性条 / 低值告警 / 增量刷新
            try
            {
                ShowCharacterPanel();
                var meterCount = _charMeters.Count;          // 7 条属性
                var tabCount = _charTabs.Count;              // 3 个页签

                SwitchTab(1);
                var recShown = _charPageRecord is { IsVisible: true } && _charPageOverview is { IsVisible: false };
                SwitchTab(2);
                var yearShown = _charPageYear is { IsVisible: true } && _charPageRecord is { IsVisible: false };
                SwitchTab(0);
                var backOk = _charPageOverview is { IsVisible: true } && _charPageYear is { IsVisible: false };

                // 低值告警只对生存类属性生效; 经验和好感从 0 起步是正常状态
                var save = MainDisplay.Core!.Save!;
                var strengthBak = save.Strength;
                var strengthMeter = _charMeters.FirstOrDefault(m => m.Name == "体力");
                save.Strength = 1;
                UpdateCharacterPanel();
                var lowRed = FillColor(strengthMeter)?.Contains("EF5350", StringComparison.OrdinalIgnoreCase) == true;
                save.Strength = strengthBak;
                UpdateCharacterPanel();
                var backNormal = FillColor(strengthMeter)?.Contains("FF8A65", StringComparison.OrdinalIgnoreCase) == true;
                var expNoWarn = _charMeters.FirstOrDefault(m => m.Name == "经验")?.Value.Foreground?.ToString()
                    ?.Contains("EF5350", StringComparison.OrdinalIgnoreCase) != true;

                // 每秒刷新必须"只改值不重建": 否则滚动位置会跳回顶部、悬停状态被抹掉
                var fillBefore = _charMeters[0].Fill;
                var rowsBefore = _charPageOverview?.Children.Count ?? 0;
                UpdateCharacterPanel();
                var noRebuild = ReferenceEquals(fillBefore, _charMeters[0].Fill)
                                && rowsBefore == (_charPageOverview?.Children.Count ?? 0);

                if (_charPanel != null)
                    _charPanel.IsVisible = false;
                UpdateClickThrough();

                Console.WriteLine($"[TEST] 角色面板: 属性条={meterCount}(应 7) 页签={tabCount}(应 3) " +
                                  $"切记录={recShown} 切年报={yearShown} 回概览={backOk} " +
                                  $"低值转红={lowRed} 恢复={backNormal} 经验不误报={expNoWarn} 增量刷新={noRebuild}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[TEST] 角色面板自检失败: " + e.Message);
            }
        }

        /// <summary>取自检用的属性条填充色(拿不到时返回 null)</summary>
        private static string? FillColor(MainWindow.CharMeter? meter)
            => meter?.Fill.Background?.ToString();

        /// <summary>
        /// 把列表里一行里的所有文本捞出来(自检核对"进行中/停止/开始"用)。
        ///
        /// v1.5.0 起行内结构变成「Border 卡片 → Grid → StackPanel/Border(胶囊) → TextBlock」,
        /// 徽章又藏在名字行里面, 只扫两层会漏。这里直接递归整棵子树, 以后改布局也不用再动它。
        /// </summary>
        private static List<string> RowTexts(StackPanel? body)
        {
            var list = new List<string>();
            if (body == null)
                return list;
            foreach (var child in body.Children)
            {
                if (child is Border row)   // 列表行都是 Border 卡片, 脚注/提示条跳过
                    CollectTexts(row, list);
            }
            return list;
        }

        private static void CollectTexts(Control control, List<string> list)
        {
            switch (control)
            {
                case TextBlock t:
                    list.Add(t.Text ?? "");
                    break;
                case Button b when b.Content is string s:
                    list.Add(s);
                    break;
                case Border bd when bd.Child != null:
                    CollectTexts(bd.Child, list);
                    break;
                case StackPanel sp:
                    foreach (var c in sp.Children)
                        CollectTexts(c, list);
                    break;
                case Grid g:
                    foreach (var c in g.Children)
                        CollectTexts(c, list);
                    break;
                case Panel p:
                    foreach (var c in p.Children)
                        CollectTexts(c, list);
                    break;
                case ContentControl cc when cc.Content is Control inner:
                    CollectTexts(inner, list);
                    break;
            }
        }

        /// <summary>取列表第一行显示的名字(自检用: 核对收藏置顶)</summary>
        private static string? FirstRowName(StackPanel? body)
        {
            if (body?.Children.FirstOrDefault() is not Border row || row.Child is not Grid g)
                return null;
            foreach (var child in g.Children)
            {
                if (child is StackPanel sp && Grid.GetColumn(sp) == 1)
                {
                    var line = sp.Children.OfType<StackPanel>().FirstOrDefault();
                    return line?.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
                }
            }
            return null;
        }

        /// <summary>
        /// 当前窗口所在的显示器
        /// </summary>
        private Screen? _screen;

        /// <summary>
        /// 设置窗口尺寸与位置
        ///
        /// 这里的策略是「窗口跟着宠物走一块屏」, 而不是一个窗口盖住所有屏:
        /// XWayland + 混合缩放(本机 2160x1440@1.0 与 2560x1440@1.25)下,
        /// WM 会把请求的 4720x1440 窗口钳成单屏尺寸(实测 2160x1358),
        /// 于是"逻辑坐标 4720 宽"和"实际窗口 2160 宽"对不上, 宠物直接跑到窗口外面看不见,
        /// 拖拽也只能在窗口覆盖的那一块里动 —— 这就是"只能在副屏拖拽"的根因。
        /// 现在窗口尺寸始终等于宠物所在那块屏, 拖到屏幕边缘时由 TrySwitchScreen 整体换屏。
        /// </summary>
        private void SetupWindowBounds()
        {
            Screen? target = null;
            try
            {
                var all = Screens?.All;
                if (all != null)
                {
                    foreach (var s in all)
                    {
                        var b = s.Bounds;
                        Console.WriteLine($"[VPet] 显示器: {b.X},{b.Y} {b.Width}x{b.Height} 缩放={s.Scaling:0.##} 主屏={ReferenceEquals(s, Screens?.Primary)}");
                    }
                }
                target = Screens?.Primary ?? (all != null && all.Count > 0 ? all[0] : null);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 获取屏幕信息失败, 用默认全屏: " + e.Message);
            }

            // Wayland 下无法保证全屏透明, 退化为"宠物大小的贴底小窗口",
            // 避免整屏出现一个不透明的大方块
            if (_sessionType == LinuxWindowNative.SessionType.Wayland)
            {
                var petW = PetDesignSize * ZoomRatio;
                var petH = PetDesignSize * ZoomRatio;
                MainDisplay.Width = petW;
                MainDisplay.Height = petH;
                _viewWidth = petW;
                _viewHeight = petH;
                Width = petW;
                Height = petH;
                var wb = target?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
                Position = new PixelPoint(wb.X + (wb.Width - (int)petW) / 2, wb.Y + wb.Height - (int)petH);
                // Wayland 下透明通常不被支持, 让自身背景彻底透明,
                // 由合成器决定最终效果, 避免出现不透明的遮挡块
                Background = null;
                Root.Background = null;
                SetPetPosition(0, 0);
                return;
            }

            if (target != null)
                ApplyScreen(target);
            else
            {
                Position = new PixelPoint(0, 0);
                Width = 1920;
                Height = 1080;
                _viewWidth = 1920;
                _viewHeight = 1080;
            }

            // 宠物尺寸随缩放倍率
            var petSize = PetDesignSize * ZoomRatio;
            MainDisplay.Width = petSize;
            MainDisplay.Height = petSize;

            // 首次定位: 当前屏底部居中
            SyncViewSize();
            SetPetPosition((_viewWidth - petSize) / 2, _viewHeight - petSize);

            // WM 可能把窗口尺寸改成别的值(钳制/加装饰), 布局完了以实际 Bounds 为准,
            // 否则坐标空间和窗口对不上, 宠物会跑到窗口外面
            LayoutUpdated += (_, _) =>
            {
                if (_sessionType == LinuxWindowNative.SessionType.Wayland)
                    return;
                var (w0, h0) = (_viewWidth, _viewHeight);
                SyncViewSize();
                if (Math.Abs(w0 - _viewWidth) > 0.5 || Math.Abs(h0 - _viewHeight) > 0.5)
                    // 拖动换屏的瞬间宠物允许暂时超出窗口(FreeClamp), 这里不能把它夹回来
                    SetPetPosition(_petLeft, _petTop, clamp: !FreeClamp);
            };
        }

        /// <summary>
        /// 把窗口对准某块显示器(尺寸 = 该屏逻辑尺寸, 位置 = 该屏左上角)
        /// </summary>
        private void ApplyScreen(Screen s)
        {
            var b = s.Bounds;
            var scale = s.Scaling > 0 ? s.Scaling : 1.0;
            var viewW = b.Width / scale;
            var viewH = b.Height / scale;
            _screen = s;
            _viewWidth = viewW;
            _viewHeight = viewH;

            // 搬家 + 改尺寸必须一次原子请求。
            // 两段式(Position 然后 Width/Height)会在中间渲染一帧「窗口已经搬到新屏原点、
            // 尺寸还是小窗的 508」——宠物画布被摆在小窗坐标系里，屏幕上就是左上角闪一下。
            var atomic = LinuxWindowNative.MoveResizeWindow(_platformHandle, b.X, b.Y, b.Width, b.Height);

            Position = new PixelPoint(b.X, b.Y);
            Width = viewW;
            Height = viewH;
            if (!atomic && _platformHandle != IntPtr.Zero)
                Console.WriteLine("[VPet] 原子 MoveResize 不可用, 退回两段式(可能闪一帧)");
            Console.WriteLine($"[VPet] 窗口对准显示器: 物理({b.X},{b.Y}) {b.Width}x{b.Height} " +
                              $"逻辑 {Width:0}x{Height:0} 缩放={scale:0.##}");
        }

        /// <summary>
        /// 用窗口实际尺寸校正视口尺寸(逻辑像素)
        /// </summary>
        private void SyncViewSize()
        {
            // 换屏事务还没完成时 Bounds 仍是旧屏尺寸, 采信它会把 _viewWidth/_viewHeight
            // 改回旧值 —— 宠物随即按旧屏宽度摆放, 落到窄屏上直接飞出右边缘,
            // 看着就是"闪到最右边"; 边界距离也跟着错, 又触发一次换屏, 于是又闪回来。
            // 这里保留 ApplyScreen 设的目标尺寸, 等窗口真的搬过去再校正。
            if (!IsSwitchSettled())
                return;

            var w = Bounds.Width;
            var h = Bounds.Height;
            if (w > 1 && Math.Abs(w - _viewWidth) > 0.5)
                _viewWidth = w;
            if (h > 1 && Math.Abs(h - _viewHeight) > 0.5)
                _viewHeight = h;
        }

        /// <summary>
        /// 拖动到窗口外面时, 把整个窗口换到指针所在的显示器上。
        /// 返回 true 表示发生了换屏(调用方需要重新取一次指针局部坐标)
        /// </summary>
        internal bool TrySwitchScreen(Avalonia.Point localPoint)
        {
            var screens = Screens;
            if (screens == null || _screen == null)
                return false;

            // 与按方向换屏共用同一道冷却。
            // 这条路径原先没限流, 而换屏后窗口坐标系要等 WM 回 ConfigureNotify 才更新,
            // 中间几帧的局部坐标仍是旧基准 → 又被判成"指针出窗" → 再换一次,
            // 于是两块屏之间连续横跳(实测 3 秒切了 10 次)。冷却把这条自激回路掐断。
            if (Environment.TickCount64 - _lastSwitchTicks < SwitchCooldownMs)
                return false;

            if (_debugDrag)
                Console.WriteLine($"[DBG] TrySwitch: local=({localPoint.X:0},{localPoint.Y:0})");

            // 注意: 不能用 Position 反推坐标 —— Avalonia 的窗口位置/尺寸是异步生效的,
            // 刚设完立刻读回来还是旧值。一律以 _screen.Bounds(物理矩形) 为基准。
            var cur = _screen.Bounds;
            var scale = _screen.Scaling > 0 ? _screen.Scaling : 1.0;
            var sp = new PixelPoint(
                (int)(cur.X + localPoint.X * scale),
                (int)(cur.Y + localPoint.Y * scale));

            // 自己按物理矩形判断落在哪块屏, 不用 ScreenFromPoint(窗口还没真正移动时它可能返回旧屏)
            Screen? target = null;
            foreach (var s in screens.All)
            {
                var b = s.Bounds;
                if (b.Width <= 0 || b.Height <= 0)
                    continue;
                if (sp.X >= b.X && sp.X < b.X + b.Width && sp.Y >= b.Y && sp.Y < b.Y + b.Height)
                {
                    target = s;
                    break;
                }
            }
            if (target == null || ReferenceEquals(target, _screen))
                return false;

            return SwitchTo(target, localPoint);
        }

        /// <summary>
        /// 按拖动方向换屏: 宠物已经顶到当前屏的边缘, 且还在继续往外拖时调用。
        ///
        /// 为什么不只靠"指针出窗"的 TrySwitchScreen:
        /// 窗口的 XShape 输入区域只覆盖宠物本体, 指针一旦离开宠物(宠物被夹在屏幕边缘后必然发生),
        /// X 服务器就不再往这个窗口投递移动事件, 出窗检测根本没有机会执行。
        /// 所以在宠物顶到边缘的这一刻就主动换屏, 不依赖后续事件。
        /// </summary>
        internal bool TrySwitchScreenByDirection(double dx, double dy, Avalonia.Point? cursorLocal = null)
        {
            var screens = Screens;
            if (screens == null || _screen == null)
                return false;

            // 冷却: 换屏后宠物正好贴在新屏的边界上, 同一方向的抖动会立刻再换回去,
            // 表现为"在两个屏之间来回跳"
            if (Environment.TickCount64 - _lastSwitchTicks < SwitchCooldownMs)
                return false;

            var cur = _screen.Bounds;
            var scale = _screen.Scaling > 0 ? _screen.Scaling : 1.0;
            var px = cur.X + _petLeft * scale;
            var py = cur.Y + _petTop * scale;
            var pw = MainDisplay.Width * scale;
            var ph = MainDisplay.Height * scale;

            Screen? target = null;
            foreach (var s in screens.All)
            {
                if (ReferenceEquals(s, _screen))
                    continue;
                var b = s.Bounds;
                if (b.Width <= 0 || b.Height <= 0)
                    continue;

                // 相邻判定: 左右相邻要求竖直方向有重叠, 上下相邻要求水平方向有重叠
                var vOverlap = py + ph > b.Y && py < b.Y + b.Height;
                var hOverlap = px + pw > b.X && px < b.X + b.Width;

                if (dx > 0 && vOverlap && Math.Abs(b.X - (cur.X + cur.Width)) <= 2)
                    target = s;   // 右边那块
                else if (dx < 0 && vOverlap && Math.Abs(b.X + b.Width - cur.X) <= 2)
                    target = s;   // 左边那块
                else if (dy > 0 && hOverlap && Math.Abs(b.Y - (cur.Y + cur.Height)) <= 2)
                    target = s;   // 下面那块
                else if (dy < 0 && hOverlap && Math.Abs(b.Y + b.Height - cur.Y) <= 2)
                    target = s;   // 上面那块

                if (target != null)
                    break;
            }

            if (target == null)
                return false;

            return SwitchTo(target, cursorLocal);
        }

        /// <summary>
        /// 把窗口搬到目标屏, 并让宠物在指针下保持连续(按抓取点换算)
        /// </summary>
        /// <param name="cursorLocal">
        /// 换屏前指针在当前窗口里的逻辑坐标。有值时按"指针在宠物上的相对位置不变"摆放,
        /// 宠物会自然地滑进新屏; 为 null 时退化为保持宠物物理位置不变
        /// </param>
        private bool SwitchTo(Screen target, Avalonia.Point? cursorLocal = null)
        {
            _lastSwitchTicks = Environment.TickCount64;
            // 窗口搬家在 X11 上是异步的: Position 属性要等 WM 回 ConfigureNotify 才更新。
            // 这段时间里 e.GetPosition(窗口) 还是旧基准, 不能拿来定位宠物。
            _switchFromPos = Position;
            _switchTargetPos = new PixelPoint(target.Bounds.X, target.Bounds.Y);
            _switchSettleDeadline = Environment.TickCount64 + SwitchSettleMs;

            var cur = _screen?.Bounds ?? default;
            var scale = _screen != null && _screen.Scaling > 0 ? _screen.Scaling : 1.0;
            var px = cur.X + _petLeft * scale;
            var py = cur.Y + _petTop * scale;

            ApplyScreen(target);
            SyncViewSize();

            var ns = target.Scaling > 0 ? target.Scaling : 1.0;
            _switchTargetSize = new Avalonia.Size(target.Bounds.Width / ns, target.Bounds.Height / ns);
            Console.WriteLine($"[VPet] 拖动换屏: → ({target.Bounds.X},{target.Bounds.Y}) " +
                              $"宠物物理({px:0},{py:0})");

            // 注意: 不在这里改 MainDisplay 的宽高去"补偿缩放差"。
            // 宠物内部是绝对坐标布局, 改容器尺寸会让标签/面板/触摸区域一起错位。
            // 缩放不同的屏幕上宠物物理尺寸会有 25% 差异(1.0x vs 1.25x), 属于可接受的观感差异。

            double targetLeft, targetTop;
            if (cursorLocal is { } c)
            {
                // 指针在宠物内的相对位置(物理像素), 换屏后保持不变 —— 宠物跟着指针走。
                var grabX = (c.X - _petLeft) * scale;
                var grabY = (c.Y - _petTop) * scale;
                var cursorPhysX = cur.X + c.X * scale;
                var cursorPhysY = cur.Y + c.Y * scale;
                targetLeft = (cursorPhysX - target.Bounds.X - grabX) / ns;
                targetTop = (cursorPhysY - target.Bounds.Y - grabY) / ns;
            }
            else
            {
                targetLeft = (px - target.Bounds.X) / ns;
                targetTop = (py - target.Bounds.Y) / ns;
            }

            // 落点必须夹在目标屏的可见范围内。
            // 允许负坐标的话, 宠物会大半个落在窗口外面(窗口只覆盖目标屏), 被裁成"闪没了";
            // 下一帧边缘检测又把宠物当成"顶在边上"往回换屏 —— 这是来回横跳的直接来源。
            var petW = MainDisplay.Width;
            var petH = MainDisplay.Height;
            var maxLeft = Math.Max(0, target.Bounds.Width / ns - petW);
            var maxTop = Math.Max(0, target.Bounds.Height / ns - petH);
            targetLeft = Math.Clamp(targetLeft, 0, maxLeft);
            targetTop = Math.Clamp(targetTop, 0, maxTop);

            SetPetPosition(targetLeft, targetTop, clamp: false);

            // 延迟回读真实窗口位置, 验证 WM 是否真的执行了这次搬移
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[DBG] 换屏后回读: Position=({Position.X},{Position.Y}) " +
                                  $"Bounds={Bounds.Width:0}x{Bounds.Height:0} " +
                                  $"视口={_viewWidth:0}x{_viewHeight:0}");
            }, Avalonia.Threading.DispatcherPriority.Background);

            return true;
        }

        private readonly bool _debugDrag =
            Environment.GetEnvironmentVariable("VPET_DEBUG_DRAG") == "1";

        /// <summary>诊断: 关闭 XShape 输入区域裁剪, 整窗可接收鼠标事件</summary>
        private readonly bool _noInputShape =
            Environment.GetEnvironmentVariable("VPET_NO_INPUT_SHAPE") == "1";

        /// <summary>上次换屏的时刻, 用于防抖</summary>
        private long _lastSwitchTicks;

        /// <summary>
        /// 换屏最小间隔。两只屏相邻时宠物换屏后正好贴在新屏边缘, 任何一点坐标
        /// 抖动都会让它"又贴边了", 间隔太短就会连续横跳
        /// </summary>
        private const long SwitchCooldownMs = 600;

        /// <summary>
        /// 换屏后等窗口真正搬到位的最长时间。超时后强制恢复拖动,
        /// 免得某些 WM 不回 ConfigureNotify 时拖动被卡死
        /// </summary>
        private const long SwitchSettleMs = 150;

        private PixelPoint _switchFromPos;
        private PixelPoint _switchTargetPos;
        private Avalonia.Size _switchTargetSize;
        private long _switchSettleDeadline;

        /// <summary>
        /// 换屏后窗口是否已经搬到目标屏。
        /// X11 上 Move/Resize 只是请求, Position/Bounds 要等 WM 回 ConfigureNotify 才更新;
        /// 在那之前它们还是旧屏的值, 拿它们算宠物位置/边界距离必然错位。
        /// 判据: 位置已经离开换屏前的旧值, 且尺寸已经和目标屏对上。
        /// </summary>
        internal bool IsSwitchSettled()
        {
            if (Environment.TickCount64 > _switchSettleDeadline)
                return true;   // WM 不回 ConfigureNotify 时的兜底, 别把拖动卡死
            if (Position.X == _switchFromPos.X && Position.Y == _switchFromPos.Y)
                return false;  // 窗口还没搬走
            return Math.Abs(Bounds.Width - _switchTargetSize.Width) < 1.5
                && Math.Abs(Bounds.Height - _switchTargetSize.Height) < 1.5;
        }

        /// <summary>
        /// 登记一次"窗口几何变更中"的事务：在 Bounds 真的变成目标尺寸之前，
        /// SyncViewSize 不要采信它。
        ///
        /// 小窗口拖拽的「缩窗」和「还原整屏」都是异步的（X11 上 Move/Resize 只是请求，
        /// 要等 WM 回 ConfigureNotify）。少了这层保护，还原瞬间 LayoutUpdated 会读到
        /// 还停在 508x508 的 Bounds，把 _viewWidth 改小 → SetPetPosition 把宠物
        /// 夹进那个小视口 → 宠物瞬移到屏幕左上角且回不来（实测复现）。
        /// </summary>
        internal void ExpectWindowGeometry(Avalonia.Size targetLogicalSize, long settleMs = SwitchSettleMs)
        {
            _switchFromPos = Position;
            _switchTargetSize = targetLogicalSize;
            _switchSettleDeadline = Environment.TickCount64 + settleMs;
        }

        /// <summary>
        /// 指针是否已经越出窗口边界(即真的移动到别处去了)。
        /// 只凭"宠物贴边"就换屏是不行的: 那时指针还在本屏内(在宠物身上),
        /// 换屏后宠物按抓取偏移换算会大半落在本屏 → 被新窗口裁掉, 看着就是"闪没了"
        /// </summary>
        internal bool PointerOutside(Avalonia.Point p, double margin)
        {
            // 用 _viewWidth/_viewHeight 而不是 Bounds: 换屏后 Bounds 还是旧屏尺寸,
            // 会让"越界"判断在错误的宽度上做
            if (_viewWidth <= 1 || _viewHeight <= 1)
                return false;
            return p.X < -margin || p.Y < -margin
                || p.X > _viewWidth + margin || p.Y > _viewHeight + margin;
        }

        /// <summary>当前视口逻辑尺寸(以本程序设定的目标值为准, 不读滞后的 Bounds)</summary>
        internal double ViewWidth => _viewWidth;
        internal double ViewHeight => _viewHeight;

        /// <summary>
        /// 拖动中允许宠物暂时超出窗口边界(跨屏换屏的连续性需要)。
        /// 松开鼠标后必须复位并夹回屏内。
        /// </summary>
        internal bool FreeClamp { get; set; }

        /// <summary>
        /// 松开鼠标时兜底: 指针已经落在另一块显示器上, 就把窗口搬过去、宠物放到指针附近。
        /// 覆盖"拖动过程中换屏没触发, 但手已经拖到另一块屏"的情况。
        /// </summary>
        internal void FinishDragAt(Avalonia.Point localPoint)
        {
            var screens = Screens;
            if (screens == null || _screen == null)
                return;

            var cur = _screen.Bounds;
            var scale = _screen.Scaling > 0 ? _screen.Scaling : 1.0;
            var sp = new PixelPoint(
                (int)(cur.X + localPoint.X * scale),
                (int)(cur.Y + localPoint.Y * scale));

            Screen? target = null;
            foreach (var s in screens.All)
            {
                var b = s.Bounds;
                if (b.Width <= 0 || b.Height <= 0)
                    continue;
                if (sp.X >= b.X && sp.X < b.X + b.Width && sp.Y >= b.Y && sp.Y < b.Y + b.Height)
                {
                    target = s;
                    break;
                }
            }
            if (target == null || ReferenceEquals(target, _screen))
                return;

            _lastSwitchTicks = Environment.TickCount64;
            _switchFromPos = Position;
            _switchTargetPos = new PixelPoint(target.Bounds.X, target.Bounds.Y);
            _switchSettleDeadline = Environment.TickCount64 + SwitchSettleMs;
            var ns = target.Scaling > 0 ? target.Scaling : 1.0;
            _switchTargetSize = new Avalonia.Size(target.Bounds.Width / ns, target.Bounds.Height / ns);
            ApplyScreen(target);
            SyncViewSize();

            Console.WriteLine($"[VPet] 松手换屏: → ({target.Bounds.X},{target.Bounds.Y})");
            SetPetPosition((sp.X - target.Bounds.X) / ns - MainDisplay.Width / 2,
                           (sp.Y - target.Bounds.Y) / ns - MainDisplay.Height / 2);
        }

        /// <summary>
        /// 设置宠物位置并夹在屏幕内
        /// </summary>
        /// <param name="clamp">false 时允许超出窗口(拖动换屏的瞬间需要, 否则宠物会整只弹到新屏最左)</param>
        private void SetPetPosition(double left, double top, bool clamp = true)
        {
            var petW = MainDisplay.Width;
            var petH = MainDisplay.Height;

            if (clamp)
            {
                var maxLeft = Math.Max(0, _viewWidth - petW);
                var maxTop = Math.Max(0, _viewHeight - petH);
                left = Math.Max(0, Math.Min(maxLeft, left));
                top = Math.Max(0, Math.Min(maxTop, top));
            }

            _petLeft = left;
            _petTop = top;

            Canvas.SetLeft(MainDisplay, _petLeft);
            Canvas.SetTop(MainDisplay, _petTop);

            LayoutPetUI();
            UpdateClickThrough();
        }

        /// <summary>
        /// 摆放气泡、计时器、面板等跟随宠物的 UI
        ///
        /// 拖动时这个方法每帧都会被调用一次。对不可见的控件设置 Canvas 坐标
        /// 会让布局系统白白 measure/arrange 一遍(面板层级不浅), 所以先判可见性。
        /// </summary>
        private void LayoutPetUI()
        {
            var petW = MainDisplay.Width;

            // 对话气泡: 宠物正上方
            if (MessageBar.IsVisible)
            {
                Canvas.SetLeft(MessageBar, _petLeft + (petW - MessageBar.Width) / 2);
                Canvas.SetTop(MessageBar, Math.Max(0, _petTop - 150));
            }

            // 工作计时器: 气泡右侧, 超出屏幕时改放左侧
            if (WorkTimer.IsVisible)
            {
                var timerLeft = _petLeft + petW + 10;
                if (timerLeft + WorkTimer.Width > _viewWidth)
                    timerLeft = Math.Max(0, _petLeft - WorkTimer.Width - 10);
                Canvas.SetLeft(WorkTimer, timerLeft);
                Canvas.SetTop(WorkTimer, Math.Max(0, _petTop - 60));
            }
        }

        #endregion

        #region 鼠标穿透

        private void InitializeClickThrough()
        {
            try
            {
                _platformHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            }
            catch
            {
                _platformHandle = IntPtr.Zero;
            }

            if (_sessionType == LinuxWindowNative.SessionType.Wayland)
            {
                // Wayland 不允许客户端改写输入区域, 只能关闭穿透
                _clickThroughEnabled = false;
                Console.WriteLine("[VPet] 检测到 Wayland 会话, 鼠标穿透不可用(协议限制), 已降级");
                return;
            }

            if (!LinuxWindowNative.IsClickThroughAvailable)
            {
                _clickThroughEnabled = false;
                Console.WriteLine("[VPet] XShape 不可用(缺少 libXext 或非 X11), 鼠标穿透已关闭");
                return;
            }

            UpdateClickThrough();
        }

        /// <summary>
        /// 上一次提交给 X11 的输入区域签名
        /// SetInputRegions 会触发控件重排 → LayoutUpdated → 再次调本方法,
        /// 不加去重就变成死循环, 主线程被占满后鼠标点击全部失效(菜单"看得见点不动")
        /// </summary>
        private string _lastRegionKey = "";

        private bool _updatingInput;

        /// <summary>
        /// 拖动期间是否已把输入区域暂存为整窗(见 BeginDragShape)
        /// </summary>
        private bool _dragShapeActive;

        /// <summary>
        /// 拖动开始: 把输入区域一次性放大到整窗, 之后整个拖动过程不再逐帧提交。
        ///
        /// 为什么必须这么做:
        /// 拖动时宠物位置每帧都在变 → UpdateClickThrough 的 key 每帧都不同 →
        /// 每帧一次 XShapeCombineRectangles + XFlush。XWayland 下每次 shape 变更都要
        /// 重算区域并让合成器重建 wl_surface 的 input region, 请求堆积后 UI 线程
        /// 卡在 socket 写上, 表现就是"宠物明显落后鼠标, 拖一下才跟一下"。
        ///
        /// 放大到整窗之后:
        ///   1. 指针在整个窗口内移动都有 MotionNotify, 彻底摆脱"离开宠物就收不到事件";
        ///   2. X 请求从每秒上百次降到 0 次。
        /// 松手时 EndDragShape 再按宠物最终位置恢复精确区域(一次)。
        /// </summary>
        internal void BeginDragShape()
        {
            if (_dragShapeActive || _sessionType != LinuxWindowNative.SessionType.X11)
                return;
            if (_platformHandle == IntPtr.Zero || _noInputShape)
                return;

            _dragShapeActive = true;
            _lastRegionKey = "";
            // 尺寸给足即可, shape 会被自动裁剪到窗口边界
            LinuxWindowNative.ResetInputRegion(_platformHandle);
        }

        /// <summary>
        /// 拖动结束: 撤销整窗输入区域, 按宠物最终位置重新下发一次精确区域
        /// </summary>
        internal void EndDragShape()
        {
            if (!_dragShapeActive)
                return;
            _dragShapeActive = false;
            _lastRegionKey = "";   // key 清空 = 强制重新提交
            UpdateClickThrough();
        }

        #region 小窗口拖拽（窗口缩到宠物大小后跟着指针走）

        /// <summary>小窗口拖拽时四周留的逻辑像素</summary>
        internal const double SmallDragPad = 4;

        private bool _smallDrag;
        private PixelPoint _smallDragSavedPos;
        private double _smallDragSavedWidth;
        private double _smallDragSavedHeight;
        private double _smallDragSavedPetLeft;
        private double _smallDragSavedPetTop;
        private Screen? _smallDragSavedScreen;

        /// <summary>
        /// 最近一次"请求"的窗口位置（X11 的 Position 是异步回读，会慢一拍，落点要用这个）
        /// </summary>
        private PixelPoint _smallDragLastPos;

        /// <summary>
        /// 小窗口拖拽时请求的窗口位置（按下瞬间的值，供上层换算抓取偏移）
        /// </summary>
        internal PixelPoint SmallDragOrigin => _smallDragLastPos;

        /// <summary>
        /// 是否启用小窗口拖拽（默认开；VPET_SMALLWIN=0 可退回原来的"整屏窗口内移动"）
        /// </summary>
        /// <summary>
        /// 是否启用「缩窗拖动」（窗口缩到宠物大小、整窗跟指针）。
        ///
        /// 默认关闭。原因：每次缩窗/还原都要把 2160x1440 的透明窗口表面重建一遍，
        /// ARM 上实测要 200~750ms，这期间窗口显示的是旧表面按新尺寸裁出来的切片——
        /// 表现为拖动开始时宠物闪一下、松手后宠物消失半秒。而全屏窗口那条路径
        /// 在 PointerPressed 里已经把输入区域临时放大到整窗（BeginDragShape），
        /// 拖动期间不再逐帧下发 XShape，跟手程度本来就没问题。
        ///
        /// 需要时可用 VPET_SMALLWIN=1 打开（已做成原子 MoveResize，不会再闪到左上角）。
        /// </summary>
        internal static bool SmallDragEnabled =>
            string.Equals(Environment.GetEnvironmentVariable("VPET_SMALLWIN"), "1", StringComparison.Ordinal);

        internal bool SmallDragActive => _smallDrag;

        /// <summary>
        /// 进入小窗口拖拽：窗口缩到「宠物 + 四周 4px」，并让宠物在屏幕上原地不动
        ///
        /// 为什么要缩窗口：原实现窗口铺满整块屏（2160x1440），拖动时宠物只是在窗口里挪位置，
        /// 软件渲染下每帧都要重绘/合成一大片透明表面，宠物明显落后指针。
        /// 缩成宠物大小后，拖动改为整窗移动（一次 MoveWindow），画面内容不变，自然跟手。
        /// </summary>
        internal bool BeginSmallWindowDrag()
        {
            if (_smallDrag)
                return true;
            if (!SmallDragEnabled)
                return false;
            if (_sessionType != LinuxWindowNative.SessionType.X11)
                return false;

            var petW = MainDisplay.Width;
            var petH = MainDisplay.Height;
            if (petW <= 0 || petH <= 0)
                return false;

            _smallDragSavedPos = Position;
            _smallDragSavedWidth = Width;
            _smallDragSavedHeight = Height;
            _smallDragSavedPetLeft = _petLeft;
            _smallDragSavedPetTop = _petTop;
            _smallDragSavedScreen = _screen;

            var scale = RenderScaling > 0 ? RenderScaling : 1.0;
            var petScreenX = _smallDragSavedPos.X + (int)Math.Round(_petLeft * scale);
            var petScreenY = _smallDragSavedPos.Y + (int)Math.Round(_petTop * scale);

            // 缩窗也是异步的：在 WM 真的把窗口改小之前，Bounds 还是整屏的旧值。
            // 这期间 LayoutUpdated → SyncViewSize 会拿旧 Bounds 覆盖 _viewWidth，
            // 顺手把宠物夹到"旧视口"的角落。先挂一次几何事务挡住这次校正。
            var winW = petW + SmallDragPad * 2;
            var winH = petH + SmallDragPad * 2;
            var targetPos = new PixelPoint(
                petScreenX - (int)Math.Round(SmallDragPad * scale),
                petScreenY - (int)Math.Round(SmallDragPad * scale));
            ExpectWindowGeometry(new Avalonia.Size(winW, winH));

            _viewWidth = winW;
            _viewHeight = winH;
            _petLeft = SmallDragPad;
            _petTop = SmallDragPad;
            Canvas.SetLeft(MainDisplay, _petLeft);
            Canvas.SetTop(MainDisplay, _petTop);
            _smallDragLastPos = targetPos;

            // 【必须原子】位置和尺寸一次请求改完（XMoveResizeWindow）。
            // 用 Avalonia 的 Width/Height + Position 是两次独立请求，中间会渲染一帧
            // 「窗口已缩成 508×508、原点还在原处」——宠物画布在 (pad,pad)，屏幕上就是
            // 闪到左上角（用户实测："拖动会往左上角闪一下"）。
            var physW = (int)Math.Round(winW * scale);
            var physH = (int)Math.Round(winH * scale);
            var atomic = LinuxWindowNative.MoveResizeWindow(_platformHandle, targetPos.X, targetPos.Y, physW, physH);

            // Avalonia 侧只做状态同步：值已和原生一致，setter 发出的请求是等值操作，
            // 不会再产生一次可见的几何跳变。原子请求不可用时退回原来的路径（会闪，但仍然能用）。
            Width = winW;
            Height = winH;
            Position = targetPos;
            if (!atomic && _debugDrag)
                Console.WriteLine("[DBG] 原子 MoveResize 不可用, 退回 Avalonia 两段式(会闪一帧)");

            // resize 之后 X 表面的内容是旧的，只会按新尺寸裁一刀；在 Avalonia 重绘之前，
            // 窗口里看到的是"旧画面在新位置的切片"（实测宠物只剩一个脑袋，约 3 帧）。
            // 主动催一次布局 + 重绘，把这段空窗压到最短。
            MainDisplay.InvalidateMeasure();
            MainDisplay.InvalidateVisual();
            InvalidateVisual();

            _smallDrag = true;
            return true;
        }

        /// <summary>
        /// 把窗口整体挪到指定物理像素位置（夹在虚拟桌面范围内，至少留一只宠物在屏内）
        /// </summary>
        internal void MoveWindowPhysical(int x, int y)
        {
            var all = Screens?.All;
            if (all != null && all.Count > 0)
            {
                var minX = int.MaxValue;
                var minY = int.MaxValue;
                var maxX = int.MinValue;
                var maxY = int.MinValue;
                foreach (var s in all)
                {
                    var b = s.Bounds;
                    minX = Math.Min(minX, b.X);
                    minY = Math.Min(minY, b.Y);
                    maxX = Math.Max(maxX, b.X + b.Width);
                    maxY = Math.Max(maxY, b.Y + b.Height);
                }
                var scale = RenderScaling > 0 ? RenderScaling : 1.0;
                var winW = (int)Math.Round(Width * scale);
                var winH = (int)Math.Round(Height * scale);
                const int keep = 60; // 至少留这么多像素在屏内，宠物不至于完全拖丢
                x = Math.Clamp(x, minX - winW + keep, maxX - keep);
                y = Math.Clamp(y, minY - winH + keep, maxY - keep);
            }
            _smallDragLastPos = new PixelPoint(x, y);
            Position = _smallDragLastPos;
        }

        /// <summary>
        /// 结束小窗口拖拽：窗口还原成落点所在那块屏，并把宠物摆到落点
        /// </summary>
        internal void EndSmallWindowDrag()
        {
            if (!_smallDrag)
                return;
            _smallDrag = false;

            var scale = RenderScaling > 0 ? RenderScaling : 1.0;
            // 用请求值而不是 Position：X11 上刚发出去的移动要等 ConfigureNotify 才回读，会慢一拍
            var petScreenX = _smallDragLastPos.X + (int)Math.Round(SmallDragPad * scale);
            var petScreenY = _smallDragLastPos.Y + (int)Math.Round(SmallDragPad * scale);
            var petW = MainDisplay.Width;
            var petH = MainDisplay.Height;

            // 落点所在的屏：取【宠物矩形与各屏重叠面积】最大的那块。
            // 不能用"宠物中心命中"——MoveWindowPhysical 允许窗口大半出屏(只留 60px 可见)，
            // 拖到屏幕边缘时宠物中心会跑到屏外，中心判定全部落空 → 回落到原屏/主屏，
            // 宠物就被硬拽回去了(实测：主屏拖到副屏顶部，宠物跑回主屏右下角)。
            Screen? target = _smallDragSavedScreen ?? Screens?.Primary;
            var all = Screens?.All;
            if (all != null)
            {
                var petRight = petScreenX + petW * scale;
                var petBottom = petScreenY + petH * scale;
                double bestArea = 0;
                foreach (var s in all)
                {
                    var b = s.Bounds;
                    if (b.Width <= 0 || b.Height <= 0)
                        continue;
                    var ox = Math.Min(petRight, b.X + b.Width) - Math.Max(petScreenX, b.X);
                    var oy = Math.Min(petBottom, b.Y + b.Height) - Math.Max(petScreenY, b.Y);
                    if (ox <= 0 || oy <= 0)
                        continue;   // 与这块屏没有交集
                    var area = ox * oy;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        target = s;
                    }
                }
            }

            if (target != null)
            {
                var targetScale = target.Scaling > 0 ? target.Scaling : 1.0;
                // 还原同样要挂几何事务：实测不挡这一下，LayoutUpdated 会用还没更新的
                // 508x508 旧 Bounds 覆盖 _viewWidth，把宠物夹到屏幕左上角（松手就瞬移）。
                // 事务窗口给得比换屏长一点，等 WM 真正改完尺寸。
                ExpectWindowGeometry(
                    new Avalonia.Size(target.Bounds.Width / targetScale, target.Bounds.Height / targetScale),
                    settleMs: 800);

                var b = target.Bounds;
                var viewW = b.Width / targetScale;
                var viewH = b.Height / targetScale;
                // 落点换算必须用【目标屏】的缩放：跨 DPI 拖拽时 RenderScaling 还是旧屏的值，
                // 用旧值会把宠物按 1.0 摆到 1.25 的屏上，越靠右偏得越多
                var left = (petScreenX - b.X) / targetScale;
                var top = (petScreenY - b.Y) / targetScale;

                // 顺序：先把宠物摆到【还原后】的坐标系，再改窗口几何。
                // 反过来的话窗口已经放大、宠物画布还在 (pad,pad)，那一帧重绘就是
                // "宠物闪到屏幕左上角"（松手瞬间可见）。此时窗口还是小窗，宠物被摆在
                // 窗口外，天然看不见，所以不会闪。
                _screen = target;
                _viewWidth = viewW;
                _viewHeight = viewH;
                SetPetPosition(left, top);

                ApplyScreen(target);
                Console.WriteLine($"[VPet] 小窗口落点: 窗口=({_smallDragLastPos.X},{_smallDragLastPos.Y}) " +
                                  $"宠物屏幕=({petScreenX},{petScreenY}) 屏=({b.X},{b.Y} {b.Width}x{b.Height} 缩放={targetScale:0.##}) " +
                                  $"逻辑=({left:0},{top:0})");
            }
            else
            {
                // 同上：先摆宠物，再原子改窗口
                _viewWidth = _smallDragSavedWidth;
                _viewHeight = _smallDragSavedHeight;
                SetPetPosition(_smallDragSavedPetLeft, _smallDragSavedPetTop);

                LinuxWindowNative.MoveResizeWindow(_platformHandle,
                    _smallDragSavedPos.X, _smallDragSavedPos.Y,
                    (int)Math.Round(_smallDragSavedWidth * scale),
                    (int)Math.Round(_smallDragSavedHeight * scale));
                Width = _smallDragSavedWidth;
                Height = _smallDragSavedHeight;
                Position = _smallDragSavedPos;
            }

            UpdateClickThrough();
        }

        #endregion

        /// <summary>
        /// 根据当前宠物位置刷新窗口输入区域
        /// </summary>
        private void UpdateClickThrough()
        {
            if (_platformHandle == IntPtr.Zero || _updatingInput)
                return;

            // 拖动中: 输入区域已放大为整窗, 逐帧提交只会拖慢跟手度
            if (_dragShapeActive)
                return;

            // 诊断开关: 关掉输入区域裁剪(整窗可点), 用来判断"指针离开宠物后
            // 事件是否还投递到窗口" —— 跨屏拖动卡住时排查用
            if (_noInputShape)
                return;

            if (!_clickThroughEnabled)
            {
                if (_lastRegionKey == "reset")
                    return;
                _lastRegionKey = "reset";
                LinuxWindowNative.ResetInputRegion(_platformHandle);
                return;
            }

            // Canvas 坐标是逻辑像素, X11 用物理像素
            var scale = RenderScaling > 0 ? RenderScaling : 1.0;

            var regions = new List<(int X, int Y, int Width, int Height)>
            {
                (
                    (int)(_petLeft * scale),
                    (int)(_petTop * scale),
                    (int)(MainDisplay.Width * scale),
                    (int)(MainDisplay.Height * scale)
                )
            };

            // 右键菜单展开时, 菜单区域也要能点
            // 菜单刚 IsVisible=true 时 Bounds.Height 还是 0, 直接用它会导致
            // 输入区域高度为 0 → 点击全部穿透到桌面(菜单看得见但点不动)
            if (ContextPanel.IsVisible)
            {
                var menuH = ContextPanel.Bounds.Height > 1
                    ? ContextPanel.Bounds.Height
                    : Math.Max(120, ContextItems.Children.Count * 38 + 16);
                regions.Add((
                    (int)(Canvas.GetLeft(ContextPanel) * scale),
                    (int)(Canvas.GetTop(ContextPanel) * scale),
                    (int)(ContextPanel.Width * scale),
                    (int)(menuH * scale)
                ));
            }

            // 商店 / 设置 / 角色 / 物品栏 / 成就面板 (各自 partial 文件) 同理
            foreach (var panel in VisibleOverlays())
            {
                var h = panel.Bounds.Height > 0 ? panel.Bounds.Height : panel.MaxHeight;
                regions.Add((
                    (int)(Canvas.GetLeft(panel) * scale),
                    (int)(Canvas.GetTop(panel) * scale),
                    (int)(panel.Width * scale),
                    (int)(h * scale)
                ));
            }

            var key = string.Join("|", regions.Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
            if (key == _lastRegionKey)
                return;
            _lastRegionKey = key;

            _updatingInput = true;
            try
            {
                LinuxWindowNative.SetInputRegions(_platformHandle, regions.ToArray());
            }
            finally
            {
                _updatingInput = false;
            }
        }

        #endregion

        #region 右键菜单

        private void BuildContextMenu()
        {
            Root.PointerPressed += OnRootPointerPressed;

            AddMenuItem("角色面板", ShowCharacterPanel);
            AddMenuItem("成就", ShowAchievementsPanel);
            AddMenuItem("物品栏", ShowInventoryPanel);
            AddMenuItem("投喂", ShowFeedMenu);
            AddMenuItem("工作 / 学习", ShowWorkPanel);
            AddMenuItem("商店", ShowShop);
            AddMenuItem("睡觉", "唤醒", () => MainDisplay.State == Main.WorkingState.Sleep, ToggleSleep);
            AddMenuItem("摸摸头", () => MainDisplay.DisplayTouchHead());
            AddMenuItem("说说话", () => MainDisplay.SayRnd(SpeechPool.PickIdle(MainDisplay.Core?.Save)));
            AddMenuItem("设置", ShowSettingsPanel);
            AddMenuItem("始终置顶: 开", "始终置顶: 关", () => Topmost, () => Topmost = !Topmost);
            AddMenuItem("鼠标穿透: 开", "鼠标穿透: 关", () => _clickThroughEnabled, () =>
            {
                _clickThroughEnabled = !_clickThroughEnabled;
                UpdateClickThrough();
            });
            AddMenuItem("回到屏幕底部", () => ResetPosition());
            AddMenuItem("退出", () => Close());

            // 记下根菜单, 子菜单(投喂/工作)用完可以返回
            SaveRootMenu();
        }

        /// <summary>
        /// 菜单项上次触发时间戳(用于防抖)
        /// 部分 X11/合成器环境下 xdotool/触屏会产生重复 PointerPressed,
        /// 一次点击 toggle 两次 = 面板开了立刻被关, 看起来就像"打不开"
        /// </summary>
        private long _lastMenuInvokeTicks;

        /// <summary>
        /// 点击「工作 / 学习」「投喂」「← 返回」这类会**换掉菜单内容**的项时,
        /// 不应该把菜单关掉。之前 AddMenuItem 收尾无条件 HideContextMenu(),
        /// 于是子菜单刚填进去就被隐藏 —— 表现就是"点了没反应, 得再点一次"
        /// (第二次点时旧内容还在, 才碰巧看到列表)
        /// </summary>
        private bool _keepMenuOpen;

        private void AddMenuItem(string text, Action action)
        {
            var item = new TextBlock
            {
                Text = text,
                Foreground = Avalonia.Media.Brushes.White,
                FontSize = 15,
                Padding = new Thickness(10, 6),
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Background = Avalonia.Media.Brushes.Transparent,
            };
            item.PointerPressed += (_, e) =>
            {
                // 300ms 内的重复触发视为同一次点击
                var now = Environment.TickCount64;
                if (now - _lastMenuInvokeTicks < 300)
                {
                    e.Handled = true;
                    return;
                }
                _lastMenuInvokeTicks = now;
                e.Handled = true; // 阻止冒泡到 Root 触发额外逻辑
                InvokeMenuItem(action);
            };
            ContextItems.Children.Add(item);
        }

        /// <summary>
        /// 执行一个菜单项, 并决定菜单是关闭还是留在原地。
        /// 抽成独立方法是为了让自检也能走同一条路径(否则"点了子菜单会不会被关掉"没法自动验证)
        /// </summary>
        private void InvokeMenuItem(Action action)
        {
            _keepMenuOpen = false;
            try { action(); }
            catch (Exception ex) { Console.WriteLine("[VPet] 菜单操作失败: " + ex.Message); }
            if (_keepMenuOpen)
            {
                // 留在菜单里: 内容已经换了, 重新排位置 + 刷输入区域
                _keepMenuOpen = false;
                RepositionContextPanel();
            }
            else
            {
                HideContextMenu();
            }
        }

        /// <summary>
        /// 可切换状态的菜单项, 显示文本随状态变化
        /// </summary>
        private void AddMenuItem(string textOn, string textOff, Func<bool> getState, Action toggle)
        {
            var item = new TextBlock
            {
                Text = getState() ? textOn : textOff,
                Foreground = Avalonia.Media.Brushes.White,
                FontSize = 15,
                Padding = new Thickness(10, 6),
                Background = Avalonia.Media.Brushes.Transparent,
            };
            item.PointerPressed += (_, e) =>
            {
                var now = Environment.TickCount64;
                if (now - _lastMenuInvokeTicks < 300)
                {
                    e.Handled = true;
                    return;
                }
                _lastMenuInvokeTicks = now;
                e.Handled = true;
                try
                {
                    toggle();
                    item.Text = getState() ? textOn : textOff;
                }
                catch (Exception ex) { Console.WriteLine("[VPet] 菜单操作失败: " + ex.Message); }
                HideContextMenu();
            };
            ContextItems.Children.Add(item);
        }

        private void OnRootPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(Root).Properties;
            if (props.IsRightButtonPressed)
            {
                ShowContextMenu(e.GetPosition(Root));
                e.Handled = true;
            }
            else
            {
                HideContextMenu();
            }
        }

        /// <summary>
        /// 菜单打开时的锚点(右键位置), 内容变化后重新定位要用
        /// </summary>
        private Avalonia.Point _menuAnchor;

        private void ShowContextMenu(Avalonia.Point position)
        {
            _menuAnchor = position;
            // 长列表(工作/学习、投喂)不限制高度就会超出屏幕被窗口裁掉,
            // 表现出来就是"菜单显示不全、后面几项点不到"
            ContextPanel.MaxHeight = Math.Max(160, Bounds.Height - 40);
            ContextPanel.IsVisible = true;
            RepositionContextPanel();
            UpdateClickThrough();

            // 显示瞬间 Bounds 还是 0, 只能按行数估算; 真实布局完成后再校一次
            Avalonia.Threading.Dispatcher.UIThread.Post(RepositionContextPanel,
                Avalonia.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 按当前内容尺寸把面板夹进窗口内(优先保持右键位置, 越界则翻转/上移)
        /// </summary>
        private void RepositionContextPanel()
        {
            if (!ContextPanel.IsVisible)
                return;

            // 布局未完成时用估算值兜底: 每行约 38 逻辑像素
            var menuW = ContextPanel.Bounds.Width > 1 ? ContextPanel.Bounds.Width : ContextPanel.Width;
            var estimateH = ContextItems.Children.Count * 38 + 16;
            var menuH = ContextPanel.Bounds.Height > 1
                ? ContextPanel.Bounds.Height
                : Math.Min(ContextPanel.MaxHeight, Math.Max(120, estimateH));

            var left = _menuAnchor.X;
            var top = _menuAnchor.Y;
            if (left + menuW > Bounds.Width)
                left = Bounds.Width - menuW;
            if (top + menuH > Bounds.Height)
                top = Bounds.Height - menuH;
            left = Math.Max(0, left);
            top = Math.Max(0, top);

            Canvas.SetLeft(ContextPanel, left);
            Canvas.SetTop(ContextPanel, top);

            UpdateClickThrough();
        }

        private void HideContextMenu()
        {
            if (!ContextPanel.IsVisible)
                return;
            ContextPanel.IsVisible = false;
            UpdateClickThrough();
        }

        #endregion

        #region 子菜单 / 投喂 / 工作

        /// <summary>
        /// 根菜单项缓存, 子菜单可以"返回"
        /// </summary>
        private readonly List<Control> _rootMenuItems = new List<Control>();

        private void SaveRootMenu()
        {
            _rootMenuItems.Clear();
            foreach (var child in ContextItems.Children)
            {
                if (child is Control ctl)
                    _rootMenuItems.Add(ctl);
            }
        }

        private void RestoreRootMenu()
        {
            ContextItems.Children.Clear();
            foreach (var c in _rootMenuItems)
                ContextItems.Children.Add(c);
            // 换回根菜单后要继续显示, 不要被 AddMenuItem 收尾关掉
            _keepMenuOpen = true;
        }

        /// <summary>
        /// 把右键菜单换成一列子项(顶部带返回)
        /// </summary>
        private void ShowChildMenu(string title, IEnumerable<(string text, Action action)> items)
        {
            ContextItems.Children.Clear();
            ContextItems.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Avalonia.Media.Brushes.LightGray,
                FontSize = 13,
                Padding = new Thickness(10, 4, 10, 6),
            });
            AddMenuItem("← 返回", RestoreRootMenu);
            foreach (var (text, action) in items)
                AddMenuItem(text, action);
            _keepMenuOpen = true;
            UpdateClickThrough();
        }

        /// <summary>
        /// 投喂: 列出背包里的食物
        /// </summary>
        private void ShowFeedMenu()
        {
            var core = MainDisplay.Core;
            if (core?.Inventory == null)
                return;

            var items = new List<(string, Action)>();
            foreach (var kv in core.Inventory.Items.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var food = core.Foods.FirstOrDefault(f => f.Name == kv.Key);
                if (food == null || kv.Value <= 0)
                    continue;
                var id = food.Name;
                items.Add(($"{food.Name} ×{kv.Value}", () => FeedFood(id)));
            }

            if (items.Count == 0)
                items.Add(("背包空了, 点这里去商店", ShowShop));

            ShowChildMenu("投喂", items);
        }

        /// <summary>
        /// 吃背包里的一份食物
        /// </summary>
        private void FeedFood(string name)
        {
            var core = MainDisplay.Core;
            if (core?.Inventory == null)
                return;
            var food = core.Foods.FirstOrDefault(f => f.Name == name);
            if (food == null)
            {
                MainDisplay.ShowBubble("找不到这种食物");
                return;
            }
            if (!core.Inventory.TryConsume(name))
            {
                MainDisplay.ShowBubble($"「{name}」已经吃完了, 去商店补货吧");
                return;
            }
            core.Inventory.Save();
            MainDisplay.EatFood(food);
            Console.WriteLine($"[VPet] 投喂: {food.Name} 剩余 {core.Inventory.Count(name)}");
            CheckAchievementsAndNotify();
        }


        /// <summary>
        /// 睡觉 / 唤醒
        /// </summary>
        private void ToggleSleep()
        {
            if (MainDisplay.State == Main.WorkingState.Sleep)
            {
                MainDisplay.State = Main.WorkingState.Nomal;
                MainDisplay.DisplayToNomal();
                MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.Wake));
            }
            else
            {
                MainDisplay.DisplaySleep(true);
                MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.Sleep));
            }
        }

        /// <summary>
        /// 工作结束: 复位状态并播报
        /// </summary>
        private void OnWorkFinished(FinishWorkInfo info)
        {
            var core = MainDisplay.Core;
            if (core == null)
                return;

            if (MainDisplay.State == Main.WorkingState.Work)
                MainDisplay.State = Main.WorkingState.Nomal;
            MainDisplay.NowWork = null;

            var name = info.work?.Name ?? "工作";
            var reason = info.Reason switch
            {
                FinishWorkInfo.StopReason.TimeFinish => "完成啦",
                FinishWorkInfo.StopReason.MenualStop => "被停下了",
                FinishWorkInfo.StopReason.StateFail => "被中断了",
                _ => "结束了",
            };
            var gain = "";
            if (info.Reason == FinishWorkInfo.StopReason.TimeFinish)
            {
                // info.count 是 WorkTimer.GetCount —— 整段工作每秒累加的产出总额,
                // 不是"最后一分钟的"。原来按最后一分钟写在气泡里, 数字对不上。
                // 注意: 原版的 FinishBonus 只参与效率展示与统计口径, 并没有在结算里加钱;
                // 这里如实报总额, 不额外加一笔"完成奖励", 免得和原版数值不一致。
                gain = info.work?.Type == Work.WorkType.Work
                    ? $"\n本次收入 ${info.count:0.#}"
                    : info.work?.Type == Work.WorkType.Study ? $"\n本次共获得 {info.count:0.#} 经验" : "";
            }
            else if (info.work != null && info.count > 0.05)
            {
                gain = $"\n已经结算 {info.count:0.#} {TypeUnit(info.work)}";
            }
            MainDisplay.ShowBubble($"{name} {reason} (共 {info.spendtime:F1} 分钟){gain}");
            Console.WriteLine($"[VPet] 工作结束: {name} 原因={info.Reason} 时长={info.spendtime:F1}分");
            if (Settings.SayEnabled && info.Reason == FinishWorkInfo.StopReason.TimeFinish)
                MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.WorkDone));
            if (info.Reason == FinishWorkInfo.StopReason.TimeFinish)
            {
                Stats.CountWorkFinish();
                CheckAchievementsAndNotify();
            }

            // 工作结束统一播 C_End 再回默认姿态。没有 C_End 的配置会直接回默认
            MainDisplay.Display(info.work?.Graph, GraphInfo.AnimatType.C_End,
                () => MainDisplay.DisplayToNomal());

            // 面板开着的话顺手把"进行中"刷回"开始"
            OnWorkFinishedRefreshPanel();
        }

        #endregion

        /// <summary>
        /// 成就统一检查+播报入口: 计数点(购买/投喂/工作完成/摸头)之后调用。
        /// 周期类(陪伴/干活时长)由主循环每 20 周期自查, 不走这里。
        /// </summary>
        internal void CheckAchievementsAndNotify()
        {
            try
            {
                var core = MainDisplay.Core;
                Stats.ObserveSave(core?.Save);
                var fresh = Achievements.CheckAll(Stats, core?.Save);
                foreach (var d in fresh)
                {
                    Achievements.AppendUnlockTime(d.Id);
                    MainDisplay.ShowBubble($"🏆 成就解锁: {d.Name}\n{d.Desc}");
                }
                if (fresh.Count > 0)
                    Console.WriteLine("[VPet] 成就解锁: " + string.Join(", ", fresh.Select(x => x.Name)));
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 成就检查失败: " + e.Message);
            }
        }

        #region 自动保存

        private DispatcherTimer? _autoSaveTimer;

        /// <summary>
        /// 按设置里的间隔定时保存存档与背包 (0 = 关闭)
        /// 原版有 AutoSaveInterval 定时器, 移植版之前只有退出时保存,
        /// 一旦崩溃或被强杀进度全丢
        /// </summary>
        private void StartAutoSave()
        {
            _autoSaveTimer?.Stop();

            var minutes = Settings.AutoSaveMinutes;
            if (minutes <= 0)
            {
                Console.WriteLine("[VPet] 自动保存已关闭");
                return;
            }

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(minutes) };
            _autoSaveTimer.Tick += (s, e) =>
            {
                try
                {
                    MainDisplay.Core?.Save?.Save();
                    MainDisplay.Core?.Inventory?.Save();
                    Stats.SaveIfDirty();
                    Console.WriteLine($"[VPet] 自动保存完成 {DateTime.Now:HH:mm:ss}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VPet] 自动保存失败: " + ex.Message);
                }
            };
            _autoSaveTimer.Start();
        }

        #endregion

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            try
            {
                _autoSaveTimer?.Stop();
                _charTimer?.Stop();
                MainDisplay.Core?.Save?.Save();
                MainDisplay.Core?.Inventory?.Save();
                Stats.Save();
                Settings.Save();
                MainDisplay.StopTimers();
                PetImageElement.ClearCache();
                MainDisplay.Core?.Graph?.Dispose();
            }
            catch
            {
                // 退出时不再抛出
            }
            base.OnClosing(e);
        }
    }
}
