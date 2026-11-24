using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 面板统一设计语言（v1.5.0）。
    ///
    /// v1.4.0 只把「角色面板」按新样式重做了，其它面板还是老的 FluentTheme 观感：
    ///   · 分类/排序/购买全是系统 Button —— 直角灰块、hover 是系统色，和角色面板的
    ///     圆角胶囊并排放在一起像两个软件
    ///   · 右上角没有关闭入口，每个面板底部都挂一个大「关闭」按钮，占掉一整行高度
    ///   · 列表是 1px 分隔线平铺，没有卡片层次；数值用天蓝小字，和角色面板的金色大数字不一致
    ///
    /// 这里把角色面板里的那套东西抽出来复用：
    ///   颜色令牌 / 胶囊页签 / 胶囊动作按钮 / 徽章 / 卡片 / 搜索框 / 分组标题 / 只读属性条
    /// 全部用 Border + Pointer 事件手绘，不依赖 Button 的控件模板 —— 无主题环境也不会退化成灰方块。
    /// </summary>
    public partial class MainWindow
    {
        // ================= 颜色令牌(和角色面板一致) =================

        internal static readonly IBrush UiCardBg = Brush("#14FFFFFF");
        internal static readonly IBrush UiCardHover = Brush("#2EFFFFFF");
        internal static readonly IBrush UiTrackBg = Brush("#1CFFFFFF");
        internal static readonly IBrush UiDanger = Brush("#EF5350");
        internal static readonly IBrush UiGold = Brush("#FFD54F");
        internal static readonly IBrush UiGreen = Brush("#66BB6A");
        internal static readonly IBrush UiCyan = Brush("#81D4FA");
        internal static readonly IBrush UiPurple = Brush("#BA68C8");
        internal static readonly IBrush UiOrange = Brush("#FFB74D");
        internal static readonly IBrush UiMuted = Brush("#8A8A8A");
        internal static readonly IBrush UiFaint = Brush("#6E6E6E");

        // 动作按钮底色 / hover 底色
        internal const string BtnGreen = "#AA2E7D32";
        internal const string BtnGreenHover = "#CC388E3C";
        internal const string BtnGreenDeep = "#AA1B5E20";
        internal const string BtnGreenDeepHover = "#CC246B27";
        internal const string BtnGray = "#AA444444";
        internal const string BtnGrayHover = "#CC555555";
        internal const string BtnRed = "#AA8B3A3A";
        internal const string BtnRedHover = "#CC9E4444";
        internal const string BtnCyan = "#AA0286C6";
        internal const string BtnCyanHover = "#CC0E9BDD";

        /// <summary>胶囊页签/筛选: 选中态填色 + 白字加粗, 未选中透明 + 灰字, hover 轻微提亮</summary>
        private static Border UiPill(string label, bool active, Action onClick, bool small = false)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = small ? 11 : 12,
                Foreground = active ? Brushes.White : TextDim,
                FontWeight = active ? FontWeight.Bold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var pill = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = small ? new Thickness(8, 2) : new Thickness(11, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Background = active ? Brush("#330286C6") : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text,
            };
            pill.PointerEntered += (s, e) =>
            {
                if (!active)
                    pill.Background = UiCardHover;
            };
            pill.PointerExited += (s, e) =>
            {
                if (!active)
                    pill.Background = Brushes.Transparent;
            };
            pill.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                onClick();
            };
            return pill;
        }

        /// <summary>胶囊动作按钮(买/用/弃/开始/停止): 圆角实色, hover 提亮, 禁用态灰掉且不响应</summary>
        private static Border UiAction(string label, string bg, string bgHover, Action onClick,
                                       bool enabled = true, bool noMargin = false)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = enabled ? Brushes.White : Brush("#888888"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btn = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 3),
                Margin = new Thickness(noMargin ? 0 : 3, 0, 0, 0),
                Background = Brush(enabled ? bg : "#44333333"),
                Cursor = new Cursor(enabled ? StandardCursorType.Hand : StandardCursorType.Arrow),
                Child = text,
            };
            if (enabled)
            {
                btn.PointerEntered += (s, e) => btn.Background = Brush(bgHover);
                btn.PointerExited += (s, e) => btn.Background = Brush(bg);
                btn.PointerPressed += (s, e) =>
                {
                    e.Handled = true;
                    onClick();
                };
            }
            return btn;
        }

        /// <summary>小徽章(进行中 / 已有 N / 需要 Lv.5)</summary>
        private static Border UiBadge(string label, string bgHex, IBrush? fg = null, double fontSize = 10)
        {
            return new Border
            {
                Background = Brush(bgHex),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = fontSize,
                    Foreground = fg ?? Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        }

        /// <summary>卡片: 列表行/信息块的统一底板</summary>
        private static Border UiCard(Control child, double padX = 10, double padY = 7,
                                     double bottom = 6, IBrush? bg = null)
        {
            return new Border
            {
                Background = bg ?? UiCardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(padX, padY),
                Margin = new Thickness(0, 0, 0, bottom),
                Child = child,
            };
        }

        /// <summary>给卡片加悬停高亮(整行可点的时候用)</summary>
        private static void UiHover(Border card, IBrush? normal = null)
        {
            var rest = normal ?? card.Background;
            card.PointerEntered += (s, e) => card.Background = UiCardHover;
            card.PointerExited += (s, e) => card.Background = rest;
        }

        /// <summary>搜索框: 外面套一层圆角浅底, TextBox 自身去边去底色</summary>
        private static Control UiSearch(TextBox box, string watermark)
        {
            box.Watermark = watermark;
            box.FontSize = 13;
            box.MinHeight = 30;
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.Padding = new Thickness(8, 4);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            return new Border
            {
                Background = Brush("#1AFFFFFF"),
                BorderBrush = Brush("#22FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 2, 0, 8),
                ClipToBounds = true,
                Child = box,
            };
        }

        /// <summary>分组标题(橙色小标题 + 右侧灰色补充说明)</summary>
        private static Control UiSection(string title, string? right = null)
        {
            var g = new Grid { Margin = new Thickness(2, 10, 2, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            g.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = UiOrange,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (right != null)
            {
                var r = new TextBlock
                {
                    Text = right,
                    Foreground = UiFaint,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(r, 1);
                g.Children.Add(r);
            }
            return g;
        }

        /// <summary>
        /// 只读属性条(成就进度、工作进度用): 标签 + 圆角轨道 + 彩色填充 + 数值。
        /// 和角色面板的 CharMeter 同一套画法, 只是不参与定时刷新。
        /// </summary>
        private static Control UiMeter(string label, double value, double max, string valueText,
                                       IBrush? accent = null, IBrush? labelBrush = null,
                                       double labelWidth = 46)
        {
            var pct = max > 0 ? Math.Clamp(value / max * 100, 0, 100) : 0;
            var color = accent ?? UiCyan;

            var fillCol = new ColumnDefinition(Math.Max(0.01, pct), GridUnitType.Star);
            var restCol = new ColumnDefinition(Math.Max(0.01, 100 - pct), GridUnitType.Star);
            var fill = new Border { Background = color, CornerRadius = new CornerRadius(4) };

            var seg = new Grid();
            seg.ColumnDefinitions.Add(fillCol);
            seg.ColumnDefinitions.Add(restCol);
            seg.Children.Add(fill);

            var bar = new Grid { Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
            bar.Children.Add(new Border { Background = UiTrackBg, CornerRadius = new CornerRadius(4) });
            bar.Children.Add(seg);

            var row = new Grid { Height = 20 };
            row.ColumnDefinitions.Add(new ColumnDefinition(labelWidth, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = labelBrush ?? TextDim,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var value2 = new TextBlock
            {
                Text = valueText,
                Foreground = UiCyan,
                FontSize = 11,
                Width = 74,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(value2, 2);
            row.Children.Add(value2);
            return row;
        }

        /// <summary>右上角圆形 ✕ (各面板头部统一用这个, 不再用底部大「关闭」按钮)</summary>
        private Border UiHeaderClose(Action close)
        {
            var glyph = new TextBlock
            {
                Text = "✕",
                FontSize = 14,
                Foreground = TextDim,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var box = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = glyph,
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.PointerEntered += (s, e) =>
            {
                box.Background = Brush("#33FFFFFF");
                glyph.Foreground = Brushes.White;
            };
            box.PointerExited += (s, e) =>
            {
                box.Background = Brushes.Transparent;
                glyph.Foreground = TextDim;
            };
            box.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                close();
            };
            return box;
        }

        /// <summary>
        /// 面板里的一组带底板的字段(设置面板用): 只创建并挂载外层卡片, 返回内部容器。
        /// 这样设置面板每个「分组」都是独立一张卡, 和角色面板的 4 张数值卡同一层语言。
        /// </summary>
        private static StackPanel UiFieldGroup(StackPanel parent, string title, string? right = null)
        {
            var inner = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
            var card = new Border
            {
                Background = UiCardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 4, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel
                {
                    Children =
                    {
                        UiSection(title, right),
                        inner,
                    },
                },
            };
            parent.Children.Add(card);
            return inner;
        }
    }
}
