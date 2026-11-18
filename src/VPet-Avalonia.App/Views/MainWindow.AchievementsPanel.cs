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
    /// 加了"只看未解锁"过滤和总进度条 —— 12 条成就里解锁了几条一眼能看到。
    /// </summary>
    public partial class MainWindow
    {
        private Border? _achPanel;
        private StackPanel? _achBody;
        private bool _achOnlyLocked;

        private void ShowAchievementsPanel()
        {
            if (_achPanel == null)
            {
                _achPanel = CreateOverlayPanel("成就", 420, 540, out _achBody);
                PetCanvas.Children.Add(_achPanel);
            }
            ShowOverlay(_achPanel, RefreshAchievementsPanel);
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

            _achBody.Children.Add(new TextBlock
            {
                Text = $"已解锁 {unlocked} / {total}" + (total > 0 ? $"  ({unlocked * 100 / total}%)" : ""),
                Foreground = TextAccent,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 2),
            });
            _achBody.Children.Add(MakeMeter("总进度", unlocked, total, $"{unlocked}/{total}"));

            _achBody.Children.Add(MakeRow("只看未解锁", MakeToggle(
                () => _achOnlyLocked,
                v =>
                {
                    _achOnlyLocked = v;
                    RefreshAchievementsPanel();
                    UpdateClickThrough();
                }, "是", "否")));

            var shown = 0;
            foreach (var def in Achievements.Defs)
            {
                var isUnlocked = Achievements.Unlocked.Contains(def.Id);
                if (_achOnlyLocked && isUnlocked)
                    continue;
                shown++;

                // 名称 + (已解锁时间 或 进度)
                var head = new Grid { Margin = new Thickness(0, 6, 0, 0) };
                head.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                head.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                head.Children.Add(new TextBlock
                {
                    Text = (isUnlocked ? "🏅 " : "🔒 ") + def.Name,
                    Foreground = isUnlocked ? Brushes.Gold : TextMain,
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                });

                var right = new TextBlock
                {
                    Text = isUnlocked && times.TryGetValue(def.Id, out var t)
                        ? t.ToString("yyyy-MM-dd HH:mm")
                        : Achievements.ProgressText(def, Stats, core?.Save),
                    Foreground = isUnlocked ? Brushes.Gold : TextDim,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(right, 1);
                head.Children.Add(right);
                _achBody.Children.Add(head);

                // 描述 + 进度条
                _achBody.Children.Add(MakeNote(def.Desc, isUnlocked ? Brushes.Gold : null));

                double cur;
                try { cur = def.Progress(Stats, core?.Save); }
                catch { cur = 0; }
                _achBody.Children.Add(MakeMeter(isUnlocked ? "完成" : "进度",
                    Math.Min(cur, def.Goal), def.Goal,
                    Achievements.ProgressText(def, Stats, core?.Save)));
            }

            if (shown == 0)
                _achBody.Children.Add(MakeNote("全部成就都解锁了, 厉害! 关掉「只看未解锁」可以回看达成时间。"));

            _achBody.Children.Add(MakeNote("成就与存档分开保存(achievements.txt), 删档不清零。"));
        }
    }
}
