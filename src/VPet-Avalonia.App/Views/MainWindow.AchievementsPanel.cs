using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VPetAvalonia.Core;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 成就面板 (原先挤在 MainWindow.InventoryPanel.cs 里, 顺手挪出来独立成文件)。
    ///
    /// v1.5.0 按角色面板的样式重做: 头部副标题显示"已解锁 x/y", 每条成就是一张卡片
    /// (已解锁用金色底 + 金条, 未解锁用灰字 + 天蓝条), 不再是"标题 + 小灰字 + 系统进度条"
    /// 平铺到底。
    /// </summary>
    public partial class MainWindow
    {
        private Border? _achPanel;
        private StackPanel? _achBody;
        private StackPanel? _achTools;
        private TextBlock? _achHeadText;
        private bool _achOnlyLocked;

        private void ShowAchievementsPanel()
        {
            if (_achPanel == null)
            {
                _achPanel = CreatePanel("成就", 440, 600, out _achBody, out _achTools, out _,
                                        out _achHeadText, 40, 0);
                BuildAchTools();
                PetCanvas.Children.Add(_achPanel);
            }
            ShowOverlay(_achPanel, RefreshAchievementsPanel);
        }

        private void BuildAchTools()
        {
            if (_achTools == null)
                return;

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 4),
                Spacing = 6,
            };
            row.Children.Add(new TextBlock
            {
                Text = "筛选",
                Foreground = UiFaint,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0),
            });
            row.Children.Add(MakeToggle(() => _achOnlyLocked, v =>
            {
                _achOnlyLocked = v;
                RefreshAchievementsPanel();
                UpdateClickThrough();
            }, "只看未解锁", "显示全部"));

            _achTools.Children.Add(row);
        }

        private void RefreshAchievementsPanel()
        {
            if (_achBody == null)
                return;
            _achBody.Children.Clear();

            var core = MainDisplay.Core;
            var times = Achievements.LoadUnlockTimes();
            var total = Achievements.Defs.Length;
            var unlocked = Achievements.Unlocked.Count;
            var pct = total > 0 ? unlocked * 100 / total : 0;

            if (_achHeadText != null)
                _achHeadText.Text = $"已解锁 {unlocked} / {total}" + (total > 0 ? $" · {pct}%" : "");

            // ---- 总进度卡 ----
            var sumText = new TextBlock
            {
                Text = $"{unlocked}",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = UiGold,
            };
            var sumLine = new StackPanel { Orientation = Orientation.Horizontal };
            sumLine.Children.Add(sumText);
            sumLine.Children.Add(new TextBlock
            {
                Text = $" / {total}",
                FontSize = 11,
                Foreground = UiMuted,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 3),
            });
            var sumStack = new StackPanel();
            sumStack.Children.Add(sumLine);
            sumStack.Children.Add(new TextBlock
            {
                Text = "已解锁成就",
                FontSize = 11,
                Foreground = TextDim,
                Margin = new Thickness(0, 1, 0, 0),
            });

            var totalCard = UiCard(new StackPanel
            {
                Children =
                {
                    sumStack,
                    new Border
                    {
                        Margin = new Thickness(0, 6, 0, 0),
                        Child = UiMeter("总进度", unlocked, total, $"{pct}%", UiGold, labelWidth: 34),
                    },
                },
            }, 12, 9, 8);
            _achBody.Children.Add(totalCard);

            // ---- 每条成就一张卡 ----
            var shown = 0;
            foreach (var def in Achievements.Defs)
            {
                var isUnlocked = Achievements.Unlocked.Contains(def.Id);
                if (_achOnlyLocked && isUnlocked)
                    continue;
                shown++;

                var head = new Grid();
                head.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
                nameLine.Children.Add(new TextBlock
                {
                    Text = (isUnlocked ? "🏅 " : "🔒 ") + def.Name,
                    Foreground = isUnlocked ? UiGold : Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (isUnlocked)
                    nameLine.Children.Add(UiBadge("已达成", "#33FFD54F", UiGold, 9));
                head.Children.Add(nameLine);

                var right = new TextBlock
                {
                    Text = isUnlocked && times.TryGetValue(def.Id, out var t)
                        ? t.ToString("yyyy-MM-dd")
                        : Achievements.ProgressText(def, Stats, core?.Save),
                    Foreground = isUnlocked ? UiGold : TextDim,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(right, 1);
                head.Children.Add(right);

                double cur;
                try { cur = def.Progress(Stats, core?.Save); }
                catch { cur = 0; }

                var inner = new StackPanel();
                inner.Children.Add(head);
                inner.Children.Add(new TextBlock
                {
                    Text = def.Desc,
                    Foreground = isUnlocked ? Brush("#CCFFD54F") : UiMuted,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
                inner.Children.Add(new Border
                {
                    Margin = new Thickness(0, 5, 0, 0),
                    Child = UiMeter(isUnlocked ? "完成" : "进度",
                        Math.Min(cur, def.Goal), def.Goal,
                        Achievements.ProgressText(def, Stats, core?.Save),
                        isUnlocked ? UiGold : UiCyan, labelWidth: 34),
                });

                _achBody.Children.Add(UiCard(inner, 10, 8, 5,
                    isUnlocked ? Brush("#1AFFD54F") : UiCardBg));
            }

            if (shown == 0)
                _achBody.Children.Add(MakeNote("全部成就都解锁了, 厉害! 关掉「只看未解锁」可以回看达成时间。"));

            _achBody.Children.Add(MakeNote("成就与存档分开保存(achievements.txt), 删档不清零。"));
        }
    }
}
