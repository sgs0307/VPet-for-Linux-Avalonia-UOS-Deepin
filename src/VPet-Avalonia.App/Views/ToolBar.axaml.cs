using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;

namespace VPetAvalonia.App.Views
{
    public partial class ToolBar : UserControl
    {
        internal Main? _mainDisplay;

        /// <summary>
        /// 面板打开期间定期刷新数值
        /// </summary>
        private DispatcherTimer? _refreshTimer;

        public ToolBar()
        {
            InitializeComponent();

            // Initialize UI components
            InitializeToolBar();
        }

        /// <summary>
        /// 设置主显示引用
        /// </summary>
        public void SetMainDisplay(Main main)
        {
            _mainDisplay = main;
        }

        /// <summary>
        /// 打开/关闭数据面板
        /// 原版工具条还要先点"面板"按钮才显示数值区(BdrPanel 默认隐藏),
        /// 但右键菜单的"数据面板"语义就是直接看数据, 所以这里连 BdrPanel 一起切换
        /// </summary>
        public void TogglePanel()
        {
            var show = !BdrPanel.IsVisible;
            BdrPanel.IsVisible = show;
            if (show)
            {
                RefreshValues();
                _refreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                _refreshTimer.Tick -= OnRefreshTick;
                _refreshTimer.Tick += OnRefreshTick;
                _refreshTimer.Start();
            }
            else
            {
                _refreshTimer?.Stop();
            }
        }

        private void OnRefreshTick(object? sender, EventArgs e)
        {
            try
            {
                RefreshValues();
            }
            catch
            {
                // 刷新失败不影响面板
            }
        }

        /// <summary>
        /// 用当前存档数据刷新面板显示
        /// </summary>
        public void RefreshValues()
        {
            var save = _mainDisplay?.Core?.Save;
            if (save == null)
                return;

            Tlv.Text = $"Lv {save.Level}";
            tMoney.Text = $"${save.Money:F0}";

            pExp.Value = ClampPct(save.Exp, save.LevelUpNeed());
            tExp.Text = $"{save.Exp:F0}/{save.LevelUpNeed():F0}";

            pStrength.Value = ClampPct(save.Strength, save.StrengthMax);
            tStrength.Text = $"{save.Strength:F0}";

            pFeeling.Value = ClampPct(save.Feeling, save.FeelingMax);
            tFeeling.Text = $"{save.Feeling:F0}";

            pStrengthFoodMax.Value = ClampPct(save.StrengthMax, save.StrengthMax);
            pStrengthFood.Value = ClampPct(save.StrengthFood, save.StrengthMax);
            tStrengthFood.Text = $"{save.StrengthFood:F0}";

            pStrengthDrinkMax.Value = ClampPct(save.StrengthMax, save.StrengthMax);
            pStrengthDrink.Value = ClampPct(save.StrengthDrink, save.StrengthMax);
            tStrengthDrink.Text = $"{save.StrengthDrink:F0}";

            // 状态提示
            if (save.Mode == VPetAvalonia.Abstractions.Interfaces.ModeType.Ill)
            {
                till.Text = "宠物已经生病, 通过服用药物可以恢复";
                tfun.Text = "";
            }
            else
            {
                till.Text = "";
                tfun.Text = _mainDisplay?.Core?.Controller?.EnableFunction == true
                    ? "数据计算已开启"
                    : "已关闭数据计算, 可放心挂机";
            }
        }

        private static double ClampPct(double value, double max)
        {
            if (max <= 0) return 0;
            var pct = value / max * 100;
            return Math.Max(0, Math.Min(100, pct));
        }

        // UI update methods
        public void UpdateLevel(int level)
        {
            Tlv.Text = $"Lv {level}";
        }

        public void UpdateMoney(string money)
        {
            tMoney.Text = money;
        }

        public void UpdateExp(double value, string multiplier)
        {
            pExp.Value = value;
            tExp.Text = $"x{multiplier}";
        }

        public void UpdateStrength(double value, string rate)
        {
            pStrength.Value = value;
            tStrength.Text = $"{rate}";
        }

        public void UpdateFeeling(double value, string rate)
        {
            pFeeling.Value = value;
            tFeeling.Text = $"{rate}";
        }

        public void UpdateStrengthFood(double current, double max, string rate)
        {
            pStrengthFood.Value = current;
            pStrengthFoodMax.Value = max;
            tStrengthFood.Text = $"{rate}";
        }

        public void UpdateStrengthDrink(double current, double max, string rate)
        {
            pStrengthDrink.Value = current;
            pStrengthDrinkMax.Value = max;
            tStrengthDrink.Text = $"{rate}";
        }

        public void UpdateStatusMessage(string message, bool isError = false)
        {
            if (isError)
            {
                till.Text = message;
                till.Foreground = Brushes.Red;
                tfun.Text = "";
            }
            else
            {
                tfun.Text = message;
                tfun.Foreground = Brushes.Green;
                till.Text = "";
            }
        }

        private void InitializeToolBar()
        {
            // Set up event handlers
            MenuFeed.Click += (s, e) => { };
            MenuPanel.Click += MenuPanel_Click;
            MenuStudy.Click += (s, e) => { };
            MenuPlay.Click += (s, e) => { };
            MenuWork.Click += (s, e) => { };
        }

        private void MenuPanel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Toggle panel visibility
            BdrPanel.IsVisible = !BdrPanel.IsVisible;
        }
    }
}