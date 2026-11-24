using System;
using System.Collections.Generic;
using System.Linq;
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
        /// 面板统一外观: 标题(可带副标题) + 固定头部 + 可滚动内容 + 固定底部。
        /// </summary>
        private Border CreateOverlayPanel(string title, double width, double height, out StackPanel body)
        {
            StackPanel header, footer;
            TextBlock? subtitle;
            return CreatePanel(title, width, height, out body, out header, out footer, out subtitle);
        }

        private Border CreateOverlayPanel(string title, double width, double height,
                                          out StackPanel body, out StackPanel header,
                                          double headerHeight = 0)
        {
            StackPanel footer;
            TextBlock? subtitle;
            return CreatePanel(title, width, height, out body, out header, out footer, out subtitle,
                               headerHeight, 0);
        }

        private Border CreateOverlayPanel(string title, double width, double height,
                                          out StackPanel body, out StackPanel header, out StackPanel footer,
                                          double headerHeight = 0, double footerHeight = 0)
        {
            TextBlock? subtitle;
            return CreatePanel(title, width, height, out body, out header, out footer, out subtitle,
                               headerHeight, footerHeight);
        }

        /// <summary>
        /// 真正的实现。
        ///
        /// v1.5.0 统一外观的三处改动:
        ///   · 关闭改成右上角圆形 ✕, 不再在底部挂一个占满整行的大「关闭」按钮
        ///     (角色面板早就是这个样子, 其它面板一直没跟上, 并排打开像两个软件)
        ///   · 标题下面可以带一行灰色副标题(subtitle), 放"余额/共 N 项/第几页"这类
        ///     会变的状态 —— 比在列表顶上塞一行金色小字更稳, 也不会随列表滚走
        ///   · 滚动区高度按"减掉 ✕ 行与固定区"重算(原先按底部按钮还在时算的, 会白白
        ///     空出一截, 现在把那一截还给内容)
        ///
        /// showTitle=false / showClose=false 供"自己画头部"的面板用(角色面板):
        /// 它把标题、状态徽章、页签和右上角 ✕ 都放进固定头部, 不需要外壳再画一遍。
        /// </summary>
        private Border CreatePanel(string title, double width, double height,
                                   out StackPanel body, out StackPanel header, out StackPanel footer,
                                   out TextBlock? subtitle,
                                   double headerHeight = 0, double footerHeight = 0,
                                   bool showTitle = true, bool showClose = true)
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
            subtitle = null;

            if (showTitle)
            {
                var headGrid = new Grid();
                headGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                headGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var titleBox = new StackPanel();
                titleBox.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = TextMain,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                });
                subtitle = new TextBlock
                {
                    Text = "",
                    Foreground = TextDim,
                    FontSize = 11,
                    Margin = new Thickness(0, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                titleBox.Children.Add(subtitle);
                headGrid.Children.Add(titleBox);

                if (showClose)
                {
                    var close = UiHeaderClose(() =>
                    {
                        root.IsVisible = false;
                        UpdateClickThrough();
                    });
                    Grid.SetColumn(close, 1);
                    headGrid.Children.Add(close);
                }

                stack.Children.Add(headGrid);
            }

            header = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
            stack.Children.Add(header);

            body = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            stack.Children.Add(new ScrollViewer
            {
                // 减掉标题行 / 固定头部 / 固定底部, 否则固定区会把内容顶出面板
                MaxHeight = Math.Max(140, height - 76 - headerHeight - footerHeight),
                Content = body,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            });

            footer = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(footer);

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
        /// 一行"标签 | 控件"(放在 UiFieldGroup 卡片里, 本身不带底板)
        /// </summary>
        private static Control MakeRow(string label, Control right, string? hint = null)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = TextMain,
                FontSize = 13,
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
                    Foreground = UiFaint,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            return panel;
        }

        /// <summary>
        /// 开关: 胶囊形状, 点一下切换, 开=绿 关=灰（不用 CheckBox, 无主题环境下更稳）
        /// </summary>
        private static Control MakeToggle(Func<bool> get, Action<bool> set, string onText = "已开启", string offText = "已关闭")
        {
            var text = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text,
            };

            var on = get();
            void Paint(bool isOn)
            {
                on = isOn;
                text.Text = isOn ? onText : offText;
                text.Foreground = isOn ? Brushes.White : TextDim;
                border.Background = Brush(isOn ? BtnGreen : "#55333333");
            }

            Paint(on);
            border.PointerEntered += (s, e) => border.Background = Brush(on ? BtnGreenHover : "#77444444");
            border.PointerExited += (s, e) => border.Background = Brush(on ? BtnGreen : "#55333333");
            border.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                set(!on);
                Paint(!on);
            };
            return border;
        }

        /// <summary>
        /// 步进器: [-] 数值 [+]，圆角胶囊按钮（不用 Button, 免得退化成系统灰块）
        /// </summary>
        private static Control MakeStepper(Func<int> get, Action<int> set, int step, int min, int max,
                                           Func<int, string> format)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

            var value = new TextBlock
            {
                Foreground = TextAccent,
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Width = 72,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Border StepBtn(string caption, Action act)
            {
                var t = new TextBlock
                {
                    Text = caption,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var b = new Border
                {
                    Width = 26,
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = Brush("#AA444444"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = t,
                };
                b.PointerEntered += (s, e) => b.Background = Brush("#CC555555");
                b.PointerExited += (s, e) => b.Background = Brush("#AA444444");
                b.PointerPressed += (s, e) =>
                {
                    e.Handled = true;
                    act();
                };
                return b;
            }

            void Refresh() => value.Text = format(get());

            panel.Children.Add(StepBtn("−", () =>
            {
                set(Math.Max(min, get() - step));
                Refresh();
            }));
            panel.Children.Add(value);
            panel.Children.Add(StepBtn("+", () =>
            {
                set(Math.Min(max, get() + step));
                Refresh();
            }));
            Refresh();
            return panel;
        }

        /// <summary>
        /// 只读属性条(成就进度等)。转给统一组件, 保持调用方签名不变。
        /// </summary>
        private static Control MakeMeter(string label, double value, double max, string valueText,
                                         IBrush? accent = null)
            => UiMeter(label, value, max, valueText, accent);

        private static Control MakeSection(string title) => UiSection(title);

        private static Control MakeNote(string text, IBrush? brush = null)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = brush ?? UiFaint,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 6, 2, 0),
            };
        }

        /// <summary>
        /// 普通动作按钮(执行一个动作)。confirm=true 时第一次点击只是"上膛", 4 秒内再点一次才真执行
        /// —— 桌宠面板没有系统对话框, 危险操作(清统计)不能用一问一答, 用二次点击最省事。
        /// 形状与列表里的购买/使用按钮一致(圆角胶囊), 不再用系统 Button。
        /// </summary>
        private Control MakeActionPill(string text, Action act, bool confirm = false)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 4),
                Background = Brush(BtnCyan),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = label,
            };

            var armed = false;
            DispatcherTimer? timer = null;

            void Paint(string bg)
            {
                pill.Background = Brush(bg);
            }

            void Disarm()
            {
                armed = false;
                timer?.Stop();
                timer = null;
                label.Text = text;
                Paint(BtnCyan);
            }

            pill.PointerEntered += (s, e) => Paint(armed ? BtnRedHover : BtnCyanHover);
            pill.PointerExited += (s, e) => Paint(armed ? BtnRed : BtnCyan);
            pill.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                if (confirm && !armed)
                {
                    armed = true;
                    label.Text = "再点一次确认";
                    Paint(BtnRed);
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
            return pill;
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
                _settingsPanel = CreateOverlayPanel("设置", 440, 640, out _settingsBody);
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
            var gCalc = UiFieldGroup(_settingsBody, "数据计算");
            gCalc.Children.Add(MakeRow("启用计算", MakeToggle(
                () => Settings.EnableFunction,
                v =>
                {
                    Settings.EnableFunction = v;
                    ApplySettings();
                    Persist();
                }), "关掉之后体力/饱腹/心情都不再变化, 适合挂机看桌宠"));

            gCalc.Children.Add(MakeRow("计算间隔", MakeStepper(
                () => Settings.LogicIntervalSec,
                v =>
                {
                    Settings.LogicIntervalSec = v;
                    ApplySettings();
                    Persist();
                }, 5, GameSettings.MinInterval, GameSettings.MaxInterval, v => v + " 秒"),
                "每隔多久跑一次数值结算, 默认 15 秒"));

            gCalc.Children.Add(MakeRow("互动周期", MakeStepper(
                () => Settings.InteractionCycle,
                v =>
                {
                    Settings.InteractionCycle = v;
                    ApplySettings();
                    Persist();
                }, 5, GameSettings.MinCycle, GameSettings.MaxCycle, v => v + " 周期"),
                "越小宠物越爱自己动"));

            gCalc.Children.Add(MakeRow("自动保存", MakeStepper(
                () => Settings.AutoSaveMinutes,
                v =>
                {
                    Settings.AutoSaveMinutes = v;
                    ApplySettings();
                    Persist();
                }, 5, 0, GameSettings.MaxAutoSave, v => v <= 0 ? "关闭" : v + " 分钟")));

            // ---- 说话 ----
            var gSay = UiFieldGroup(_settingsBody, "说话");
            gSay.Children.Add(MakeRow("随机说话", MakeToggle(
                () => Settings.SayEnabled,
                v =>
                {
                    Settings.SayEnabled = v;
                    Persist();
                })));

            gSay.Children.Add(MakeRow("说话频率", MakeStepper(
                () => Settings.SayPeriod,
                v =>
                {
                    Settings.SayPeriod = v;
                    Persist();
                }, 1, GameSettings.MinSayPeriod, GameSettings.MaxSayPeriod, v => $"{v} 周期"),
                "每隔几个计算周期试一次; 数字越小话越多"));

            gSay.Children.Add(MakeRow("气泡停留", MakeStepper(
                () => Settings.BubbleSeconds,
                v =>
                {
                    Settings.BubbleSeconds = v;
                    ApplySettings();
                    Persist();
                }, 1, GameSettings.MinBubble, GameSettings.MaxBubble, v => v + " 秒"),
                "说话/提示气泡多久自动消失"));

            // ---- 行为 ----
            var gAct = UiFieldGroup(_settingsBody, "行为");
            gAct.Children.Add(MakeRow("自己走动", MakeToggle(
                () => Settings.AllowRandomMove,
                v =>
                {
                    Settings.AllowRandomMove = v;
                    ApplySettings();
                    Persist();
                }, onText: "会到处爬", offText: "原地发呆"),
                "关掉后宠物不会自己离开原位(拖它还是会走)"));

            // ---- 数据 ----
            var gData = UiFieldGroup(_settingsBody, "数据");
            gData.Children.Add(MakeRow("备份存档", MakeActionPill("立即备份", () =>
            {
                var dir = SaveBackup.Create(out var n);
                MainDisplay.ShowBubble(dir == null
                    ? "备份失败, 详见终端日志"
                    : $"已备份 {n} 个文件\n{dir}");
                if (dir != null)
                    RefreshSettingsPanel();
            }), $"把存档/背包/统计复制到 backup/ 下, 只保留最近 {SaveBackup.KeepCount} 份"
                + $"(现有 {SaveBackup.Count()} 份)"));

            gData.Children.Add(MakeRow("数据目录", MakeActionPill("打开", OpenDataDir),
                "在文件管理器里打开存档所在目录"));

            gData.Children.Add(MakeRow("陪伴统计", MakeActionPill("清除", ResetStats, confirm: true),
                "只清 stats.txt(陪伴时长/次数/纪录), 不影响等级金钱和背包"));

            // ---- 系统 ----
            var gSys = UiFieldGroup(_settingsBody, "系统");
            gSys.Children.Add(MakeRow("开机自启", MakeToggle(
                () => Settings.AutoStart,
                v =>
                {
                    Settings.AutoStart = v;
                    Settings.ApplyAutoStart();
                    Persist();
                }, onText: "已加入自启动", offText: "未加入")));

            // ---- 关于 ----
            var gAbout = UiFieldGroup(_settingsBody, "关于");
            gAbout.Children.Add(MakeNote("VPet Linux (Avalonia) " + AppInfo.Version +
                                         $" · 食物 {MainDisplay.Core?.Foods.Count ?? 0} 种" +
                                         $" · 背包 {MainDisplay.Core?.Inventory?.TotalCount ?? 0} 件"));
            gAbout.Children.Add(MakeNote("自定义台词: " + SpeechPool.UserSpeechPath));
            gAbout.Children.Add(MakeNote("设置文件: " + GameSettings.SettingsPath));
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
