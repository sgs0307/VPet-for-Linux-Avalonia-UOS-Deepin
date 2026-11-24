using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VPetAvalonia.Abstractions.Interfaces;
using VPetAvalonia.Core;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 工作 / 学习 / 玩耍 面板 (对应原版 WinDesign/winWorkMenu)。
    ///
    /// v1.1.0 的入口是右键菜单里一串纯文本 "[工作] 文案 (60分, 约8/分)"。
    /// 数值结算本身是完整的(效率、体力/饱腹/口渴/心情消耗、健康、产出都照搬原版),
    /// 缺的是"选之前能不能看清代价"——等级够不够、要花多少体力、做完给多少奖励,
    /// 全得点进去才知道(等级不够时只弹一句"等级不足")。
    ///
    /// 这一版补齐:
    ///   · 三个分类(工作/学习/玩耍)分开, 带数量角标
    ///   · 每项显示 时长 / 收益速度 / 总收益 / 消耗 / 等级要求 / 完成奖励
    ///   · 等级不足压暗并写清"Lv.N 解锁", 按钮点不动(不再等点了才弹提示)
    ///   · ★ 收藏置顶 (workfavorites.txt, 与食物的 favorites.txt 分开)
    ///   · 正在进行的项显示实时进度与已赚取, 按钮变「停止」
    ///   · 底部常驻体力/饱腹/口渴, 低了直接提示"先吃点东西再干活"
    ///
    /// 和商店一致: 不是真窗口, 是 PetCanvas 里的 Border, 由 VisibleOverlays()
    /// 收进 XShape 输入区域。
    /// </summary>
    public partial class MainWindow
    {
        private Border? _workPanel;
        private StackPanel? _workBody;      // 列表(可滚动)
        private StackPanel? _workTools;     // 固定头部: 分类 + 收藏
        private StackPanel? _workFooter;    // 固定底部: 状态数值
        private StackPanel? _workTabRow;
        private TextBlock? _workStatusText;
        private TextBlock? _workFooterText;
        private DispatcherTimer? _workRefresh;

        private Work.WorkType _workFilter = Work.WorkType.Work;
        private bool _workOnlyStar;

        /// <summary>
        /// 上一次看到的"是否在干活 + 在干什么"签名。状态变了才重建列表——
        /// 每秒重建 13 行控件既浪费又会让滚动位置乱跳。
        /// (修的是: 从菜单/诊断口启动工作后, 列表行还停在"锁定/开始"的样子,
        ///  只有顶部状态行跟着变, 行内高亮和「停止」按钮出不来)
        /// </summary>
        private string _workRunSignature = "";

        /// <summary>工作项很少(vup.lps 里 13 条), 一次渲染完, 不需要分页</summary>
        private const double WorkPanelWidth = 440;
        private const double WorkPanelHeight = 600;

        private void ShowWorkPanel()
        {
            var core = MainDisplay.Core;
            if (core?.Graph == null)
                return;

            var all = AllWorks();
            if (all.Count == 0)
            {
                MainDisplay.ShowBubble("没有可用的工作配置 (mod/*/pet/*.lps 里的 work 条目)");
                return;
            }

            if (_workPanel == null)
            {
                _workPanel = CreateOverlayPanel("工作 / 学习", WorkPanelWidth, WorkPanelHeight,
                    out _workBody, out _workTools, out _workFooter, 78, 46);
                BuildWorkTools();
                PetCanvas.Children.Add(_workPanel);

                // 每秒刷新一次: 状态行总是刷, 列表只在"是否在干活/干什么"变化时重建
                _workRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _workRefresh.Tick += (s, e) =>
                {
                    if (_workPanel is not { IsVisible: true })
                        return;
                    var sig = WorkTimer.IsWorking ? "R:" + (WorkTimer.CurrentWork?.Name ?? "") : "-";
                    if (sig != _workRunSignature)
                    {
                        _workRunSignature = sig;
                        RefreshWorkPanel();
                        UpdateClickThrough();
                    }
                    else if (WorkTimer.IsWorking)
                    {
                        RefreshWorkStatus();
                    }
                };
            }

            // 默认停在"有内容"的分类上: 上次选的分类空了就落到第一个非空分类
            if (!all.Any(w => w.Type == _workFilter))
                _workFilter = all[0].Type;

            ShowOverlay(_workPanel, RefreshWorkPanel);
            _workRunSignature = WorkTimer.IsWorking ? "R:" + (WorkTimer.CurrentWork?.Name ?? "") : "-";
            _workRefresh?.Start();
        }

        /// <summary>当前 MOD 的全部工作配置</summary>
        private List<Work> AllWorks()
        {
            var core = MainDisplay.Core;
            if (core?.Graph == null)
                return new List<Work>();
            try
            {
                return core.Graph.GraphConfig.WorksList.OfType<Work>()
                    .Where(w => !string.IsNullOrEmpty(w.Name))
                    .ToList();
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取工作列表失败: " + e.Message);
                return new List<Work>();
            }
        }

        private static string TypeName(Work.WorkType t) => t switch
        {
            Work.WorkType.Study => "学习",
            Work.WorkType.Play => "玩耍",
            _ => "工作",
        };

        private static string TypeUnit(Work w) => w.Type == Work.WorkType.Work ? "$" : "经验";

        /// <summary>
        /// 典型效率 0.9 时的每分钟产出。原版每秒收入 = MoneyBase × (2×效率 − 0.5),
        /// 效率 0.9 → 1.3 × MoneyBase; 这里只做展示, 和实际逐秒结算不冲突。
        /// </summary>
        private static double EstimatePerMin(Work w) => Math.Max(0, w.MoneyBase * 1.3);

        /// <summary>估算整段工作做完能拿多少</summary>
        private static double EstimateTotal(Work w) => EstimatePerMin(w) * Math.Max(1, w.Time);

        private void BuildWorkTools()
        {
            if (_workTools == null)
                return;

            _workTabRow = new StackPanel { Orientation = Orientation.Horizontal };
            _workTools.Children.Add(_workTabRow);

            _workStatusText = new TextBlock
            {
                Foreground = TextAccent,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            _workTools.Children.Add(_workStatusText);
        }

        /// <summary>分类页签 + 只看收藏 (状态变化时只重建这一行)</summary>
        private void BuildWorkTabRow()
        {
            if (_workTabRow == null)
                return;
            _workTabRow.Children.Clear();

            var all = AllWorks();
            foreach (var t in new[] { Work.WorkType.Work, Work.WorkType.Study, Work.WorkType.Play })
            {
                var n = all.Count(w => w.Type == t);
                if (n == 0)
                    continue;
                var type = t;
                var btn = new Button
                {
                    Content = $"{TypeName(t)} {n}",
                    FontSize = 11,
                    Padding = new Thickness(9, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = _workFilter == t ? Brush("#AA0286C6") : Brush("#AA444444"),
                    Foreground = Brushes.White,
                };
                btn.Click += (s, e) =>
                {
                    _workFilter = type;
                    BuildWorkTabRow();
                    RefreshWorkPanel();
                    UpdateClickThrough();
                };
                _workTabRow.Children.Add(btn);
            }

            var star = MakeToggle(() => _workOnlyStar, v =>
            {
                _workOnlyStar = v;
                RefreshWorkPanel();
                UpdateClickThrough();
            }, "★ 只看收藏", "全部工作");
            star.Margin = new Thickness(6, 0, 0, 0);
            _workTabRow.Children.Add(star);
        }

        private void RefreshWorkPanel()
        {
            if (_workBody == null)
                return;

            BuildWorkTabRow();
            _workBody.Children.Clear();

            var core = MainDisplay.Core;
            var save = core?.Save;
            if (save == null)
                return;

            var list = AllWorks().Where(w => w.Type == _workFilter);
            if (_workOnlyStar)
                list = list.Where(w => WorkFavorites.Contains(w.Name));

            // 收藏置顶, 其余按等级要求从低到高(新手先看到能做的)
            var shown = list
                .OrderByDescending(w => WorkFavorites.Contains(w.Name))
                .ThenBy(w => w.LevelLimit)
                .ThenBy(w => w.Time)
                .ToList();

            if (shown.Count == 0)
            {
                _workBody.Children.Add(MakeNote(_workOnlyStar
                    ? "这个分类下还没有收藏。点每一项左边的 ☆ 就能收藏, 常用的置顶好找。"
                    : "这个分类下没有可用的工作。"));
                RefreshWorkStatus();
                return;
            }

            foreach (var work in shown)
                _workBody.Children.Add(BuildWorkRow(work, save));

            _workBody.Children.Add(MakeNote(
                "效率随体力/饱腹/口渴变化; 中途停下, 已赚到的不会回收。"));

            RefreshWorkStatus();
        }

        private Border BuildWorkRow(Work work, Core.IGameSave save)
        {
            // running 先算: 正在做的项不受"等级不足/生病"影响, 否则刚开完就显示成锁定态
            var running = WorkTimer.IsWorking && WorkTimer.CurrentWork?.Name == work.Name;
            var levelOk = save.Level >= work.LevelLimit || running;
            var ill = work.Type != Work.WorkType.Play && save.Mode == ModeType.Ill && !running;
            var enabled = levelOk && !ill;

            var row = new Border
            {
                Background = running ? Brush("#332E7D32") : Brushes.Transparent,
                Padding = new Thickness(4, 5),
                Margin = new Thickness(0, 0, 0, 2),
                BorderBrush = running ? Brush("#882E7D32") : Brush("#33FFFFFF"),
                BorderThickness = new Thickness(running ? 1 : 0, 0, running ? 1 : 0, 1),
                CornerRadius = new CornerRadius(4),
                Opacity = enabled || running ? 1.0 : 0.45,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // 星标
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star)); // 信息
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // 按钮

            // ---- 星标 ----
            var starBtn = new Button
            {
                Content = WorkFavorites.Contains(work.Name) ? "★" : "☆",
                FontSize = 15,
                Padding = new Thickness(7, 2),
                Background = Brushes.Transparent,
                Foreground = WorkFavorites.Contains(work.Name) ? Brushes.Gold : Brush("#777777"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            starBtn.Click += (s, e) =>
            {
                e.Handled = true;
                WorkFavorites.Toggle(work.Name);
                RefreshWorkPanel();
                UpdateClickThrough();
            };
            grid.Children.Add(starBtn);

            // ---- 名称 + 副信息 ----
            var info = new StackPanel { Margin = new Thickness(2, 0, 6, 0) };

            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock
            {
                Text = work.Name,
                Foreground = Brushes.White,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (running)
            {
                nameLine.Children.Add(new TextBlock
                {
                    Text = "进行中",
                    Foreground = Brush("#8BC34A"),
                    FontSize = 11,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
            info.Children.Add(nameLine);

            // 第一行: 时长 · 收益速度 · 总收益
            info.Children.Add(new TextBlock
            {
                Text = $"{work.Time} 分 · 约 {EstimatePerMin(work):0.#} {TypeUnit(work)}/分" +
                       $" · 做完约 {EstimateTotal(work):0} {TypeUnit(work)}",
                Foreground = Brush("#8A8A8A"),
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0),
            });

            // 第二行: 消耗 + 完成奖励 + 等级门槛
            var cost = new List<string>();
            if (work.StrengthFood > 0) cost.Add($"食物{work.StrengthFood:0.#}");
            if (work.StrengthDrink > 0) cost.Add($"水{work.StrengthDrink:0.#}");
            if (work.Feeling > 0) cost.Add($"心情{work.Feeling:0.#}");
            var extra = cost.Count > 0 ? "消耗 " + string.Join("/", cost) : "几乎不消耗";
            if (!levelOk)
                extra += $" · 需要 Lv.{work.LevelLimit}";
            if (ill)
                extra += " · 生病中不能做";

            info.Children.Add(new TextBlock
            {
                Text = extra,
                Foreground = levelOk ? Brush("#7A7A7A") : Brush("#C98A8A"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0),
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // ---- 右: 开始 / 停止 ----
            var action = new Button
            {
                FontSize = 11,
                Padding = new Thickness(11, 3),
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = running ? Brush("#AA8B3A3A")
                           : enabled ? Brush("#AA2E7D32") : Brush("#55333333"),
                Foreground = enabled || running ? Brushes.White : Brush("#888888"),
                Content = running ? "停止" : levelOk ? "开始" : $"Lv.{work.LevelLimit}",
                IsEnabled = enabled || running,
            };
            action.Click += (s, e) =>
            {
                e.Handled = true;
                StartOrStopWork(work);
            };
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);

            row.Child = grid;

            // 整行点击 = 开始/停止 (和"投喂""商店"一致的操作习惯)
            row.PointerPressed += (s, e) =>
            {
                if (e.Source is Visual v && (v is Button || VisualAncestorIsButton(v)))
                    return;
                if (!enabled && !running)
                    return;
                e.Handled = true;
                StartOrStopWork(work);
            };

            return row;
        }

        private static bool VisualAncestorIsButton(Visual v)
        {
            var cur = v;
            while (cur != null)
            {
                if (cur is Button)
                    return true;
                cur = cur.GetVisualParent();
            }
            return false;
        }

        /// <summary>
        /// 开始/停止一项工作。开始成功后收起面板 —— 桌宠动画要看得见。
        /// </summary>
        private void StartOrStopWork(Work work)
        {
            if (WorkTimer.IsWorking && WorkTimer.CurrentWork?.Name == work.Name)
            {
                WorkTimer.Stop(reason: FinishWorkInfo.StopReason.MenualStop);
                RefreshWorkPanel();
                UpdateClickThrough();
                return;
            }

            if (MainDisplay.StartWork(work))
            {
                Console.WriteLine($"[VPet] 开始{TypeName(work.Type)}: {work.Name} ({work.Time}分)");
                MainDisplay.ShowBubble($"开始{TypeName(work.Type)}「{work.Name}」\n" +
                                       $"{work.Time} 分钟, 做完约 {EstimateTotal(work):0} {TypeUnit(work)}");
                if (Settings.SayEnabled)
                    MainDisplay.SayRnd(SpeechPool.Pick(SpeechPool.Scene.Work));

                if (_workPanel != null)
                    _workPanel.IsVisible = false;
                UpdateClickThrough();
            }
            else
            {
                // StartWork 内部已经用气泡说明了原因(生病/等级), 这里只需把状态刷一遍
                RefreshWorkStatus();
                UpdateClickThrough();
            }
        }

        /// <summary>
        /// 顶部状态行 + 底部数值行 (每秒刷新, 不重建列表)
        /// </summary>
        private void RefreshWorkStatus()
        {
            var core = MainDisplay.Core;
            var save = core?.Save;
            if (save == null)
                return;

            if (_workStatusText != null)
            {
                if (WorkTimer.IsWorking && WorkTimer.CurrentWork != null)
                {
                    var w = WorkTimer.CurrentWork;
                    var done = Math.Min(WorkTimer.WorkMinutes, w.Time);
                    var left = Math.Max(0, w.Time - done);
                    _workStatusText.Text =
                        $"正在进行: {w.Name}  {done:0.0}/{w.Time} 分 (剩 {left:0.0} 分)" +
                        $"  已得 {WorkTimer.GetCount:0.#} {TypeUnit(w)}";
                }
                else
                {
                    _workStatusText.Text = save.Mode == ModeType.Ill
                        ? "现在生病了, 不能干活 —— 先吃点药。"
                        : "挑一项开始吧。数值计算关闭时只播动画, 不产出。";
                }
            }

            if (_workFooterText == null)
            {
                if (_workFooter == null)
                    return;
                _workFooterText = new TextBlock
                {
                    Foreground = TextDim,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                };
                _workFooter.Children.Add(_workFooterText);
            }

            var smax = save.StrengthMax > 0 ? save.StrengthMax : 100;
            var fPercent = save.StrengthFood / smax;
            var dPercent = save.StrengthDrink / smax;
            var warn = fPercent < 0.25 || dPercent < 0.25 || save.Strength < 30;

            _workFooterText.Text =
                $"体力 {save.Strength:0}  饱腹 {save.StrengthFood:0}  口渴 {save.StrengthDrink:0}  心情 {save.Feeling:0}" +
                (warn ? "   · 状态偏低, 干活效率会打折" : "");
            _workFooterText.Foreground = warn ? Brush("#FFB74D") : TextDim;
        }

        /// <summary>工作结束后由 OnWorkFinished 调用: 面板开着就顺手刷一遍</summary>
        internal void OnWorkFinishedRefreshPanel()
        {
            if (_workPanel is { IsVisible: true })
            {
                RefreshWorkPanel();
                UpdateClickThrough();
            }
        }
    }
}
