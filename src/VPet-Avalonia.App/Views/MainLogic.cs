using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.App.Views;
using VPetAvalonia.Abstractions.Interfaces;
using Timer = System.Timers.Timer;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 主逻辑 - 对应原 MainLogic.cs
    /// 处理说话、状态计算、工作/学习/玩耍等交互逻辑
    /// </summary>
    public partial class Main
    {
        public const int TreeRND = 5;
        
        public Func<Main, bool>? TimeHandle { get; set; }
        public Action<Main>? TimeUIHandle { get; set; }

        /// <summary>
        /// 处理说话内容
        /// </summary>
        public event Action<string>? OnSay;

        /// <summary>
        /// 运行设置 (由 MainWindow 注入); 说话开关/频率从这里读
        /// </summary>
        public GameSettings? Settings;

        /// <summary>
        /// 陪伴统计 (由 MainWindow 注入)
        /// </summary>
        public CharacterStats? Stats;

        /// <summary>说话节流计数: 每经过 SayPeriod 个计算周期才试一次</summary>
        private int _sayTick;

        /// <summary>上一次的等级, 用来发现升级</summary>
        private int _lastLevel = -1;

        /// <summary>
        /// 体力/心情/饱腹/口渴等数值发生变化时触发, 供角色面板立即刷新。
        /// 之前面板只靠定时器刷新, 摸头弹窗写着 -2/+1 时面板还是旧数字,
        /// 看起来就像"弹窗和面板对不上"
        /// </summary>
        public event Action? ValueChanged;

        /// <summary>
        /// 通知界面数值已变化 (统一切到 UI 线程)
        /// </summary>
        public void NotifyValueChanged()
        {
            var handler = ValueChanged;
            if (handler == null)
                return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { handler(); }
                catch (Exception e) { Console.WriteLine("[VPet] 刷新面板失败: " + e.Message); }
            });
        }

        /// <summary>
        /// 上次交互时间
        /// </summary>
        public DateTime LastInteractionTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 事件Timer
        /// </summary>
        public Timer EventTimer = new Timer(15000)
        {
            AutoReset = true,
            Enabled = true
        };

        /// <summary>
        /// 说话,使用随机表情
        /// </summary>
        public void SayRnd(string text, bool force = false, string? desc = null)
        {
            Say(text, SayRndFunction(text), force, desc);
        }

        /// <summary>
        /// 随机表情的方法
        /// 从已加载的 Say 类动画中随机选一个; 之前写死返回 "say",
        /// 但根本没有叫 "say" 的动画(实际叫 self/serious/shining/shy),
        /// 查找失败后在回调链里无限递归直接栈溢出
        /// </summary>
        public Func<string, string> SayRndFunction => (text) =>
            Core?.Graph?.FindName(GraphInfo.GraphType.Say) ??
            Core?.Graph?.FindName(GraphInfo.GraphType.Default) ?? "";

        /// <summary>
        /// 说话处理 (请不要阻塞该处理)
        /// </summary>
        public List<Action<SayInfo>> SayProcess = new List<Action<SayInfo>>();

        /// <summary>
        /// 普通说话
        /// </summary>
        public void Say(SayInfoWithOutStream sayinfo)
        {
            Task.Run(() =>
            {
                OnSay?.Invoke(sayinfo.Text);

                SayProcess.ForEach(a => a.Invoke(sayinfo));

                if (sayinfo.Force || !string.IsNullOrWhiteSpace(sayinfo.GraphName) && DisplayType.Type == GraphInfo.GraphType.Default)
                {
                    Display(sayinfo.GraphName, GraphInfo.AnimatType.A_Start, () =>
                    {
Dispatcher.Invoke(() =>
                    {
                        MessageBar?.Show(Core.Save!.Name, sayinfo.Text, sayinfo.GraphName, sayinfo.MsgContent);
                    });
                        DisplayBLoopingForce(sayinfo.GraphName!);
                    });
                }
else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            MessageBar?.Show(Core.Save!.Name, sayinfo.Text, sayinfo.GraphName, sayinfo.MsgContent);
                        });
                    }
            });
        }

        /// <summary>
        /// 说话
        /// </summary>
        public void Say(string text, string? graphname = null, bool force = false, string? desc = null) => Say(new SayInfoWithOutStream()
        {
            Text = text,
            GraphName = graphname,
            Desc = desc,
            Force = force,
            MsgContent = null
        });

        /// <summary>
        /// 说话
        /// </summary>
        public void Say(string text, object msgcontent, string? graphname = null, bool force = false) => Say(new SayInfoWithOutStream()
        {
            Text = text,
            GraphName = graphname,
            Desc = null,
            Force = force,
            MsgContent = msgcontent
        });

        int labeldisplaycount = 100;
        int labeldisplayhash = 0;
        Timer labeldisplaytimer = new Timer(10)
        {
            AutoReset = true,
        };
        double labeldisplaychangenum1 = 0;
        double labeldisplaychangenum2 = 0;

        /// <summary>
        /// 显示消息弹窗Label
        /// </summary>
        public void LabelDisplayShow(string text, int time = 2000)
        {
            labeldisplayhash = text.GetHashCode();
            Dispatcher.Invoke(() =>
            {
                LabelDisplayText.Text = text;
                LabelDisplay.Opacity = 1;
                LabelDisplay.IsVisible = true;
                labeldisplaycount = time / 10;
                labeldisplaytimer.Start();
            });
        }

        /// <summary>
        /// 显示消息弹窗Label,自动统计数值变化
        /// </summary>
        public void LabelDisplayShowChangeNumber(string text, double changenum1, double changenum2 = 0, int time = 2000)
        {
            if (labeldisplayhash == text.GetHashCode())
            {
                labeldisplaychangenum1 += changenum1;
                labeldisplaychangenum2 += changenum2;
            }
            else
            {
                labeldisplaychangenum1 = changenum1;
                labeldisplaychangenum2 = changenum2;
                labeldisplayhash = text.GetHashCode();
            }
            Dispatcher.Invoke(() =>
            {
                LabelDisplayText.Text = string.Format(text, labeldisplaychangenum1, labeldisplaychangenum2);
                LabelDisplay.Opacity = 1;
                LabelDisplay.IsVisible = true;
                labeldisplaycount = time / 10;
                labeldisplaytimer.Start();
            });
        }

        public Work? NowWork;

        /// <summary>
        /// 根据消耗计算相关数据
        /// </summary>
        public void FunctionSpend(double TimePass)
        {
            Core.Save!.CleanChange();
            Core.Save!.StoreTake();
            double freedrop = (DateTime.Now - LastInteractionTime).TotalMinutes;
            if (freedrop < 1)
                freedrop = 0;
            else
                freedrop = Math.Min(Math.Sqrt(freedrop) * TimePass / 4, Core.Save!.FeelingMax / 800);
            double sm25 = Core.Save!.StrengthMax * 0.25;
            double sm50 = Core.Save!.StrengthMax * 0.5;
            double sm60 = Core.Save!.StrengthMax * 0.6;
            double sm75 = Core.Save!.StrengthMax * 0.75;

            switch (State)
            {
                case WorkingState.Empty:
                    break;
                case WorkingState.Sleep:
                    //睡觉 缓慢恢复所有(除了心情,但是心情不会下降)
                    Core.Save!.StrengthChange(TimePass * 2);
                    Core.Save!.StrengthChangeFood(TimePass);
                    if (Core.Save!.StrengthFood <= sm25)
                    {
                        Core.Save!.StrengthChangeFood(TimePass);
                    }
                    else if (Core.Save!.StrengthFood >= sm75)
                        Core.Save!.Health += TimePass * 2;
                    Core.Save!.StrengthChangeDrink(TimePass);
                    if (Core.Save!.StrengthDrink >= sm25)
                    {
                        Core.Save!.StrengthChangeDrink(TimePass);
                    }
                    else if (Core.Save!.StrengthDrink >= sm75)
                        Core.Save!.Health += TimePass * 2;
                    LastInteractionTime = DateTime.Now;
                    break;
                case WorkingState.Work:
                    if (NowWork == null)
                        break;
                    var needfood = TimePass * NowWork.StrengthFood;
                    var needdrink = TimePass * NowWork.StrengthDrink;

                    double efficiency = 0;
                    int addhealth = -2;

                    var nsfood = needfood * .3;
                    var nsdrink = needdrink * .3;
                    if (Core.Save!.Strength > sm25 + nsfood + nsdrink)
                    {
                        Core.Save!.StrengthChange(-nsfood - nsdrink);
                        efficiency += 0.1;
                        needfood -= nsfood;
                        needdrink -= nsdrink;
                    }

                    if (Core.Save!.StrengthFood <= sm25)
                    {
                        Core.Save!.StrengthChangeFood(-needfood / 2);
                        efficiency += 0.2;
                        if (Core.Save!.Strength >= needfood)
                        {
                            Core.Save!.StrengthChange(-needfood);
                            efficiency += 0.1;
                        }
                        addhealth -= 2;
                    }
                    else
                    {
                        Core.Save!.StrengthChangeFood(-needfood);
                        efficiency += 0.4;
                        if (Core.Save!.StrengthFood >= sm60)
                        {
                            addhealth += Function.Rnd.Next(1, 3);
                            efficiency += 0.1;
                        }
                    }
                    if (Core.Save!.StrengthDrink <= sm25)
                    {
                        Core.Save!.StrengthChangeDrink(-needdrink / 2);
                        efficiency += 0.2;
                        if (Core.Save!.Strength >= needdrink)
                        {
                            Core.Save!.StrengthChange(-needdrink);
                            efficiency += 0.1;
                        }
                        addhealth -= 2;
                    }
                    else
                    {
                        Core.Save!.StrengthChangeDrink(-needdrink);
                        efficiency += 0.4;
                        if (Core.Save!.StrengthDrink >= sm60)
                        {
                            addhealth += Function.Rnd.Next(1, 3);
                            efficiency += 0.1;
                        }
                    }
                    if (addhealth > 0)
                        Core.Save!.Health += addhealth * TimePass;
                    var addmoney = Math.Max(0, TimePass * NowWork.MoneyBase * (2 * efficiency - 0.5));
                    if (NowWork.Type == Work.WorkType.Work)
                        Core.Save!.Money += addmoney;
                    else
                        Core.Save!.Exp += addmoney;
                    if (WorkTimer != null)
                        WorkTimer.GetCount += addmoney;
                    if (NowWork.Type == Work.WorkType.Play)
                    {
                        LastInteractionTime = DateTime.Now;
                        Core.Save!.FeelingChange(-NowWork.Feeling * TimePass);
                    }
                    else
                        Core.Save!.FeelingChange(-freedrop * (0.5 + NowWork.Feeling / 2));
                    break;
                default://默认
                    //饮食等乱七八糟的消耗
                    addhealth = -2;
                    if (Core.Save!.StrengthFood >= sm50)
                    {
                        Core.Save!.StrengthChangeFood(-TimePass);
                        Core.Save!.StrengthChange(TimePass);
                        if (Core.Save!.StrengthFood >= sm75)
                            addhealth += Function.Rnd.Next(1, 3);
                    }
                    else if (Core.Save!.StrengthFood <= sm25)
                    {
                        Core.Save!.Health -= Function.Rnd.NextDouble() * TimePass;
                        addhealth -= 2;
                    }
                    if (Core.Save!.StrengthDrink >= sm50)
                    {
                        Core.Save!.StrengthChangeDrink(-TimePass);
                        Core.Save!.StrengthChange(TimePass);
                        if (Core.Save!.StrengthDrink >= sm75)
                            addhealth += Function.Rnd.Next(1, 3);
                    }
                    else if (Core.Save!.StrengthDrink <= sm25)
                    {
                        Core.Save!.Health -= Function.Rnd.NextDouble() * TimePass;
                        addhealth -= 2;
                    }
                    if (addhealth > 0)
                        Core.Save!.Health += addhealth * TimePass;
                    Core.Save!.StrengthChangeFood(-TimePass);
                    Core.Save!.StrengthChangeDrink(-TimePass);
                    Core.Save!.FeelingChange(-freedrop);
                    break;
            }

            Core.Save!.Exp += TimePass;
            //感受提升好感度
            if (Core.Save!.Feeling >= Core.Save!.FeelingMax * 0.75)
            {
                if (Core.Save!.Feeling >= Core.Save!.FeelingMax * 0.90)
                {
                    Core.Save!.Likability += TimePass;
                }
                Core.Save!.Exp += TimePass * 2;
                Core.Save!.Health += TimePass;
            }
            else if (Core.Save!.Feeling <= 25)
            {
                Core.Save!.Likability -= TimePass;
                Core.Save!.Exp -= TimePass;
            }
            if (Core.Save!.StrengthDrink <= sm25)
            {
                Core.Save!.Health -= Function.Rnd.Next(0, 1) * TimePass;
                Core.Save!.Exp -= TimePass;
            }
            else if (Core.Save!.StrengthDrink >= sm75)
                Core.Save!.Health += Function.Rnd.Next(0, 1) * TimePass;

            FunctionSpendHandle?.Invoke();
            var newmod = Core.Save!.CalMode();
            if (Core.Save!.Mode != newmod)
            {
                //切换显示动画
                PlaySwitchAnimat(Core.Save!.Mode, newmod);

                Core.Save!.Mode = newmod;
            }
            //看情况播放停止工作动画
            if (Core.Save!.Mode == VPetAvalonia.Abstractions.Interfaces.ModeType.Ill && State == WorkingState.Work)
            {
                Dispatcher.Invoke(() => WorkTimer?.Stop(reason: FinishWorkInfo.StopReason.StateFail));
            }
        }

        /// <summary>
        /// 播放切换动画
        /// </summary>
        public void PlaySwitchAnimat(VPetAvalonia.Abstractions.Interfaces.ModeType before, VPetAvalonia.Abstractions.Interfaces.ModeType after)
        {
            if (!(DisplayType.Type == GraphInfo.GraphType.Default || DisplayType.Type == GraphInfo.GraphType.Switch_Down || DisplayType.Type == GraphInfo.GraphType.Switch_Up))
            {
                return;
            }
            if (before == after)
            {
                DisplayToNomal();
                return;
            }
            if (before < after)
            {
                Display(Core.Graph!.FindGraph(Core.Graph!.FindName(GraphInfo.GraphType.Switch_Down), GraphInfo.AnimatType.Single, before),
                    () => PlaySwitchAnimat((VPetAvalonia.Abstractions.Interfaces.ModeType)(((int)before) + 1), after));
            }
            else
            {
                Display(Core.Graph!.FindGraph(Core.Graph!.FindName(GraphInfo.GraphType.Switch_Up), GraphInfo.AnimatType.Single, before),
                    () => PlaySwitchAnimat((VPetAvalonia.Abstractions.Interfaces.ModeType)(((int)before) - 1), after));
            }
        }

        /// <summary>
        /// 状态计算Handle
        /// </summary>
        public event Action? FunctionSpendHandle;

        /// <summary>
        /// 想要随机显示的接口 (return:是否成功)
        /// </summary>
        public List<Func<bool>> RandomInteractionAction = new List<Func<bool>>();

        private bool isPress = false;

        /// <summary>成就周期检查计数</summary>
        private int _achCheckCounter;

        /// <summary>
        /// 判断是否是闲置状态
        /// </summary>
        public bool IsIdel => (DisplayType.Type == GraphInfo.GraphType.Default || DisplayType.Type == GraphInfo.GraphType.Work) && !isPress;

        /// <summary>
        /// 每隔指定时间自动触发计算
        /// </summary>
        public void EventTimer_Elapsed()
        {
            //所有Handle
            TimeHandle?.Invoke(this);
            if (Core.Controller!.EnableFunction)
            {
                FunctionSpend(0.05);
            }
            else
            {
                Core.Save!.Mode = ModeType.Nomal;
            }

            // 陪伴统计: 每个周期累加一次时长
            var periodSec = Math.Max(1, (long)(EventTimer.Interval / 1000));
            Stats?.AddPlaySeconds(periodSec);
            if (State == WorkingState.Work)
                Stats?.AddWorkSeconds(periodSec);
            else if (State == WorkingState.Sleep)
                Stats?.AddSleepSeconds(periodSec);

            // 成就检查: 峰值类(等级/金钱)每周期观察, 判定每 20 周期跑一次
            // (检查本身是遍历十几个 lambda, 没必要每周期都做)
            Stats?.ObserveSave(Core.Save);
            if (++_achCheckCounter >= 20)
            {
                _achCheckCounter = 0;
                Achievements.CheckAll(Stats, Core.Save);
            }

            //UIHandle
            Dispatcher.Invoke(() => TimeUIHandle?.Invoke(this));

            if (IsIdel)
            {
                // 说话优先: 说了就不再随机播动作, 否则两套动画互相打断
                if (TryRandomSay())
                    return;

                int rnddisplay = Math.Max(20, Core.Controller!.InteractionCycle - CountNomal);
                if (DisplayType.Type == GraphInfo.GraphType.Work)
                    rnddisplay = 2 * rnddisplay + 20;
                switch (Function.Rnd.Next(rnddisplay))
                {
                    case 0:
                    case 1:
                    case 2:
                        //显示移动(设置里关掉"自己走动"就改成原地待机, 免得它满屏乱爬)
                        if (Settings?.AllowRandomMove == false)
                            DisplayIdel();
                        else
                            DisplayMove();
                        break;
                    case 3:
                    case 4:
                    case 5:
                        //显示待机
                        DisplayIdel();
                        break;
                    case 6:
                        DisplayIdel_StateONE();
                        break;
                    case 7:
                        DisplaySleep();
                        break;
                    case 8:
                    case 9:
                    case 10:
                        //给其他显示留个机会
                        var list = RandomInteractionAction.ToList();
                        for (int i = Function.Rnd.Next(list.Count); 0 != list.Count; i = Function.Rnd.Next(list.Count))
                        {
                            var act = list[i];
                            if (act.Invoke())
                            {
                                break;
                            }
                            else
                            {
                                list.RemoveAt(i);
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 随机说话: 按设置里的频率做节流, 内容优先贴合当前处境(生病/饿/渴/累)。
        /// 返回 true 表示这一轮已经开口, 调用方应跳过随机动作 —— 说话本身会切到
        /// Say 类动画, 再叠一个移动/待机动画会互相打断。
        /// </summary>
        private bool TryRandomSay()
        {
            if (Settings is not { SayEnabled: true } || Core?.Save == null)
                return false;

            // 升级播报优先, 不受节流限制
            var level = Core.Save.Level;
            if (_lastLevel < 0)
            {
                _lastLevel = level;
            }
            else if (level > _lastLevel)
            {
                _lastLevel = level;
                SayRnd(SpeechPool.Pick(SpeechPool.Scene.LevelUp));
                return true;
            }

            if (++_sayTick < Math.Max(1, Settings.SayPeriod))
                return false;
            _sayTick = 0;

            // 到了周期也再随机拦一道, 免得说话像闹钟一样准时
            if (Function.Rnd.Next(3) != 0)
                return false;

            var line = SpeechPool.PickIdle(Core.Save);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            SayRnd(line);
            return true;
        }

        /// <summary>
        /// 当前状态
        /// </summary>
        public WorkingState State = WorkingState.Nomal;

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
        /// 获得工作列表分类
        /// </summary>
        public void WorkList(out List<Work> ws, out List<Work> ss, out List<Work> ps)
        {
            ws = new List<Work>();
            ss = new List<Work>();
            ps = new List<Work>();
            foreach (var w in Core.Graph!.GraphConfig.WorksList.OfType<Work>())
            {
                switch (w.Type)
                {
                    case Work.WorkType.Study:
                        ss.Add(w);
                        break;
                    case Work.WorkType.Work:
                        ws.Add(w);
                        break;
                    case Work.WorkType.Play:
                        ps.Add(w);
                        break;
                }
            }
        }

        /// <summary>
        /// 工作检测
        /// </summary>
        public Func<Work, bool>? WorkCheck;

        /// <summary>
        /// 开始工作
        /// </summary>
        public bool StartWork(Work? work)
        {
            if (work == null)
                return false;
            // 数据计算关闭时不做任何限制, 纯看动画
            if (Core.Controller!.EnableFunction)
            {
                if (Core.Save!.Mode == ModeType.Ill)
                {
                    ShowBubble("生病了没有力气工作, 先喂点药吧");
                    return false;
                }
                if (Core.Save!.Level < work.LevelLimit)
                {
                    ShowBubble($"等级不足, 需要 Lv.{work.LevelLimit}");
                    return false;
                }
            }
            if (State == WorkingState.Work && NowWork?.Name == work.Name)
            {
                // 重复点同一项 = 停止
                WorkTimer?.Stop(reason: FinishWorkInfo.StopReason.MenualStop);
                return false;
            }
            if (WorkCheck != null && !WorkCheck.Invoke(work))
                return false;
            // 关键接线: State/NowWork 置位后 FunctionSpend 的 Work 分支才会真正运转
            NowWork = work;
            State = WorkingState.Work;
            WorkTimer?.Start(work);
            // 播放工作/学习/玩耍动画 (A_Start → B_Loop 循环)。
            // 原版是 WorkTimer.Start 里的 work.Display(m), 移植后 WorkTimer 不是 IDisplayHost
            // 所以那里调了也白调, 工作状态下一路没有动画。这里由 Main 自己播。
            try
            {
                work.Display(this);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 工作动画播放失败: " + e.Message);
            }
            Event_WorkStartInvoke(work);
            return true;
        }

        /// <summary>
        /// 任务开始时调用该参数
        /// </summary>
        public event Action<Work>? Event_WorkStart;
        internal void Event_WorkStartInvoke(Work work)
        {
            Event_WorkStart?.Invoke(work);
        }

        /// <summary>
        /// 移动开始前(未播放动画)调用该参数
        /// </summary>
        public event Action<Move>? Event_MoveStart;
        /// <summary>
        /// 移动结束后(播放完动画)调用该参数
        /// </summary>
        public event Action<Move>? Event_MoveEnd;
        internal void Event_MoveStartInvoke(Move move)
        {
            Event_MoveStart?.Invoke(move);
        }
        internal void Event_MoveEndInvoke(Move move)
        {
            Event_MoveEnd?.Invoke(move);
        }

        #region 计时器启动/停止

        /// <summary>
        /// 启动主循环与气泡计时器
        /// 移植时这两个 Timer 只被创建却没有绑定 Elapsed 事件也没有 Start,
        /// 所以宠物永远不会自己动、数值也永远不会变化
        /// </summary>
        public void StartTimers()
        {
            EventTimer.Elapsed -= EventTimerElapsedHandler;
            EventTimer.Elapsed += EventTimerElapsedHandler;
            labeldisplaytimer.Elapsed -= LabelDisplayTimerElapsedHandler;
            labeldisplaytimer.Elapsed += LabelDisplayTimerElapsedHandler;
            EventTimer.Start();
        }

        /// <summary>
        /// 停止所有计时器
        /// </summary>
        public void StopTimers()
        {
            try
            {
                EventTimer.Stop();
                labeldisplaytimer.Stop();
                EventTimer.Elapsed -= EventTimerElapsedHandler;
                labeldisplaytimer.Elapsed -= LabelDisplayTimerElapsedHandler;
            }
            catch
            {
                // 退出阶段忽略
            }
        }

        private void EventTimerElapsedHandler(object? sender, ElapsedEventArgs e)
        {
            // 核心未就绪时不做任何事, 避免空引用刷屏
            if (Core?.Save == null || Core?.Graph == null)
                return;
            // System.Timers.Timer 在线程池线程触发, 而 Avalonia 不允许非 UI 线程
            // 访问控件("Call from invalid thread"), 整个主循环切到 UI 线程执行
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    EventTimer_Elapsed();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VPet] 主循环异常: " + ex.Message);
                }
            });
        }

        private void LabelDisplayTimerElapsedHandler(object? sender, ElapsedEventArgs e)
        {
            try
            {
                // 计数与累积值全部在 UI 线程上改: 定时器跑在线程池,
                // 而新提示是 UI 线程发出的, 分开改会和"刚弹出新提示"抢同一个计数
                Dispatcher.Invoke(() =>
                {
                    if (--labeldisplaycount <= 0)
                    {
                        labeldisplaytimer.Stop();
                        LabelDisplay.IsVisible = false;
                        // 关键: 弹窗消失时把累积值清零(对应原版 Main.xaml.cs 的行为)。
                        // 不清零的话, 下一次同文案的提示会接着上次的数字累加 ——
                        // 表现为弹窗写"体力-4 心情+2", 而数据面板实际只变了 -2/+1
                        labeldisplaychangenum1 = 0;
                        labeldisplaychangenum2 = 0;
                    }
                    else
                    {
                        // 最后 200ms 内淡出
                        LabelDisplay.Opacity = Math.Min(1.0, labeldisplaycount / 20.0);
                    }
                });
            }
            catch
            {
                // 忽略
            }
        }

        #endregion
    }
}