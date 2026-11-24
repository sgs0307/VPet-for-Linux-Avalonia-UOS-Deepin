using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 角色面板（旧版叫"数据面板"）。
    ///
    /// v1.4.0 之前这里是"一列到底"的文本清单：7 条属性都用同一个颜色的系统 ProgressBar、
    /// 数值右对齐不齐、能力/记录/年报全堆在同一列要滚动，而且**每秒整块 Clear 重建一次**
    /// —— 滚动位置每次都被打回顶部，鼠标悬停状态也每秒被抹掉。
    ///
    /// 现在改成：
    ///   · 头部固定：名字 + 等级徽章 + 状态徽章 + 右上角 ×；下面一排页签（概览 / 记录 / 年报）
    ///   · 概览：4 张数值卡 + 7 条自绘属性条（颜色按属性语义区分，低于 25% 转红并给出说明）
    ///   · 属性行可悬停：鼠标移上去高亮，底部提示条给出"这个数值怎么涨、怎么掉"
    ///   · 控件只建一次，每秒只改文本和条宽 —— 滚动不跳、悬停不丢
    /// </summary>
    public partial class MainWindow
    {
        private Border? _charPanel;
        private StackPanel? _charBody;
        private DispatcherTimer? _charTimer;

        // 三个页各自缓存, 切换只改 IsVisible(不重建 -> 滚动位置和悬停都保留)
        private StackPanel? _charPageOverview;
        private StackPanel? _charPageRecord;
        private StackPanel? _charPageYear;
        private int _charTab;

        private readonly List<Border> _charTabs = new();
        private readonly List<TextBlock> _charTabTexts = new();
        private readonly List<CharMeter> _charMeters = new();

        private TextBlock? _charName;
        private TextBlock? _charLv;
        private TextBlock? _charStateText;
        private Border? _charStateBadge;
        private TextBlock? _charMoney;
        private TextBlock? _charBag;
        private TextBlock? _charDays;
        private TextBlock? _charAch;
        private TextBlock? _charHint;

        // ---- 配色 ----
        private static readonly IBrush CardBg = Brush("#14FFFFFF");
        private static readonly IBrush CardBgHover = Brush("#2EFFFFFF");
        private static readonly IBrush Danger = Brush("#EF5350");
        private static readonly IBrush TabOnBg = Brush("#330286C6");
        private static readonly IBrush ChipText = Brush("#E6FFFFFF");

        // 属性条配色：一个属性一个颜色，扫一眼就能分清，不用读文字
        private static readonly IBrush CGold = Brush("#FFD54F");     // 经验 金
        private static readonly IBrush CStrength = Brush("#FF8A65"); // 体力 橙红
        private static readonly IBrush CFood = Brush("#FFB74D");     // 饱腹 橙
        private static readonly IBrush CDrink = Brush("#4FC3F7");    // 口渴 浅蓝
        private static readonly IBrush CFeeling = Brush("#F06292");  // 心情 粉
        private static readonly IBrush CHealth = Brush("#66BB6A");   // 健康 绿
        private static readonly IBrush CLike = Brush("#BA68C8");     // 好感 紫

        /// <summary>
        /// 一条属性条：轨道 + 填充 + 数值。控件建一次，之后只改宽度和文本。
        /// </summary>
        private sealed class CharMeter
        {
            public string Name = "";
            public string Hint = "";
            public IBrush Accent = Brushes.White;
            /// <summary>低于 25% 是否告警(生存类属性才需要, 经验/好感从 0 起步是正常的)</summary>
            public bool Warn;
            public TextBlock Value = null!;
            public Border Fill = null!;
            public ColumnDefinition FillCol = null!;
            public ColumnDefinition RestCol = null!;
            public Border Row = null!;

            /// <summary>更新数值: 条宽按比例, 低于 25% 转红</summary>
            public void Set(double v, double max)
            {
                var pct = max > 0 ? Math.Clamp(v / max * 100, 0, 100) : 0;
                // 用两段星号列表示比例, 不用像素宽度 —— 面板缩放时不用重算
                FillCol.Width = new GridLength(Math.Max(0.01, pct), GridUnitType.Star);
                RestCol.Width = new GridLength(Math.Max(0.01, 100 - pct), GridUnitType.Star);

                var low = Warn && pct <= 25;
                Fill.Background = low ? Danger : Accent;
                Value.Text = max > 0 ? $"{v:F0}/{max:F0}" : $"{v:F0}";
                Value.Foreground = low ? Danger : ChipText;
            }
        }

        // ================= 打开 / 构建 =================

        private void ShowCharacterPanel()
        {
            if (_charPanel == null)
                BuildCharacterPanel();
            ShowOverlay(_charPanel!, UpdateCharacterPanel);
            _charTimer?.Start();
        }

        private void BuildCharacterPanel()
        {
            _charPanel = CreatePanel("角色面板", 440, 690,
                                     out _charBody, out var header, out _, out _,
                                     headerHeight: 96, showTitle: false, showClose: false);
            PetCanvas.Children.Add(_charPanel);

            BuildHeader(header);

            _charPageOverview = new StackPanel();
            _charPageRecord = new StackPanel { IsVisible = false, MinHeight = 420 };
            _charPageYear = new StackPanel { IsVisible = false, MinHeight = 420 };
            _charBody!.Children.Add(_charPageOverview);
            _charBody.Children.Add(_charPageRecord);
            _charBody.Children.Add(_charPageYear);

            BuildOverviewPage(_charPageOverview);

            SwitchTab(0);

            // 面板开着时每秒只刷新数值(不重建控件)
            _charTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _charTimer.Tick += (s, e) =>
            {
                if (_charPanel is { IsVisible: true })
                    UpdateCharacterPanel();
            };
        }

        /// <summary>固定头部: 名字 + 等级 + 状态 + 关闭, 下面一排页签</summary>
        private void BuildHeader(StackPanel header)
        {
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            titleRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _charName = new TextBlock
            {
                Text = "角色",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            left.Children.Add(_charName);

            // 等级徽章
            _charLv = new TextBlock
            {
                Text = "Lv 1",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            left.Children.Add(new Border
            {
                Background = Brush("#330286C6"),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _charLv,
            });

            // 状态徽章(颜色随状态变)
            _charStateText = new TextBlock
            {
                Text = "发呆中",
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _charStateBadge = new Border
            {
                Background = Brush("#33FFFFFF"),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _charStateText,
            };
            left.Children.Add(_charStateBadge);

            Grid.SetColumn(left, 0);
            titleRow.Children.Add(left);

            // 右上角 ×（外壳的"关闭"按钮已关掉, 改成这里, 少一行高度）
            var closeGlyph = new TextBlock
            {
                Text = "✕",
                FontSize = 14,
                Foreground = TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var close = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = closeGlyph,
            };
            close.PointerEntered += (s, e) =>
            {
                close.Background = Brush("#33FFFFFF");
                closeGlyph.Foreground = Brushes.White;
            };
            close.PointerExited += (s, e) =>
            {
                close.Background = Brushes.Transparent;
                closeGlyph.Foreground = TextDim;
            };
            close.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                if (_charPanel != null)
                    _charPanel.IsVisible = false;
                UpdateClickThrough();
            };
            Grid.SetColumn(close, 1);
            titleRow.Children.Add(close);
            header.Children.Add(titleRow);

            // 页签
            var tabBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 2),
                Spacing = 6,
            };
            string[] names = { "概览", "记录", "年报" };
            for (var i = 0; i < names.Length; i++)
            {
                var idx = i;
                var text = new TextBlock
                {
                    Text = names[i],
                    FontSize = 13,
                    Foreground = TextDim,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var tab = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 4),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = text,
                };
                tab.PointerEntered += (s, e) =>
                {
                    if (_charTab != idx)
                        tab.Background = Brush("#14FFFFFF");
                };
                tab.PointerExited += (s, e) =>
                {
                    if (_charTab != idx)
                        tab.Background = Brushes.Transparent;
                };
                tab.PointerPressed += (s, e) =>
                {
                    e.Handled = true;
                    SwitchTab(idx);
                };

                _charTabs.Add(tab);
                _charTabTexts.Add(text);
                tabBar.Children.Add(tab);
            }
            header.Children.Add(tabBar);
        }

        private void SwitchTab(int tab)
        {
            _charTab = tab;
            if (_charPageOverview != null) _charPageOverview.IsVisible = tab == 0;
            if (_charPageRecord != null) _charPageRecord.IsVisible = tab == 1;
            if (_charPageYear != null) _charPageYear.IsVisible = tab == 2;

            for (var i = 0; i < _charTabs.Count; i++)
            {
                var on = i == tab;
                _charTabs[i].Background = on ? TabOnBg : Brushes.Transparent;
                _charTabTexts[i].Foreground = on ? Brushes.White : TextDim;
                _charTabTexts[i].FontWeight = on ? FontWeight.Bold : FontWeight.Normal;
            }

            if (tab != 0)
                RebuildPage(tab);
            UpdateClickThrough();
        }

        // ================= 概览页 =================

        private void BuildOverviewPage(StackPanel page)
        {

            // ---- 4 张数值卡 ----
            var cards = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            cards.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            cards.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            cards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Control Place(Control c, int col, int row)
            {
                c.Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 0 ? 4 : 0, row == 0 ? 6 : 0);
                Grid.SetColumn(c, col);
                Grid.SetRow(c, row);
                cards.Children.Add(c);
                return c;
            }

            Place(MakeStatCard("金钱", out _charMoney, CGold, prefix: "$"), 0, 0);
            Place(MakeStatCard("背包", out _charBag, Brush("#81D4FA"), suffix: "份"), 1, 0);
            Place(MakeStatCard("相伴", out _charDays, Brush("#FFB74D"), suffix: "天"), 0, 1);
            Place(MakeStatCard("成就", out _charAch, Brush("#BA68C8")), 1, 1);
            page.Children.Add(cards);

            // ---- 属性 ----
            page.Children.Add(MakeSection("属性"));
            AddMeter("经验", CGold, "攒够就升级：吃东西、干活、摸头都会给。升级会提升体力/心情上限。", warn: false);
            AddMeter("体力", CStrength, "干活、走动都会掉；睡觉或投喂恢复。掉到 25% 以下干活效率打折。");
            AddMeter("饱腹", CFood, "投喂正餐涨得快，零食涨得慢。一直饿着会掉健康。");
            AddMeter("口渴", CDrink, "投喂饮料类恢复。口渴见底比饿肚子掉健康更快。");
            AddMeter("心情", CFeeling, "摸头、玩耍、吃喜欢的食物都会涨；长时间不理会会慢慢掉。");
            AddMeter("健康", CHealth, "体力/饱腹/口渴长期见底就会掉，掉到 0 会生病 —— 生病时要先治再喂。");
            AddMeter("好感度", CLike, "互动累积，上限随等级提升；好感度越高，随机台词越亲昵。", warn: false);

            // ---- 能力 ----
            page.Children.Add(MakeSection("能力"));
            var abil = new Grid();
            abil.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            abil.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            abil.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            abil.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Control Put(Control c, int col, int row)
            {
                c.Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 0 ? 4 : 0, 4);
                Grid.SetColumn(c, col);
                Grid.SetRow(c, row);
                abil.Children.Add(c);
                return c;
            }

            Put(MakeChip("体力上限", out _chipStrengthMax), 0, 0);
            Put(MakeChip("心情上限", out _chipFeelingMax), 1, 0);
            Put(MakeChip("好感上限", out _chipLikeMax), 0, 1);
            Put(MakeChip("经验加成", out _chipExpBonus), 1, 1);
            page.Children.Add(abil);

            // ---- 悬停提示条 ----
            _charHint = new TextBlock
            {
                Text = "把鼠标移到属性上，这里会说明它怎么涨、怎么掉。",
                Foreground = Brush("#88FFFFFF"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 30,
                Margin = new Thickness(0, 10, 0, 2),
            };
            page.Children.Add(_charHint);
            _charHintDefault = _charHint.Text;
        }

        private TextBlock? _chipStrengthMax;
        private TextBlock? _chipFeelingMax;
        private TextBlock? _chipLikeMax;
        private TextBlock? _chipExpBonus;
        private string _charHintDefault = "";

        /// <summary>一张数值卡：大数字 + 小标签（数字用 TextBlock 引用, 每秒只改 Text）</summary>
        private static Control MakeStatCard(string label, out TextBlock value, IBrush color, string prefix = "", string suffix = "")
        {
            var v = new TextBlock
            {
                Text = "-",
                FontSize = 19,
                FontWeight = FontWeight.Bold,
                Foreground = color,
            };
            value = v;

            var sp = new StackPanel();
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            if (prefix.Length > 0)
            {
                line.Children.Add(new TextBlock
                {
                    Text = prefix,
                    FontSize = 13,
                    Foreground = Brush("#99FFFFFF"),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 1, 3),
                });
            }
            line.Children.Add(v);
            if (suffix.Length > 0)
            {
                line.Children.Add(new TextBlock
                {
                    Text = " " + suffix,
                    FontSize = 11,
                    Foreground = Brush("#99FFFFFF"),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3),
                });
            }
            sp.Children.Add(line);
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = TextDim,
                Margin = new Thickness(0, 1, 0, 0),
            });

            return new Border
            {
                Background = CardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Child = sp,
            };
        }

        /// <summary>能力小卡：左标签右数值</summary>
        private static Control MakeChip(string label, out TextBlock value)
        {
            value = new TextBlock
            {
                Text = "-",
                FontSize = 13,
                Foreground = ChipText,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            g.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = TextDim,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(value, 1);
            g.Children.Add(value);

            return new Border
            {
                Background = CardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                Child = g,
            };
        }

        /// <summary>加一条属性条(带悬停高亮 + 底部说明)</summary>
        private void AddMeter(string label, IBrush accent, string hint, bool warn = true)
        {
            var valueText = new TextBlock
            {
                Text = "-",
                Foreground = ChipText,
                FontSize = 12,
                Width = 74,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var fillCol = new ColumnDefinition(50, GridUnitType.Star);
            var restCol = new ColumnDefinition(50, GridUnitType.Star);
            var fill = new Border { Background = accent, CornerRadius = new CornerRadius(4) };

            var seg = new Grid();
            seg.ColumnDefinitions.Add(fillCol);
            seg.ColumnDefinitions.Add(restCol);
            seg.Children.Add(fill);

            var bar = new Grid { Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            bar.Children.Add(new Border { Background = UiTrackBg, CornerRadius = new CornerRadius(4) });
            bar.Children.Add(seg);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(48, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TextDim,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            Grid.SetColumn(valueText, 2);
            row.Children.Add(valueText);

            var meter = new CharMeter
            {
                Name = label,
                Hint = hint,
                Accent = accent,
                Warn = warn,
                Value = valueText,
                Fill = fill,
                FillCol = fillCol,
                RestCol = restCol,
            };

            // 悬停整行高亮 + 底部换说明文字(面板没有系统 ToolTip, 自绘更稳)
            var box = new Border
            {
                Child = row,
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 4),
            };
            box.PointerEntered += (s, e) =>
            {
                box.Background = CardBgHover;
                if (_charHint != null)
                    _charHint.Text = hint;
            };
            box.PointerExited += (s, e) =>
            {
                box.Background = Brushes.Transparent;
                if (_charHint != null)
                    _charHint.Text = _charHintDefault;
            };
            meter.Row = box;
            _charMeters.Add(meter);
            _charPageOverview?.Children.Add(box);
        }

        // ================= 记录 / 年报 =================

        private void RebuildPage(int tab)
        {
            var page = tab == 1 ? _charPageRecord : _charPageYear;
            if (page == null)
                return;
            page.Children.Clear();
            if (tab == 1)
                FillRecordPage(page);
            else
                FillYearPage(page);
        }

        private void FillRecordPage(StackPanel page)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            void Put(string label, string value, IBrush color, int col, int row)
            {
                var card = MakeStatCard(label, out var v, color, "");
                v.Text = value;
                v.FontSize = 16;
                card.Margin = new Thickness(col == 0 ? 0 : 4, 2, col == 0 ? 4 : 0, 6);
                Grid.SetColumn(card, col);
                Grid.SetRow(card, row);
                g.Children.Add(card);
            }

            Put("摸头 · 摸身体", $"{Stats.TouchCount} 次", Brush("#81D4FA"), 0, 0);
            Put("捏脸", $"{Stats.PinchCount} 次", Brush("#F06292"), 1, 0);
            Put("投喂", $"{Stats.FeedCount} 次", Brush("#FFB74D"), 0, 1);
            Put("完成工作", $"{Stats.WorkFinishCount} 次", Brush("#66BB6A"), 1, 1);
            Put("已经陪你", CharacterStats.HumanDuration(Stats.PlaySeconds), CGold, 0, 2);
            Put("一起干活", CharacterStats.HumanDuration(Stats.WorkSeconds), Brush("#BA68C8"), 1, 2);

            page.Children.Add(g);

            page.Children.Add(MakeSection("时间线"));
            page.Children.Add(MakeRow("初次见面", new TextBlock
            {
                Text = Stats.FirstSeen.ToString("yyyy-MM-dd"),
                Foreground = ChipText,
                FontSize = 13,
            }));
            page.Children.Add(MakeRow("相伴天数", new TextBlock
            {
                Text = $"{Math.Max(1, (int)(DateTime.Now.Date - Stats.FirstSeen.Date).TotalDays + 1)} 天",
                Foreground = ChipText,
                FontSize = 13,
            }));
            page.Children.Add(MakeNote("摸头/捏脸/投喂统计来自本机 stats.txt，删档不清零。"));
        }

        private void FillYearPage(StackPanel page)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            var total = MakeStatCard("互动总数", out var totalV, CGold, suffix: "次");
            totalV.Text = $"{Stats.InteractionTotal}";
            totalV.FontSize = 20;
            total.Margin = new Thickness(0, 2, 4, 0);

            var food = MakeStatCard("最常吃", out var foodV, Brush("#FFB74D"), "");
            foodV.Text = string.IsNullOrEmpty(Stats.TopFood) ? "还没吃过" : Stats.TopFood;
            foodV.FontSize = string.IsNullOrEmpty(Stats.TopFood) ? 13 : 15;
            food.Margin = new Thickness(4, 2, 0, 0);

            Grid.SetColumn(total, 0);
            Grid.SetColumn(food, 1);
            row.Children.Add(total);
            row.Children.Add(food);
            page.Children.Add(row);

            page.Children.Add(MakeSection("这一年"));
            page.Children.Add(MakeRow("睡得最久", ValueText(CharacterStats.HumanDuration(Stats.SleepSeconds))));
            page.Children.Add(MakeRow("最常吃", ValueText(
                string.IsNullOrEmpty(Stats.TopFood) ? "还没吃过东西" : $"{Stats.TopFood} ×{Stats.TopFoodCount}")));
            page.Children.Add(MakeRow("最高等级", ValueText($"Lv {Math.Max(Stats.MaxLevel, MainDisplay.Core?.Save?.Level ?? 0)}")));
            page.Children.Add(MakeRow("最有钱时", ValueText($"${Math.Max(Stats.MaxMoney, MainDisplay.Core?.Save?.Money ?? 0):0.#}")));
            page.Children.Add(MakeRow("完成工作", ValueText($"{Stats.WorkFinishCount} 次")));
            page.Children.Add(MakeRow("成就", ValueText($"{Achievements.Unlocked.Count} / {Achievements.Defs.Length}")));

            page.Children.Add(MakeNote("统计不会随删档清空，想归零去「设置 → 数据 → 清除陪伴统计」。"));
        }

        private static TextBlock ValueText(string text) => new()
        {
            Text = text,
            Foreground = ChipText,
            FontSize = 13,
        };

        // ================= 每秒刷新 =================

        /// <summary>
        /// 只改文本与条宽, 不重建控件(重建会让 ScrollViewer 回到顶部、鼠标悬停状态丢失)
        /// </summary>
        private void UpdateCharacterPanel()
        {
            var core = MainDisplay.Core;
            var save = core?.Save;
            if (_charBody == null || save == null)
                return;

            var days = Math.Max(1, (int)(DateTime.Now.Date - Stats.FirstSeen.Date).TotalDays + 1);

            if (_charName != null) _charName.Text = save.Name;
            if (_charLv != null) _charLv.Text = $"Lv {save.Level}";
            if (_charMoney != null) _charMoney.Text = $"{save.Money:0.#}";
            if (_charBag != null) _charBag.Text = $"{core?.Inventory?.TotalCount ?? 0}";
            if (_charDays != null) _charDays.Text = $"{days}";
            if (_charAch != null) _charAch.Text = $"{Achievements.Unlocked.Count}/{Achievements.Defs.Length}";

            // 状态徽章: 生病优先, 其次看主循环在干什么
            if (_charStateText != null && _charStateBadge != null)
            {
                if (save.Mode == ModeType.Ill)
                {
                    _charStateText.Text = "生病中";
                    _charStateBadge.Background = Brush("#B3EF5350");
                }
                else
                {
                    (_charStateText.Text, _charStateBadge.Background) = MainDisplay.State switch
                    {
                        Main.WorkingState.Work => ("正在干活", Brush("#B366BB6A")),
                        Main.WorkingState.Sleep => ("睡觉中", Brush("#B37986CB")),
                        Main.WorkingState.Travel => ("出门了", Brush("#B34DD0E1")),
                        _ => ("发呆中", Brush("#33FFFFFF")),
                    };
                }
            }

            // 属性条
            var need = save.LevelUpNeed();
            foreach (var m in _charMeters)
            {
                switch (m.Name)
                {
                    case "经验": m.Set(save.Exp, need); break;
                    case "体力": m.Set(save.Strength, save.StrengthMax); break;
                    case "饱腹": m.Set(save.StrengthFood, save.StrengthMax); break;
                    case "口渴": m.Set(save.StrengthDrink, save.StrengthMax); break;
                    case "心情": m.Set(save.Feeling, save.FeelingMax); break;
                    case "健康": m.Set(save.Health, 100); break;
                    case "好感度": m.Set(save.Likability, save.LikabilityMax); break;
                }
            }

            if (_chipStrengthMax != null) _chipStrengthMax.Text = $"{save.StrengthMax:F0}";
            if (_chipFeelingMax != null) _chipFeelingMax.Text = $"{save.FeelingMax:F0}";
            if (_chipLikeMax != null) _chipLikeMax.Text = $"{save.LikabilityMax:F0}";
            if (_chipExpBonus != null) _chipExpBonus.Text = $"×{save.ExpBonus:F2}";

            // 记录/年报页内容短、不需要滚动, 直接重建最省事(不会出现滚动跳位)
            if (_charTab != 0)
                RebuildPage(_charTab);
        }
    }
}
