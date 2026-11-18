using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// MainWindow 的面板部分: 设置面板 / 角色面板。
    ///
    /// 桌宠窗口是全屏透明窗, 只能靠 XShape 抠出可点区域, 所以面板不能是真的独立窗口
    /// (那样会挡住桌面点击)。这里的做法和商店一致: 往 PetCanvas 里塞一个 Border,
    /// 再把它算进输入区域(UpdateClickThrough)。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>运行设置 (settings.txt)</summary>
        internal GameSettings Settings = new();

        /// <summary>陪伴统计 (stats.txt)</summary>
        internal CharacterStats Stats = new();

        private Border? _settingsPanel;
        private StackPanel? _settingsBody;
        // 角色面板的字段与实现挪到 MainWindow.CharacterPanel.cs (v1.4.0 重做 UI)

        private static readonly IBrush PanelBg = Brush("#F22B2B2B");
        private static readonly IBrush PanelBorder = Brush("#555555");
        private static readonly IBrush TextMain = Brushes.White;
        private static readonly IBrush TextDim = Brush("#BBBBBB");
        private static readonly IBrush TextAccent = Brush("#81D4FA");
        private static readonly IBrush BtnBg = Brush("#AA0286C6");

        private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

        /// <summary>所有叠加面板, 供 UpdateClickThrough 统一算输入区域</summary>
        private IEnumerable<Border> VisibleOverlays()
        {
            if (_settingsPanel is { IsVisible: true }) yield return _settingsPanel;
            if (_charPanel is { IsVisible: true }) yield return _charPanel;
            if (_invPanel is { IsVisible: true }) yield return _invPanel;
            if (_achPanel is { IsVisible: true }) yield return _achPanel;
            // 商店也从 MainWindow.axaml.cs 挪到这里统一管理(见 MainWindow.ShopPanel.cs)
            if (_shopPanel is { IsVisible: true }) yield return _shopPanel;
            // 工作/学习面板 (MainWindow.WorkPanel.cs)
            if (_workPanel is { IsVisible: true }) yield return _workPanel;
        }

        /// <summary>
        /// 面板统一外观: 标题 + 可滚动内容 + 关闭按钮
        /// </summary>
        private Border CreateOverlayPanel(string title, double width, double height, out StackPanel body)
            => CreateOverlayPanel(title, width, height, out body, out _, out _, 0, 0);

        private Border CreateOverlayPanel(string title, double width, double height,
                                          out StackPanel body, out StackPanel header,
                                          double headerHeight = 0)
            => CreateOverlayPanel(title, width, height, out body, out header, out _, headerHeight, 0);

        /// <summary>
        /// 同上, 额外给一个"固定头部"(不随内容滚动) 和 "固定底部"(翻页/统计条)。
        /// 商店的搜索框如果塞进可滚动区, 往下翻两屏就找不到了;
        /// 翻页按钮同理, 塞进列表末尾会随内容滚出视野。
        ///
        /// showTitle=false / closeButton=false 供"自己画头部"的面板用(角色面板):
        /// 它把标题、状态徽章、页签和右上角 × 都放进固定头部, 不需要外壳再画一遍。
        /// </summary>
        private Border CreateOverlayPanel(string title, double width, double height,
                                          out StackPanel body, out StackPanel header, out StackPanel footer,
                                          double headerHeight = 0, double footerHeight = 0,
                                          bool showTitle = true, bool closeButton = true)
        {
            var root = new Border
            {
                Width = width,
                MaxHeight = height,
                Background = PanelBg,
                BorderBrush = PanelBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                IsVisible = false,
            };

            var stack = new StackPanel();

            var headGrid = new Grid();
            headGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            headGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            if (showTitle)
            {
                var titleText = new TextBlock
                {
                    Text = title,
                    Foreground = TextMain,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                };
                headGrid.Children.Add(titleText);
                stack.Children.Add(headGrid);
            }

            header = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            stack.Children.Add(header);

            body = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            stack.Children.Add(new ScrollViewer
            {
                // 减掉头部/底部高度, 否则固定区 + 滚动区一起把"关闭"按钮推出面板
                MaxHeight = Math.Max(120, height - 100 - headerHeight - footerHeight),
                Content = body,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            });

            footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(footer);

            if (closeButton)
            {
                var close = new Button
                {
                    Content = "关闭",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                    Background = BtnBg,
                    Foreground = Brushes.White,
                    Padding = new Thickness(14, 4),
                };
                close.Click += (s, e) =>
                {
                    root.IsVisible = false;
                    UpdateClickThrough();
                };
                stack.Children.Add(close);
            }

            root.Child = stack;
            // 面板刚显示时 Bounds 还是 0, 布局完成后要重算输入区域
            root.LayoutUpdated += (s, e) =>
            {
                if (root.IsVisible)
                    UpdateClickThrough();
            };
            return root;
        }

        /// <summary>
        /// 一行"标签 | 控件"
        /// </summary>
        private static Control MakeRow(string label, Control right, string? hint = null)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TextDim,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(right, 1);
            right.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(right);
            panel.Children.Add(row);

            if (!string.IsNullOrEmpty(hint))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = hint,
                    Foreground = Brush("#888888"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 0),
                });
            }
            return panel;
        }

        /// <summary>
        /// 开关: 点一下切换, 文案跟着变（不用 CheckBox, 无主题环境下更稳）
        /// </summary>
        private static Control MakeToggle(Func<bool> get, Action<bool> set, string onText = "已开启", string offText = "已关闭")
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 5),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            var text = new TextBlock { FontSize = 13, Foreground = Brushes.White };

            void Refresh()
            {
                var on = get();
                text.Text = on ? onText : offText;
                border.Background = on ? Brush("#AA2E7D32") : Brush("#AA555555");
            }

            border.Child = text;
            border.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                set(!get());
                Refresh();
            };
            Refresh();
            return border;
        }

        /// <summary>
        /// 步进器: [-] 数值 [+] , 自动夹紧范围
        /// </summary>
        private static Control MakeStepper(Func<int> get, Action<int> set, int step, int min, int max,
                                           Func<int, string> format)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            var value = new TextBlock
            {
                Foreground = TextAccent,
                FontSize = 14,
                Width = 78,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Button MakeBtn(string caption)
            {
                var b = new Button
                {
                    Content = caption,
                    Width = 28,
                    Height = 26,
                    Padding = new Thickness(0),
                    Background = Brush("#AA444444"),
                    Foreground = Brushes.White,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                return b;
            }

            var minus = MakeBtn("−");
            var plus = MakeBtn("+");

            void Refresh() => value.Text = format(get());

            minus.Click += (s, e) => { set(Math.Max(min, get() - step)); Refresh(); };
            plus.Click += (s, e) => { set(Math.Min(max, get() + step)); Refresh(); };

            panel.Children.Add(minus);
            panel.Children.Add(value);
            panel.Children.Add(plus);
            Refresh();
            return panel;
        }

        /// <summary>
        /// 属性条: 名称 + 进度 + 数值
        /// </summary>
        private static Control MakeMeter(string label, double value, double max, string valueText)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition(52, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(74, GridUnitType.Pixel));

            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TextDim,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = max > 0 ? Math.Max(0, Math.Min(100, value / max * 100)) : 0,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0),
            };
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);

            var txt = new TextBlock
            {
                Text = valueText,
                Foreground = TextAccent,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(txt, 2);
            row.Children.Add(txt);
            return row;
        }

        private static Control MakeSection(string title)
        {
            return new TextBlock
            {
                Text = title,
                Foreground = Brush("#FFB74D"),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 12, 0, 2),
            };
        }

        private static Control MakeNote(string text, IBrush? brush = null)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = brush ?? Brush("#888888"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
        }

        /// <summary>
        /// 普通按钮(执行一个动作)。confirm=true 时第一次点击只是"上膛", 3 秒内再点一次才真执行
        /// —— 桌宠面板没有系统对话框, 危险操作(清统计)不能用一问一答, 用二次点击最省事。
        /// </summary>
        private Control MakeActionButton(string text, Action act, bool confirm = false)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 12,
                Padding = new Thickness(12, 3),
                Background = BtnBg,
                Foreground = Brushes.White,
            };

            var armed = false;
            DispatcherTimer? timer = null;

            void Disarm()
            {
                armed = false;
                timer?.Stop();
                timer = null;
                btn.Content = text;
                btn.Background = BtnBg;
            }

            btn.Click += (s, e) =>
            {
                if (confirm && !armed)
                {
                    armed = true;
                    btn.Content = "再点一次确认";
                    btn.Background = Brush("#AAC62828");
                    timer?.Stop();
                    timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    timer.Tick += (a, b) => Disarm();
                    timer.Start();
                    return;
                }

                Disarm();
                try
                {
                    act();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VPet] 操作失败: " + ex.Message);
                    MainDisplay.ShowBubble("操作失败: " + ex.Message);
                }
            };
            return btn;
        }

        /// <summary>在文件管理器里打开存档目录</summary>
        private void OpenDataDir()
        {
            var path = System.IO.Path.GetDirectoryName(VPetAvalonia.Core.GameSave.SavePath);
            if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
            {
                MainDisplay.ShowBubble("找不到数据目录");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", path)
                {
                    UseShellExecute = false,
                });
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 打开目录失败: " + e.Message);
                MainDisplay.ShowBubble("打不开文件管理器\n" + path);
            }
        }

        /// <summary>清空陪伴统计(不动存档与背包), 成就解锁记录也保留</summary>
        private void ResetStats()
        {
            Stats.Clear();
            Stats.Save();
            MainDisplay.Stats = Stats;
            Console.WriteLine("[VPet] 陪伴统计已清零");
            MainDisplay.ShowBubble("陪伴统计已清零\n(等级/金钱/背包/成就都还在)");
            RefreshSettingsPanel();
        }

        /// <summary>
        /// 面板位置: 宠物上方居中, 夹在屏幕内(和商店一致)
        /// </summary>
        private void LayoutOverlay(Border panel)
        {
            var w = panel.Width;
            var h = panel.Bounds.Height > 20 ? panel.Bounds.Height : panel.MaxHeight;
            var left = _petLeft + MainDisplay.Width / 2 - w / 2;
            var top = _petTop - h - 10;
            if (top < 0)
                top = Math.Min(_petTop + 10, Math.Max(0, _viewHeight - h));
            left = Math.Max(0, Math.Min(_viewWidth - w, left));
            Canvas.SetLeft(panel, left);
            Canvas.SetTop(panel, top);
        }

        /// <summary>
        /// 打开一个叠加面板, 同时关掉其它面板(避免叠在一起互相挡)
        /// </summary>
        private void ShowOverlay(Border panel, Action refresh)
        {
            foreach (var other in new[] { _settingsPanel, _charPanel, _shopPanel, _invPanel, _achPanel, _workPanel })
            {
                if (other != null && !ReferenceEquals(other, panel))
                    other.IsVisible = false;
            }

            refresh();
            panel.IsVisible = true;
            LayoutOverlay(panel);
            UpdateClickThrough();
        }

        #region 设置面板

        /// <summary>
        /// 打开设置面板
        /// </summary>
        private void ShowSettingsPanel()
        {
            if (_settingsPanel == null)
            {
                _settingsPanel = CreateOverlayPanel("设置", 380, 520, out _settingsBody);
                PetCanvas.Children.Add(_settingsPanel);
            }
            ShowOverlay(_settingsPanel, RefreshSettingsPanel);
        }

        /// <summary>
        /// 按当前 Settings 重建面板内容（每次改动都重建, 省得同步一堆控件引用）
        /// </summary>
        private void RefreshSettingsPanel()
        {
            if (_settingsBody == null)
                return;
            _settingsBody.Children.Clear();

            void Persist()
            {
                Settings.Save();
            }

            // ---- 数值计算 ----
            _settingsBody.Children.Add(MakeSection("数据计算"));
            _settingsBody.Children.Add(MakeRow("数据计算", MakeToggle(
                () => Settings.EnableFunction,
                v =>
                {
                    Settings.EnableFunction = v;
                    ApplySettings();
                    Persist();
                }), "关掉之后体力/饱腹/心情都不再变化, 适合挂机看桌宠"));

            _settingsBody.Children.Add(MakeRow("计算间隔", MakeStepper(
                () => Settings.LogicIntervalSec,
                v =>
                {
                    Settings.LogicIntervalSec = v;
                    ApplySettings();
                    Persist();
                }, 5, GameSettings.MinInterval, GameSettings.MaxInterval, v => v + " 秒"),
                "每隔多久跑一次数值结算, 默认 15 秒"));

            _settingsBody.Children.Add(MakeRow("互动周期", MakeStepper(
                () => Settings.InteractionCycle,
                v =>
                {
                    Settings.InteractionCycle = v;
                    ApplySettings();
                    Persist();
                }, 5, GameSettings.MinCycle, GameSettings.MaxCycle, v => v + " 周期"),
                "越小宠物越爱自己动"));

            _settingsBody.Children.Add(MakeRow("自动保存", MakeStepper(
                () => Settings.AutoSaveMinutes,
                v =>
                {
                    Settings.AutoSaveMinutes = v;
                    ApplySettings();
                    Persist();
                }, 5, 0, GameSettings.MaxAutoSave, v => v <= 0 ? "关闭" : v + " 分钟")));

            // ---- 说话 ----
            _settingsBody.Children.Add(MakeSection("说话"));
            _settingsBody.Children.Add(MakeRow("随机说话", MakeToggle(
                () => Settings.SayEnabled,
                v =>
                {
                    Settings.SayEnabled = v;
                    Persist();
                })));

            _settingsBody.Children.Add(MakeRow("说话频率", MakeStepper(
                () => Settings.SayPeriod,
                v =>
                {
                    Settings.SayPeriod = v;
                    Persist();
                }, 1, GameSettings.MinSayPeriod, GameSettings.MaxSayPeriod, v => $"{v} 周期"),
                "每隔几个计算周期试一次; 数字越小话越多"));

            _settingsBody.Children.Add(MakeRow("气泡停留", MakeStepper(
                () => Settings.BubbleSeconds,
                v =>
                {
                    Settings.BubbleSeconds = v;
                    ApplySettings();
                    Persist();
                }, 1, GameSettings.MinBubble, GameSettings.MaxBubble, v => v + " 秒"),
                "说话/提示气泡多久自动消失"));

            // ---- 行为 ----
            _settingsBody.Children.Add(MakeSection("行为"));
            _settingsBody.Children.Add(MakeRow("自己走动", MakeToggle(
                () => Settings.AllowRandomMove,
                v =>
                {
                    Settings.AllowRandomMove = v;
                    ApplySettings();
                    Persist();
                }, onText: "会到处爬", offText: "原地发呆"),
                "关掉后宠物不会自己离开原位(拖它还是会走)"));

            // ---- 数据 ----
            _settingsBody.Children.Add(MakeSection("数据"));
            _settingsBody.Children.Add(MakeRow("备份存档", MakeActionButton("立即备份", () =>
            {
                var dir = SaveBackup.Create(out var n);
                MainDisplay.ShowBubble(dir == null
                    ? "备份失败, 详见终端日志"
                    : $"已备份 {n} 个文件\n{dir}");
                if (dir != null)
                    RefreshSettingsPanel();
            }), $"把存档/背包/统计复制到 backup/ 下, 只保留最近 {SaveBackup.KeepCount} 份"
                + $"(现有 {SaveBackup.Count()} 份)"));

            _settingsBody.Children.Add(MakeRow("数据目录", MakeActionButton("打开", OpenDataDir),
                "在文件管理器里打开存档所在目录"));

            _settingsBody.Children.Add(MakeRow("陪伴统计", MakeActionButton("清除", ResetStats, confirm: true),
                "只清 stats.txt(陪伴时长/次数/纪录), 不影响等级金钱和背包"));

            // ---- 系统 ----
            _settingsBody.Children.Add(MakeSection("系统"));
            _settingsBody.Children.Add(MakeRow("开机自启", MakeToggle(
                () => Settings.AutoStart,
                v =>
                {
                    Settings.AutoStart = v;
                    Settings.ApplyAutoStart();
                    Persist();
                }, onText: "已加入自启动", offText: "未加入")));

            _settingsBody.Children.Add(MakeNote("关于: VPet Linux (Avalonia) " + AppInfo.Version +
                                              $" · 食物 {MainDisplay.Core?.Foods.Count ?? 0} 种" +
                                              $" · 背包 {MainDisplay.Core?.Inventory?.TotalCount ?? 0} 件"));
            _settingsBody.Children.Add(MakeNote("自定义台词: " + SpeechPool.UserSpeechPath));
            _settingsBody.Children.Add(MakeNote("设置文件: " + GameSettings.SettingsPath));
        }

        /// <summary>
        /// 把设置应用到运行时
        /// </summary>
        internal void ApplySettings()
        {
            EnableFunction = Settings.EnableFunction;
            InteractionCycle = Settings.InteractionCycle;

            // 气泡停留时长: 直接改 MessageBar 的自动隐藏延迟
            if (MainDisplay.MessageBar != null)
                MainDisplay.MessageBar.AutoHideDelay = Math.Max(1, Settings.BubbleSeconds) * 1000;

            // 计算间隔: 原版 SetLogicInterval, 直接改主循环 Timer 周期
            var ms = Math.Max(1, Settings.LogicIntervalSec) * 1000.0;
            if (Math.Abs(MainDisplay.EventTimer.Interval - ms) > 1)
                MainDisplay.EventTimer.Interval = ms;

            MainDisplay.Settings = Settings;
            MainDisplay.Stats = Stats;

            StartAutoSave();
        }

        #endregion

    }
}
