using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VPetAvalonia.App.Views;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 当前正在的状态
    /// </summary>
    public enum WorkingState
    {
        /// <summary>
        /// 默认:啥都没干
        /// </summary>
        Nomal,
        /// <summary>
        /// 正在干活/学习中
        /// </summary>
        Work,
        /// <summary>
        /// 睡觉
        /// </summary>
        Sleep,
        /// <summary>
        /// 旅游中
        /// </summary>
        Travel,
        /// <summary>
        /// 其他状态,给开发者留个空位计算
        /// </summary>
        Empty,
    }

    /// <summary>
    /// 主显示区域 - 对应原 Main.xaml + MainDisplay.cs + MainLogic.cs
    /// 实现 IDisplayHost: 原版 Work.Display(m) 是"把动画播放在宿主窗口上"的写法,
    /// 移植后被改成 IDisplayHost 接口。之前 Main 没实现这个接口,
    /// Work.Display 里的 is 判断恒为 false —— 于是点了工作什么都不播,
    /// 宠物只是站着(数值却在后台正常结算)
    /// </summary>
    public partial class Main : UserControl, IDisplayHost
    {
        public GameCore? Core { get; set; }
        public bool IsDragging;
        public int CountNomal = 0;
        /// <summary>
        /// 当前显示类型。初始值必须是 Default 而不是 Common:
        /// 启动时开场白(Say)会检查"当前是默认状态才播放说话动画",
        /// DisplayType 的赋值又是 Post 异步的, 用 Common 会导致开场白动画永远不播
        /// </summary>
        public GraphInfo DisplayType = new GraphInfo("", GraphInfo.GraphType.Default);
        
        public MessageBar? MessageBar { get; set; }
        public WorkTimer? WorkTimer { get; set; }

        /// <summary>
        /// 调度器
        /// </summary>
        public VPetAvalonia.Abstractions.Interfaces.IDispatcher Dispatcher => new DispatcherAdapter(Avalonia.Threading.Dispatcher.UIThread);

        private Avalonia.Point? _dragStartPoint;

        /// <summary>当前是否按住宠物 (捏脸循环里用来判断"还在捏")</summary>
        public bool IsPressNow => isPress;

        /// <summary>等待长按触发的触摸区 (摸身体/捏脸), null = 没有</summary>
        private TouchArea? _pendingLongPress;

        /// <summary>长按起始位置(设计坐标), Moved 里比较位移用</summary>
        private VPetAvalonia.Core.Point _longPressStart;

        /// <summary>长按计时器: 600ms 后仍在按住且没拖动就触发</summary>
        private DispatcherTimer? _longPressTimer;

        /// <summary>长按期间允许的最大位移(设计坐标), 超过视为拖动, 取消长按</summary>
        // 长按(捏脸/摸身体)要按住 600ms，手指或光标必然有漂移。
        // 判定阈值必须比普通拖动(4px)宽得多，否则每一次捏脸都会先被判成拖动。
        private const double LongPressMoveTolerance = 20;

        /// <summary>
        /// 宠物图像显示元素 (对应 PetGrid)
        /// </summary>
        private PetImageElement? _petElement;

        /// <summary>
        /// 宠物图像显示元素 (对应 PetGrid2, 用于交叉淡入等场景)
        /// </summary>
        private PetImageElement? _petElement2;

        public Main()
        {
            InitializeComponent();

            // 图像元素只创建一次, 反复 new 会导致每帧重建 Image 控件并闪屏
            _petElement = new PetImageElement(PetGrid);
            _petElement2 = new PetImageElement(PetGrid2);

            // 绑定拖拽事件 (XAML 中未绑定, 原实现的方法永远不会被调用)
            MainGrid.PointerPressed += MainGrid_PointerPressed;
            MainGrid.PointerReleased += MainGrid_PointerReleased;
            MainGrid.PointerMoved += MainGrid_PointerMoved;

            // 兜底: 指针捕获被系统抢走(切窗口/弹窗/拖动中途 Alt+Tab)时不会收到
            // PointerReleased。若不在这里复位, 拖动中的"整窗可接收输入"会一直留着,
            // 桌面上的点击就全被透明窗口吞掉了
            MainGrid.PointerCaptureLost += MainGrid_PointerCaptureLost;

            // 初始化显示回调
            DisplayNomal = DisplayDefault;
            DisplayMove = DisplayToMove;
            DisplayIdel = DisplayToIdel;
            DisplayIdel_StateONE = DisplayToIdel_StateONE;
            DisplayTouchHead = DisplayToTouchHead;
            DisplayTouchBody = DisplayToTouchBody;
        }

        /// <summary>
        /// 宠物图像显示元素
        /// </summary>
        public PetImageElement PetElement => _petElement ??= new PetImageElement(PetGrid);

        /// <summary>
        /// 显示标签
        /// </summary>
        public void ShowLabel(string text)
        {
            LabelDisplayText.Text = text;
            LabelDisplay.IsVisible = true;
        }

        /// <summary>
        /// 隐藏标签
        /// </summary>
        public void HideLabel()
        {
            LabelDisplay.IsVisible = false;
        }

        #region 显示控制

        public void ShowOnPetGrid(Avalonia.Controls.Control content)
        {
            PetGrid.Child = content;
        }

        public void ShowOnPetGrid2(Avalonia.Controls.Control content)
        {
            PetGrid2.Child = content;
        }

        public void SwapPetGrids()
        {
            PetGrid.IsVisible = !PetGrid.IsVisible;
            PetGrid2.IsVisible = !PetGrid2.IsVisible;
        }

        /// <summary>
        /// 显示动画 (根据类型查找)
        /// </summary>
        public void Display(GraphInfo.GraphType type, GraphInfo.AnimatType animat, Action? endAction = null)
        {
            var name = Core?.Graph?.FindName(type);
            if (name != null)
            {
                Display(name, animat, endAction);
            }
            else
            {
                Console.WriteLine($"[VPet] 未找到 {type} 类型的动画, 跳过");
                endAction?.Invoke();
            }
        }

        /// <summary>
        /// 显示动画 (自动查找和匹配, 带名字回调)
        /// </summary>
        public void Display(GraphInfo.GraphType type, GraphInfo.AnimatType animat, Action<string>? endAction = null)
        {
            var name = Core?.Graph?.FindName(type);
            if (name != null)
            {
                Display(name, animat, endAction);
            }
            else
            {
                endAction?.Invoke("");
            }
        }

        /// <summary>
        /// 显示动画 (根据名字)
        /// </summary>
        public void Display(string? name, GraphInfo.AnimatType animat, Action? endAction = null)
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var graph = Core?.Graph?.FindGraph(name, animat, mode);
            if (graph != null)
            {
                Display(graph, endAction);
            }
            else
            {
                // 打出具体缺的是哪个动画, 便于排错 —— 静默失败曾导致定格后无从排查
                Console.WriteLine($"[VPet] 找不到动画: name={name ?? "<null>"} animat={animat} mode={mode}");
                endAction?.Invoke();
            }
        }

        /// <summary>
        /// 显示动画 (根据名字, 带名字回调)
        /// </summary>
        public void Display(string? name, GraphInfo.AnimatType animat, Action<string>? endAction)
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var graph = Core?.Graph?.FindGraph(name, animat, mode);
            if (graph == null)
                Console.WriteLine($"[VPet] 找不到动画(带名回调): name={name ?? "<null>"} animat={animat} mode={mode}");
            Display(graph, () => endAction?.Invoke(name ?? ""));
        }

        /// <summary>
        /// 显示动画 (根据名字和类型)
        /// </summary>
        public void Display(string name, GraphInfo.AnimatType animat, GraphInfo.GraphType type, Action? endAction = null)
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var list = Core?.Graph?.FindGraphs(name, animat, mode)?.FindAll(x => x.GraphInfo.Type == type);
            if ((list?.Count ?? -1) > 0)
                Display(list[Function.Rnd.Next(list.Count)], endAction);
            else
                Display(type, animat, endAction);
        }

        /// <summary>
        /// 显示动画 (根据名字和类型, 带名字回调)
        /// </summary>
        public void Display(string name, GraphInfo.AnimatType animat, GraphInfo.GraphType type, Action<string>? endAction = null)
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var list = Core?.Graph?.FindGraphs(name, animat, mode)?.FindAll(x => x.GraphInfo.Type == type);
            if ((list?.Count ?? -1) > 0)
                Display(list[Function.Rnd.Next(list.Count)], () => endAction?.Invoke(name));
            else
                Display(type, animat, endAction);
        }

        /// <summary>
        /// 连续找不到动画的计数。显示回调链(DisplayBLoopingForce/Say/Sleep 等)
        /// 在资源缺失时会形成同步无限递归 —— 动画未找到 → 立即回调 → 再次查找同一动画,
        /// 在 UOS 上实测两次栈溢出。累计到阈值后打断链条并交还给 EventTimer 定期驱动。
        /// </summary>
        private int _missingGraphChain;

        /// <summary>
        /// 当前正在播放的动画。切换动画时必须 Stop 上一个,
        /// 否则新旧两个播放循环会并发写同一个 Image, 画面互相覆盖、闪烁甚至定格
        /// </summary>
        private IGraph? _currentGraph;

        /// <summary>
        /// 显示动画 (直接指定动画)
        /// 动画播放与回调都在后台线程执行, 而切换动画/记录 Tag 是 UI 操作,
        /// 这里统一切到 UI 线程 —— 之前在后台线程执行回调链会抛
        /// "Call from invalid thread", 链静默断掉, 摸头后画面定格
        /// </summary>
        public void Display(IGraph? graph, Action? endAction = null)
        {
            if (graph == null)
            {
                if (++_missingGraphChain >= 8)
                {
                    _missingGraphChain = 0;
                    Console.WriteLine("[VPet] 连续多次找不到动画, 已打断回调链(检查宠物资源是否完整)");
                    return;
                }
                // 异步回调, 把同步递归压平, 避免栈溢出
                Avalonia.Threading.Dispatcher.UIThread.Post(() => endAction?.Invoke());
                return;
            }
            _missingGraphChain = 0;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => DisplayOnUIThread(graph, endAction));
        }

        /// <summary>
        /// 在 UI 线程上执行实际的动画切换与播放
        /// </summary>
        private void DisplayOnUIThread(IGraph graph, Action? endAction)
        {
            try
            {
                // 记录当前显示类型, 摸头/摸身体的连续触发判断依赖它
                DisplayType = graph.GraphInfo;
                // 停掉上一个动画(丢弃其回调链), 再启动新动画
                if (!ReferenceEquals(_currentGraph, graph))
                {
                    try { _currentGraph?.Stop(true); }
                    catch (Exception e) { Console.WriteLine("[VPet] 停止旧动画失败: " + e.Message); }
                }
                _currentGraph = graph;
                // 记录当前动画实例, 供重复触摸时续播
                PetGrid.Tag = graph;
                graph.Run(PetElement, () =>
                {
                    // 播放结束回调来自后台线程, 回调链要继续 Display, 切回 UI 线程
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => endAction?.Invoke());
                });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[VPet] 切换动画失败 {graph.GraphInfo}: {e.Message}");
                // 兜底: 交给默认呼吸动画, 保证画面不会永远定格
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DisplayToNomal());
            }
        }

        #endregion

        #region 显示回调

        /// <summary>
        /// 显示默认呼吸动画
        /// </summary>
        public Action DisplayNomal { get; set; }

        /// <summary>
        /// 旅行状态的默认显示
        /// </summary>
        public Action? DisplayTravel { get; set; }

        /// <summary>
        /// 尝试触发移动
        /// </summary>
        public Func<bool> DisplayMove { get; set; }

        /// <summary>
        /// 显示待机情况 (只有符合条件的才会显示)
        /// </summary>
        public Func<bool> DisplayIdel { get; set; }

        /// <summary>
        /// 显示待机(模式1)情况
        /// </summary>
        public Action DisplayIdel_StateONE { get; set; }

        /// <summary>
        /// 显示摸头情况
        /// </summary>
        public Action DisplayTouchHead { get; set; }

        /// <summary>
        /// 显示摸身体情况
        /// </summary>
        public Action DisplayTouchBody { get; set; }

        #endregion

        #region 显示实现

        /// <summary>
        /// DisplayDefault 重入保护: 当 Default 动画缺失时,
        /// Display(找不到) -> endAction(DisplayToNomal) -> DisplayNomal(DisplayDefault)
        /// 会形成同步无限递归, 直接栈溢出(在 UOS 上实测发生过)。
        /// 有动画正常播放时 endAction 是异步回调, 不会命中此保护。
        /// </summary>
        private bool _inDisplayDefault;

        /// <summary>
        /// 显示默认情况, 默认为默认动画
        /// </summary>
        public void DisplayDefault()
        {
            if (_inDisplayDefault)
                return;
            _inDisplayDefault = true;
            try
            {
                CountNomal++;
                Display(GraphInfo.GraphType.Default, GraphInfo.AnimatType.Single, DisplayToNomal);
            }
            finally
            {
                _inDisplayDefault = false;
            }
        }

        /// <summary>
        /// 以标准形式显示当前默认状态
        /// </summary>
        public void DisplayToNomal()
        {
            switch (State)
            {
                default:
                case WorkingState.Nomal:
                    DisplayNomal?.Invoke();
                    return;
                case WorkingState.Sleep:
                    DisplaySleep(true);
                    return;
                case WorkingState.Work:
                    NowWork?.Display(this);
                    return;
                case WorkingState.Travel:
                    DisplayTravel?.Invoke();
                    return;
            }
        }

        /// <summary>
        /// 尝试触发移动
        /// </summary>
        public bool DisplayToMove()
        {
            var moves = Core?.Graph?.GraphConfig.MovesList;
            if (moves == null || moves.Count == 0)
                return false;
            var list = moves.ToList();
            for (int i = Function.Rnd.Next(list.Count); 0 != list.Count; i = Function.Rnd.Next(list.Count))
            {
                var move = list[i];
                if (Function.Rnd.Next(100) < 30) // 简化版：30%概率触发
                {
                    // 注意用 GraphName(graph 字段) 而不是 Name:
                    // MOD 配置里 move 行只有 graph#walk.left:, 没有 name,
                    // 用 Name 会传空串导致整条动画链断掉
                    Display(move.GraphName, GraphInfo.AnimatType.A_Start, () =>
                    {
                        DisplayBLoopingForce(move.GraphName);
                    });
                    Event_MoveStartInvoke(move);
                    return true;
                }
                else
                {
                    list.RemoveAt(i);
                }
            }
            return false;
        }

        /// <summary>
        /// 显示待机情况 (只有符合条件的才会显示)
        /// </summary>
        public bool DisplayToIdel()
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            if (Core?.Graph?.GraphsName.TryGetValue(GraphInfo.GraphType.Idel, out var gl) == true)
            {
                var list = gl.ToList();
                for (int i = Function.Rnd.Next(list.Count); 0 != list.Count; i = Function.Rnd.Next(list.Count))
                {
                    var idelname = list[i];
                    var ig = Core.Graph.FindGraphs(idelname, GraphInfo.AnimatType.A_Start, mode);
                    if (ig != null && ig.Count != 0)
                    {
                        looptimes = 0;
                        CountNomal = 0;
                        Display(ig[Function.Rnd.Next(ig.Count)], () =>
                        DisplayBLoopingToNomal(idelname, Core.Graph.GraphConfig.GetDuration(idelname)));
                        return true;
                    }
                    else
                    {
                        ig = Core.Graph.FindGraphs(idelname, GraphInfo.AnimatType.Single, mode);
                        if (ig != null && ig.Count != 0)
                        {
                            looptimes = 0;
                            CountNomal = 0;
                            Display(ig[Function.Rnd.Next(ig.Count)], DisplayToNomal);
                            return true;
                        }
                        list.RemoveAt(i);
                    }
                }
                return false;
            }
            else
                return false;
        }

        /// <summary>
        /// 显示待机(模式1)情况
        /// </summary>
        public void DisplayToIdel_StateONE()
        {
            CountNomal = 0;
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var name = Core?.Graph?.FindName(GraphInfo.GraphType.StateONE);
            if (name == null)
            {
                DisplayIdel();
                return;
            }
            var list = Core.Graph.FindGraphs(name, GraphInfo.AnimatType.A_Start, mode)?.FindAll(x => x.GraphInfo.Type == GraphInfo.GraphType.StateONE);
            if (list != null && list.Count > 0)
                Display(list[Function.Rnd.Next(list.Count)], () => DisplayIdel_StateONEing(name));
            else
                DisplayIdel();
        }

        /// <summary>
        /// 显示待机(模式1)情况
        /// </summary>
        private void DisplayIdel_StateONEing(string graphname)
        {
            if (Function.Rnd.Next(++looptimes) > Core.Graph.GraphConfig.GetDuration(graphname))
                switch (Function.Rnd.Next(2 + CountNomal))
                {
                    case 0:
                        DisplayIdel_StateTWO(graphname);
                        break;
                    default:
                        Display(graphname, GraphInfo.AnimatType.C_End, GraphInfo.GraphType.StateONE, DisplayNomal);
                        break;
                }
            else
            {
                Display(graphname, GraphInfo.AnimatType.B_Loop, GraphInfo.GraphType.StateONE, DisplayIdel_StateONEing);
            }
        }

        /// <summary>
        /// 显示待机(模式2)情况
        /// </summary>
        public void DisplayIdel_StateTWO(string graphname)
        {
            CountNomal++;
            Display(graphname, GraphInfo.AnimatType.A_Start, GraphInfo.GraphType.StateTWO, DisplayIdel_StateTWOing);
        }

        /// <summary>
        /// 显示待机(模式2)情况
        /// </summary>
        private void DisplayIdel_StateTWOing(string graphname)
        {
            if (Function.Rnd.Next(++looptimes) > Core.Graph.GraphConfig.GetDuration(graphname))
            {
                looptimes = 0;
                Display(graphname, GraphInfo.AnimatType.C_End, GraphInfo.GraphType.StateTWO, DisplayIdel_StateONEing);
            }
            else
            {
                Display(graphname, GraphInfo.AnimatType.B_Loop, GraphInfo.GraphType.StateTWO, DisplayIdel_StateTWOing);
            }
        }

        int looptimes;

        /// <summary>
        /// 显示B循环+C循环+ToNomal
        /// </summary>
        public Action<string?> DisplayBLoopingToNomal(int looplength) => (gn) => DisplayBLoopingToNomal(gn, looplength);

        /// <summary>
        /// 显示B循环+C循环+ToNomal
        /// </summary>
        public void DisplayBLoopingToNomal(string? graphname, int loopLength)
        {
            if (Function.Rnd.Next(++looptimes) > loopLength)
                DisplayCEndtoNomal(graphname);
            else
                Display(graphname, GraphInfo.AnimatType.B_Loop, DisplayBLoopingToNomal(loopLength));
        }

        /// <summary>
        /// 显示睡觉情况
        /// </summary>
        public void DisplaySleep(bool force = false)
        {
            looptimes = 0;
            CountNomal = 0;
            if (force)
            {
                State = WorkingState.Sleep;
                Display(GraphInfo.GraphType.Sleep, GraphInfo.AnimatType.A_Start, DisplayBLoopingForce);
            }
            else
                Display(GraphInfo.GraphType.Sleep, GraphInfo.AnimatType.A_Start, (graphname) => DisplayBLoopingToNomal(graphname, Core.Graph.GraphConfig.GetDuration(graphname)));
        }

        /// <summary>
        /// 显示B循环 (强制)
        /// </summary>
        public void DisplayBLoopingForce(string graphname)
        {
            Display(graphname, GraphInfo.AnimatType.B_Loop, DisplayBLoopingForce);
        }

        /// <summary>
        /// 显示结束动画到正常动画 (DisplayToNomal)
        /// </summary>
        public void DisplayCEndtoNomal(string? graphname)
        {
            Display(graphname, GraphInfo.AnimatType.C_End, () => DisplayToNomal());
        }

        /// <summary>
        /// 显示摸头情况
        /// </summary>
        public void DisplayToTouchHead()
        {
            CountNomal = 0;
            if (Core?.Controller?.EnableFunction == true && Core.Save.Strength >= 10 && Core.Save.Feeling < Core.Save.FeelingMax)
            {
                Core.Save.StrengthChange(-2);
                Core.Save.FeelingChange(1);
                Core.Save.Mode = Core.Save.CalMode();
                LabelDisplayShowChangeNumber("体力-{0:f0} 心情+{1:f0}", 2, 1);
                NotifyValueChanged();
            }
            if (DisplayType.Type == GraphInfo.GraphType.Touch_Head)
            {
if (DisplayType.Animat == GraphInfo.AnimatType.A_Start)
                     return;
                 else if (DisplayType.Animat == GraphInfo.AnimatType.B_Loop)
                 {
                     if (Dispatcher.Invoke(() => PetGrid.Tag) is IGraph ig && ig.GraphInfo.Type == GraphInfo.GraphType.Touch_Head && ig.GraphInfo.Animat == GraphInfo.AnimatType.B_Loop)
                     {
                         ig.SetContinue();
                         return;
                     }
                     else if (Dispatcher.Invoke(() => PetGrid2.Tag) is IGraph ig2 && ig2.GraphInfo.Type == GraphInfo.GraphType.Touch_Head && ig2.GraphInfo.Animat == GraphInfo.AnimatType.B_Loop)
                     {
                         ig2.SetContinue();
                         return;
                     }
                 }
            }
            Display(GraphInfo.GraphType.Touch_Head, GraphInfo.AnimatType.A_Start, (graphname) =>
               Display(graphname, GraphInfo.AnimatType.B_Loop, () =>
               DisplayCEndtoNomal(graphname)));
        }

        /// <summary>
        /// 显示摸身体情况
        /// </summary>
        public void DisplayToTouchBody()
        {
            CountNomal = 0;
            if (Core?.Controller?.EnableFunction == true && Core.Save.Strength >= 10 && Core.Save.Feeling < Core.Save.FeelingMax)
            {
                Core.Save.StrengthChange(-2);
                Core.Save.FeelingChange(1);
                Core.Save.Mode = Core.Save.CalMode();
                LabelDisplayShowChangeNumber("体力-{0:f0} 心情+{1:f0}", 2, 1);
                NotifyValueChanged();
            }
            if (DisplayType.Type == GraphInfo.GraphType.Touch_Body)
            {
if (DisplayType.Animat == GraphInfo.AnimatType.A_Start)
                     return;
                 else if (DisplayType.Animat == GraphInfo.AnimatType.B_Loop)
                 {
                     if (Dispatcher.Invoke(() => PetGrid.Tag) is IGraph ig && ig.GraphInfo.Type == GraphInfo.GraphType.Touch_Body && ig.GraphInfo.Animat == GraphInfo.AnimatType.B_Loop)
                     {
                         ig.SetContinue();
                         return;
                     }
                     else if (Dispatcher.Invoke(() => PetGrid2.Tag) is IGraph ig2 && ig2.GraphInfo.Type == GraphInfo.GraphType.Touch_Body && ig2.GraphInfo.Animat == GraphInfo.AnimatType.B_Loop)
                     {
                         ig2.SetContinue();
                         return;
                     }
                 }
            }
Display(GraphInfo.GraphType.Touch_Body, GraphInfo.AnimatType.A_Start, (graphname) =>
               Display(graphname, GraphInfo.AnimatType.B_Loop, () =>
               DisplayCEndtoNomal(graphname)));
        }

        /// <summary>
        /// 显示捏脸情况 (对应原版 MainWindow.DisplayPinch):
        /// 数值上等同摸头(体力-2 心情+1), 动画是 pinch 的 A_Start → B_Loop 持续循环,
        /// 松手后走 C_End 回待机
        /// </summary>
        public void DisplayToPinch()
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            // 该宠物没有 pinch 动画就不进 (自定义 mod 可能没画)
            if (Core?.Graph?.FindGraph("pinch", GraphInfo.AnimatType.A_Start, mode) == null)
                return;

            CountNomal = 0;
            if (Core?.Controller?.EnableFunction == true && Core.Save!.Strength >= 10 && Core.Save.Feeling < Core.Save.FeelingMax)
            {
                Core.Save.StrengthChange(-2);
                Core.Save.FeelingChange(1);
                Core.Save.Mode = Core.Save.CalMode();
                LabelDisplayShowChangeNumber("体力-{0:f0} 心情+{1:f0}", 2, 1);
                NotifyValueChanged();
            }
            // 已经在捏且处于循环段: 不重头播
            if (DisplayType.Name == "pinch" && DisplayType.Animat == GraphInfo.AnimatType.B_Loop)
                return;

            Display("pinch", GraphInfo.AnimatType.A_Start, () => DisplayPinchLoop());
        }

        /// <summary>
        /// 捏脸循环: 还按着就继续 B_Loop, 松手收尾 C_End
        /// </summary>
        private void DisplayPinchLoop()
        {
            if (IsPressNow && DisplayType.Name == "pinch")
            {
                Display("pinch", GraphInfo.AnimatType.B_Loop, DisplayPinchLoop);
            }
            else
            {
                Display("pinch", GraphInfo.AnimatType.C_End, () => DisplayToNomal());
            }
        }

        /// <summary>
        /// 投喂食物: 加数值 + 播放吃东西动画 + 冒泡提示
        /// 对应原版 MainWindow.DisplayFoodAnimation + GameSave.EatFood
        /// </summary>
        /// <param name="food">食物</param>
        public bool EatFood(Food food) => EatFood(food, 1);

        /// <summary>
        /// 一次吃 N 份(物品栏的"全部使用")。
        ///
        /// 数值按份数叠加, 但动画只播一次 —— 连着调 N 次 Display 会让回调链互相打断,
        /// 最后停在半路(宠物卡在吃东西的姿势)。吃饱/喝够就提前停, 不白吃。
        /// </summary>
        public bool EatFood(Food food, int count)
        {
            if (food == null || Core?.Save == null)
                return false;

            count = Math.Max(1, count);
            var save = Core.Save;

            // 已经满了就没必要再吃(药品看健康, 礼品不加饱腹所以不限)
            if (count > 1 && IsAlreadyFull(food, save))
                return false;

            var used = 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0 && IsAlreadyFull(food, save))
                    break;
                save.EatFood(food);
                used++;
            }
            if (used == 0)
                return false;

            save.Mode = save.CalMode();
            NotifyValueChanged();

            // 播吃饭/喝水动画。注意: 食物动画(FoodAnimation)在资源里注册的动作类型是 Single,
            // 它不是 A_Start/B_Loop/C_End 三段式的普通动画, 整段自己播完就结束。
            // 之前按 A_Start 去找, 永远命中不到 → 找不到动画 → 回调链被反复触发后打断,
            // 表现就是"投喂后没吃东西的动画"
            var graph = food.GetGraph();
            CountNomal = 0;
            try
            {
                DisplayFoodGraph(graph, food.Name);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 投喂动画失败: " + e.Message);
                DisplayToNomal();
            }

            var caption = used > 1 ? $"吃掉了 {used} 份「{food.Name}」" : $"吃掉了「{food.Name}」";
            if (Core.Controller?.EnableFunction != true)
                ShowBubble(caption + "(数据计算已关闭, 数值不会变化)");
            else
                ShowBubble(caption + $"\n{food.EffectText}" +
                           (food.IsDrug && save.Mode != ModeType.Ill ? "\n(现在不需要吃药哦)" : ""));

            // 统计 + 台词: 吃东西的时候有概率应一句
            for (int i = 0; i < used; i++)
                Stats?.CountFeed(food.Name);
            if (Settings?.SayEnabled != false && Function.Rnd.Next(2) == 0)
                SayRnd(SpeechPool.Pick(food.IsDrug ? SpeechPool.Scene.Ill : SpeechPool.Scene.Feed));

            LastEatUsed = used;
            return true;
        }

        /// <summary>上一次 EatFood 实际吃掉了几份(调用方要据此把多余的退回背包)</summary>
        public int LastEatUsed { get; private set; } = 1;

        /// <summary>
        /// 这个食物现在吃下去是否浪费: 饱腹/口渴/健康已经顶到上限
        /// </summary>
        private static bool IsAlreadyFull(Food food, VPetAvalonia.Core.IGameSave save)
        {
            if (save is not GameSave gs)
                return false;

            switch (food.Type)
            {
                case Food.FoodType.Drink:
                    return food.StrengthDrink > 0 && gs.StrengthDrink >= gs.StrengthMax - 0.5;
                case Food.FoodType.Drug:
                    return food.IsDrug && gs.Health >= 100 - 0.5;
                case Food.FoodType.Gift:
                    return false;   // 礼品主要加好感/心情, 不受饱腹限制
                default:
                    return food.StrengthFood > 0 && gs.StrengthFood >= gs.StrengthMax - 0.5;
            }
        }

        /// <summary>
        /// 播放吃饭/喝水/礼物动画。
        ///
        /// 资源里的 eat/drink/gift 是 <see cref="FoodAnimation"/> —— 三层"夹心"动画:
        /// 后层(宠物本体) + 中间层(食物图标) + 前层(宠物举手的动作)。
        /// 原版把它塞进一个固定的 500x500 容器里, 只让中间层(食物图标, 宽 60~90px)
        /// 跟着轨迹帧改尺寸。
        ///
        /// 本移植版只有单个宠物图像元素, 直接 <c>Display(FoodAnimation)</c> 等于把
        /// 食物图标的帧尺寸贴到宠物本体上: 每帧把宽高设成 60~90px,
        /// 宠物就在一瞬间缩成一个小点, 播完再弹回来。
        ///
        /// 这里改为播放它的【后层】——后层才是完整宠物本体。
        ///
        /// 实测 eat/Happy 两层内容(按非透明像素裁剪范围量):
        ///   back_lay   19 帧, 有效区域 500x937(近乎满幅) → 整只宠物
        ///   front_lay   8 帧, 有效区域 259x147, 位于 y≈447 → 只是手/嘴那一小块
        /// 两层都画在 1000x1000 画布上, 所以后层单独播尺寸与普通动画完全一致,
        /// 不会缩放; 代价是没有那颗飞进嘴里的食物图标(中间层)。
        /// 曾错选前层, 表现就是"投喂时只剩一只手"。
        /// </summary>
        private void DisplayFoodGraph(string graphName, string foodName)
        {
            var mode = Core?.Save?.Mode ?? ModeType.Nomal;
            var animat = GraphInfo.AnimatType.Single;

            if (Core?.Graph?.FindGraph(graphName, animat, mode) is FoodAnimation fa)
            {
                var layAnimat = fa.GraphInfo?.Animat ?? animat;

                // 优先后层(完整本体), 后层缺失再退到前层(部分 mod 只提供手部动作)
                foreach (var lay in new[] { fa.Back_Lay, fa.Front_Lay })
                {
                    if (string.IsNullOrEmpty(lay))
                        continue;
                    var g = Core.Graph.FindGraph(lay, layAnimat, mode);
                    if (g == null || g is FoodAnimation)
                        continue;
                    Console.WriteLine($"[VPet] 食物动画: {foodName} 播放图层={lay}");
                    Display(g, () => DisplayToNomal());
                    return;
                }

                Console.WriteLine($"[VPet] 食物动画图层缺失: {foodName} " +
                                  $"back={fa.Back_Lay} front={fa.Front_Lay}, 直接回到待机");
                DisplayToNomal();
                return;
            }

            // 不是夹心动画(自定义 mod 可能直接给普通动画), 按原样播
            Display(graphName, animat, () => DisplayToNomal());
        }

        /// <summary>
        /// 在宠物上方显示一句话(没有 MessageBar 时降级为控制台输出)
        /// </summary>
        public void ShowBubble(string text)
        {
            try
            {
                MessageBar?.Show(Core?.Save?.Name ?? "桌宠", text);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 气泡显示失败: " + e.Message);
            }
        }

        #endregion

        #region 事件处理

        private void MainGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var pointProps = e.GetCurrentPoint(MainGrid);
            if (!pointProps.Properties.IsLeftButtonPressed)
                return;

            _dragStartPoint = e.GetPosition(MainGrid);
            IsDragging = true;
            isPress = true;
            _dragFrames = 0;
            _dragBeginTicks = Environment.TickCount64;
            _dragLastEventTicks = 0;
            _dragMaxGapMs = 0;
            // 拖动期间允许宠物暂时超出窗口(跨屏换屏时保持连续)
            if (TopLevel.GetTopLevel(this) is MainWindow mw0)
            {
                mw0.FreeClamp = true;
                // 输入区域临时放大到整窗: 拖动时宠物位置每帧都变, 若继续逐帧下发
                // XShape, UI 线程会被 X 请求拖住, 表现为宠物严重落后鼠标
                mw0.BeginDragShape();

                // 抓取偏移必须用【窗口坐标】算, 不能用 MainGrid 坐标:
                // MainGrid 在宠物内部, 宠物一动它的原点就跟着动, 拿它的绝对坐标
                // 去和 Canvas 里的宠物位置相减, 相当于把指针位移算成了相对量,
                // 每帧结果在 d 和 0 之间来回跳 —— 表现就是"鼠标走两步宠物走一步"。
                _dragCursor = e.GetPosition(mw0);
                _dragStartCursor = _dragCursor;
                _dragStartPet = mw0.PetTopLeft();
                _grabOffset = _dragCursor - _dragStartPet;

                // 小窗口拖拽不在这里进：单纯点一下（摸头/捏脸）也是 PointerPressed，
                // 按下就缩窗意味着每次点击都要重建一次整屏透明表面，
                // 而且缩窗/还原是异步几何变更，容易和 LayoutUpdated 的尺寸校正打架。
                // 这里只记"候选"，等指针真的移动超过阈值（确实是拖动）再切过去。
                _smallDragCandidate = MainWindow.SmallDragEnabled;
                _smallDragAnchor = e.GetPosition(this);
            }
            if (_debugDrag)
                Console.WriteLine($"[DBG] 按下: local=({_dragStartPoint.Value.X:0},{_dragStartPoint.Value.Y:0})");

            // 显式捕获指针: 拖动时指针会离开控件, X11 + XShape 输入区域下
            // 依赖隐式捕获可能丢 PointerMoved, 显式捕获保证整个拖动过程事件不断流
            e.Pointer.Capture(MainGrid);

            // 命中摸头/摸身体/捏脸区域 (配置坐标基于 500x500 的设计分辨率, 需要换算)
            var pos = ToDesignPosition(_dragStartPoint.Value);
            var areas = Core?.TouchEvent;
            if (areas != null)
            {
                foreach (var area in areas)
                {
                    if (area.Touch(pos))
                    {
                        if (!area.IsPress)
                            area.DoAction();
                        else
                            StartLongPress(area, pos);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 启动长按: 600ms 后仍然按住且没拖走就触发 (原版靠 WPF 的 LongPress 事件,
        /// 移植版之前 IsPress 区域永远不会被触发, 摸身体/捏脸实际上是死的)
        /// </summary>
        private void StartLongPress(TouchArea area, VPetAvalonia.Core.Point designPos)
        {
            _pendingLongPress = area;
            _longPressStart = designPos;

            _longPressTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600),
            };
            _longPressTimer.Stop();
            _longPressTimer.Tick -= LongPressTick;
            _longPressTimer.Tick += LongPressTick;
            _longPressTimer.Start();
        }

        private void LongPressTick(object? sender, EventArgs e)
        {
            _longPressTimer?.Stop();
            var area = _pendingLongPress;
            _pendingLongPress = null;
            if (area == null || !isPress)
                return;
            // 这一次按下已经兑现成长按（捏脸/摸身体），后面就算还在动也不算拖动。
            // 否则捏完手一挪就缩窗拖动，捏脸动作会被打断。
            _smallDragCandidate = false;
            try
            {
                area.DoAction();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[VPet] 长按触发失败: " + ex.Message);
            }
        }

        private void CancelLongPress()
        {
            _pendingLongPress = null;
            _longPressTimer?.Stop();
        }

        private void MainGrid_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_debugDrag)
                Console.WriteLine("[DBG] 松开");

            // 小窗口拖拽收尾：把窗口还原成落点所在的整屏，并按落点摆好宠物
            if (_smallDragActive)
            {
                _smallDragActive = false;
                _smallDragCandidate = false;
                if (TopLevel.GetTopLevel(this) is MainWindow mwSmall)
                {
                    mwSmall.EndSmallWindowDrag();
                    mwSmall.FreeClamp = false;
                    mwSmall.EndDragShape();
                }
                IsDragging = false;
                isPress = false;
                _dragStartPoint = null;
                _dragRebasePending = false;
                CancelLongPress();
                e.Pointer.Capture(null);
                Core?.Controller?.ResetPosition();
                return;
            }

            if (_dragFrames > 0)
            {
                var total = Environment.TickCount64 - _dragBeginTicks;
                var petMoved = TopLevel.GetTopLevel(this) is MainWindow mwD
                    ? mwD.PetTopLeft() - _dragStartPet
                    : default;
                var cursorMoved = _dragCursor - _dragStartCursor;
                Console.WriteLine($"[VPet] 拖动: {_dragFrames} 个移动事件 / {total}ms " +
                                  $"最大间隔 {_dragMaxGapMs}ms " +
                                  $"平均 {(_dragFrames > 0 ? (double)total / _dragFrames : 0):0.0}ms/事件 " +
                                  $"指针位移=({cursorMoved.X:0},{cursorMoved.Y:0}) " +
                                  $"宠物位移=({petMoved.X:0},{petMoved.Y:0})");
            }

            // 松手时指针若已在另一块屏, 兜底把窗口和宠物带过去(窗口坐标)
            if (IsDragging && _dragStartPoint.HasValue
                && TopLevel.GetTopLevel(this) is MainWindow mw2)
                mw2.FinishDragAt(e.GetPosition(mw2));

            IsDragging = false;
            isPress = false;
            _dragStartPoint = null;
            _dragRebasePending = false;
            _smallDragCandidate = false;
            CancelLongPress();
            e.Pointer.Capture(null);

            // 松手: 关闭"允许超出窗口", 把宠物夹回屏内
            if (TopLevel.GetTopLevel(this) is MainWindow mw3)
            {
                mw3.FreeClamp = false;
                // 恢复精确的 XShape 输入区域(按宠物最终位置下发一次)
                mw3.EndDragShape();
            }

            // 松手后如果被拖到了边缘, 自动靠边
            if (Core?.Controller is { } c)
                c.ResetPosition();
        }

        /// <summary>
        /// 指针捕获意外丢失(切窗口/弹窗/Alt+Tab)时的收尾。
        /// 幂等: 与 PointerReleased 的收尾重复执行无副作用
        /// </summary>
        private void MainGrid_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!IsDragging)
                return;

            if (_smallDragActive)
            {
                _smallDragActive = false;
                if (TopLevel.GetTopLevel(this) is MainWindow mwLost)
                {
                    mwLost.EndSmallWindowDrag();
                    mwLost.FreeClamp = false;
                    mwLost.EndDragShape();
                }
            }

            IsDragging = false;
            isPress = false;
            _dragStartPoint = null;
            _dragRebasePending = false;
            _smallDragCandidate = false;
            CancelLongPress();

            if (TopLevel.GetTopLevel(this) is MainWindow mw)
            {
                mw.FreeClamp = false;
                mw.EndDragShape();
            }

            Core?.Controller?.ResetPosition();
        }

        private void MainGrid_PointerMoved(object? sender, PointerEventArgs e)
        {
            // 长按判定期间挪动超过阈值 = 用户在拖动, 取消长按
            if (_pendingLongPress != null && _dragStartPoint.HasValue)
            {
                var p = ToDesignPosition(e.GetPosition(MainGrid));
                if (Math.Abs(p.X - _longPressStart.X) > LongPressMoveTolerance
                    || Math.Abs(p.Y - _longPressStart.Y) > LongPressMoveTolerance)
                    CancelLongPress();
            }

            if (!IsDragging || !_dragStartPoint.HasValue)
                return;
            if (TopLevel.GetTopLevel(this) is not MainWindow mw)
                return;

            // 拖动确认：指针离开按下点超过阈值才真正切到小窗口拖拽。
            // 这样单击（摸头）完全不会动窗口尺寸。
            // 长按候选区放宽到 LongPressMoveTolerance：捏脸要按住 600ms，
            // 用 4px 判定等于让捏脸永远触发不了（手指一定在漂）。
            if (_smallDragCandidate && !_smallDragActive)
            {
                var threshold = _pendingLongPress != null ? LongPressMoveTolerance : DragStartThreshold;
                var cur = e.GetPosition(this);
                if (Math.Abs(cur.X - _smallDragAnchor.X) > threshold
                    || Math.Abs(cur.Y - _smallDragAnchor.Y) > threshold)
                {
                    _smallDragCandidate = false;
                    CancelLongPress();   // 已经开始拖了, 不要再判成长按
                    if (mw.BeginSmallWindowDrag())
                    {
                        _smallDragActive = true;
                        // 抓取偏移用【按下时】宠物内的相对位置算, 不能用当前指针:
                        // 缩窗后窗口是按宠物原位置摆的, 拿已经移开几像素的指针去减,
                        // 抓点就跑到宠物外面, 拖起来会"跳一下然后跟不上"。
                        // 宠物在缩后的窗口里位于 (pad,pad), 所以偏移是 pad + 按压点。
                        var sc = mw.RenderScaling > 0 ? mw.RenderScaling : 1.0;
                        _smallGrab = new Avalonia.PixelPoint(
                            (int)Math.Round((MainWindow.SmallDragPad + _grabOffset.X) * sc),
                            (int)Math.Round((MainWindow.SmallDragPad + _grabOffset.Y) * sc));
                        if (_debugDrag)
                            Console.WriteLine($"[DBG] 小窗口拖拽: 抓取=({_smallGrab.X},{_smallGrab.Y}) 抓取点={_grabOffset} 缩放={sc:0.##}");
                    }
                    return;   // 这一帧先不移动, 让窗口先落到抓取位置
                }
            }

            // 小窗口拖拽：窗口整体跟着指针走（一次 MoveWindow），窗口内坐标恒为 (pad,pad)。
            // 这条路径不碰画布布局，也不需要逐帧下发 XShape，是"跟手"的关键。
            if (_smallDragActive)
            {
                if (Native.LinuxWindowNative.TryGetPointerPosition(out var spx, out var spy))
                    mw.MoveWindowPhysical(spx - _smallGrab.X, spy - _smallGrab.Y);
                _dragFrames++;
                if (_debugDrag)
                    Console.WriteLine($"[DBG] 小窗口拖动: 指针=({spx},{spy}) 窗口=({mw.Position.X},{mw.Position.Y})");
                return;
            }

            // 全程用【窗口坐标】: 宠物位置(Canvas.GetLeft)也在窗口坐标里,
            // 同一个坐标系才能 1:1 跟随。用 MainGrid 坐标会因为宠物移动而
            // 让坐标原点跟着移动, 跟随比例变成一半(实测手感就是"错位")
            var currentPoint = e.GetPosition(mw);

            // 换屏后的重建基准阶段。
            // 窗口搬家在 X11 上是异步请求: Move/Resize 发出去之后, 要等 WM 回
            // ConfigureNotify, Avalonia 的 Position/尺寸才真正更新。这中间的几帧里
            // e.GetPosition(窗口) 仍是旧坐标系, 拿它去摆宠物必然错位:
            // 宠物被摆到窗口外(被裁掉) → 下一帧边缘检测又判它"顶边" → 再换一次屏,
            // 于是两块屏之间连续横跳。所以这里不是"丢一帧", 而是等到窗口真的到位。
            if (_dragRebasePending)
            {
                _dragCursor = currentPoint;
                _dragStartPoint = e.GetPosition(MainGrid);
                // 每帧都重算抓取偏移: 窗口就位的那一刻这份偏移自然是对的,
                // 之后接管时宠物不会跳
                _grabOffset = currentPoint - mw.PetTopLeft();
                if (!mw.IsSwitchSettled())
                    return;
                _dragRebasePending = false;
                return;
            }

            var delta = currentPoint - _dragCursor;
            _dragCursor = currentPoint;

            // 绝对定位拖动: 直接用指针位置摆放宠物(见 DragTo 注释)
            mw.DragTo(currentPoint, _grabOffset);

            // 拖动流畅度埋点: 记录事件数/间隔, 松手时打一行日志。
            // 跟手变慢时靠这行区分"事件来得稀"(合成器/输入层问题)还是
            // "处理得慢"(UI 线程被占住)
            var nowTicks = Environment.TickCount64;
            _dragFrames++;
            if (_dragLastEventTicks > 0)
            {
                var gap = nowTicks - _dragLastEventTicks;
                if (gap > _dragMaxGapMs) _dragMaxGapMs = gap;
            }
            _dragLastEventTicks = nowTicks;

            if (_debugDrag)
                Console.WriteLine($"[DBG] 拖动: win=({currentPoint.X:0},{currentPoint.Y:0}) " +
                                  $"delta=({delta.X:0},{delta.Y:0})");

            // 1) 宠物已经顶到窗口边缘, 还在往外拖 → 主动换屏。
            //    这条路径不依赖"指针出窗后还能不能收到事件", 是跨屏拖动的主通道。
            if (TryEdgeSwitch(mw, delta, currentPoint))
            {
                _dragRebasePending = true;
                return;
            }

            // 2) 指针确实拖出了窗口 → 按指针所在屏换屏。
            //    指针在窗口内时这一条永远不成立, 不必每帧遍历一遍屏幕列表。
            //    宽度取 ViewWidth(设定的目标值), 不取 Bounds(换屏后滞后, 还是旧屏宽度)
            if (currentPoint.X < 0 || currentPoint.Y < 0
                || currentPoint.X > mw.ViewWidth || currentPoint.Y > mw.ViewHeight)
            {
                if (mw.TrySwitchScreen(currentPoint))
                    _dragRebasePending = true;
            }
        }

        /// <summary>
        /// 宠物顶到屏幕边缘且继续向外拖时, 把窗口换到相邻显示器
        /// </summary>
        private bool TryEdgeSwitch(MainWindow mw, Avalonia.Vector delta, Avalonia.Point cursor)
        {
            var c = Core?.Controller;
            if (c == null)
                return false;

            // 换屏事务还没结束(窗口还在搬) → 不参与换屏判定, 否则会一路连跳
            if (!mw.IsSwitchSettled())
                return false;

            // 指针必须真的已经越出窗口, 才允许换屏。
            // 原先是"宠物贴边就换", 但那时指针还停在本屏(在宠物身上), 换屏后宠物
            // 按抓取偏移换算会大半个落在本屏, 被只覆盖新屏的窗口裁掉 —— 视觉上就是
            // "闪没了", 紧接着边缘检测又把它拉回来, 两屏之间来回鬼畜。
            // 现在等指针也越过去, 换屏时宠物落点才与新屏自洽。
            if (!mw.PointerOutside(cursor, 2))
                return false;

            // 换屏瞬间宠物允许超出窗口一点(-8 以内也算贴边), 否则会来回触发
            static bool AtEdge(double d) => d < 4 && d > -8;

            double sx = 0, sy = 0;
            if (delta.X > 0.5 && AtEdge(c.GetWindowsDistanceRight())) sx = 1;
            else if (delta.X < -0.5 && AtEdge(c.GetWindowsDistanceLeft())) sx = -1;

            if (delta.Y > 0.5 && AtEdge(c.GetWindowsDistanceDown())) sy = 1;
            else if (delta.Y < -0.5 && AtEdge(c.GetWindowsDistanceUp())) sy = -1;

            if (sx == 0 && sy == 0)
                return false;

            var ok = mw.TrySwitchScreenByDirection(sx, sy, cursor);

            // 限流日志: 排查"拖不过去"时, 日志里能看到当时贴的是哪条边、有没有换成功
            var now = Environment.TickCount64;
            if (now - _lastEdgeLogTicks > 1000)
            {
                _lastEdgeLogTicks = now;
                Console.WriteLine($"[VPet] 边缘换屏: 方向=({sx},{sy}) " +
                                  $"距离 左{c.GetWindowsDistanceLeft():0} 右{c.GetWindowsDistanceRight():0} " +
                                  $"上{c.GetWindowsDistanceUp():0} 下{c.GetWindowsDistanceDown():0} 结果={ok}");
            }

            return ok;
        }

        private long _lastEdgeLogTicks = -10000;

        /// <summary>
        /// 换屏后下一帧事件坐标系已变化, 需要丢弃该帧 delta 重新对基准
        /// </summary>
        private bool _dragRebasePending;

        /// <summary>
        /// 拖动流畅度埋点(松手时汇总成一行日志)
        /// </summary>
        private int _dragFrames;
        private long _dragBeginTicks;
        private long _dragLastEventTicks;
        private long _dragMaxGapMs;

        /// <summary>
        /// 按下瞬间"指针 - 宠物左上角"的偏移, 拖动全程保持不变(窗口坐标系)
        /// </summary>
        private Avalonia.Vector _grabOffset;

        /// <summary>
        /// 上一帧指针位置(窗口坐标系)。拖动全程只认这个坐标系,
        /// 用 MainGrid 坐标会和宠物自身位置互相影响, 跟随比例变成一半
        /// </summary>
        private Avalonia.Point _dragCursor;
        private Avalonia.Point _dragStartCursor;
        private Avalonia.Vector _dragStartPet;

        /// <summary>
        /// 小窗口拖拽中（窗口缩到宠物大小，整窗跟着指针走）
        /// </summary>
        private bool _smallDragActive;

        /// <summary>
        /// 已按下但还没确认是拖动（指针没动够阈值），只用来决定要不要切小窗口
        /// </summary>
        private bool _smallDragCandidate;

        /// <summary>
        /// 按下时的指针位置（Main 控件坐标），拖动阈值判定用
        /// </summary>
        private Avalonia.Point _smallDragAnchor;

        /// <summary>
        /// 按下后移动多少逻辑像素才认定是拖动（区分单击与拖动）
        /// </summary>
        private const double DragStartThreshold = 4;

        /// <summary>
        /// 小窗口拖拽时，指针相对窗口左上角的物理像素偏移（按下瞬间固定）
        /// </summary>
        private Avalonia.PixelPoint _smallGrab;

        private readonly bool _debugDrag =
            Environment.GetEnvironmentVariable("VPET_DEBUG_DRAG") == "1";

        /// <summary>
        /// 把控件实际坐标换算回 500x500 的设计坐标
        /// </summary>
        private VPetAvalonia.Core.Point ToDesignPosition(Avalonia.Point p)
        {
            var w = MainGrid.Bounds.Width;
            var scale = w > 0 ? 500.0 / w : 1.0;
            return new VPetAvalonia.Core.Point(p.X * scale, p.Y * scale);
        }

        #endregion
    }
}
