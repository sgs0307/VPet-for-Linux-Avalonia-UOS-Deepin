using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    public partial class MessageBar : UserControl
    {
        /// <summary>
        /// 气泡自动隐藏计时器
        /// </summary>
        private DispatcherTimer? _hideTimer;

        /// <summary>
        /// 气泡显示时长(毫秒)
        /// </summary>
        public int AutoHideDelay { get; set; } = 4000;

        public MessageBar()
        {
            InitializeComponent();
        }

        public void Show(string name, string text, string? graphName = null, object? msgContent = null)
        {
            // 之前只赋值文本却从不显示控件, 所以对话气泡永远看不到
            LName.Text = $"{name}:";
            TText.Text = text;
            IsVisible = true;
            RestartHideTimer();
        }

        public void ShowMessage(string name, string message)
        {
            LName.Text = $"{name}:";
            TText.Text = message;
            IsVisible = true;
            RestartHideTimer();
        }

        public void ClearMessage()
        {
            TText.Text = "";
            IsVisible = false;
        }

        public void AppendMessage(string message)
        {
            TText.Text += message + Environment.NewLine;
            IsVisible = true;
            RestartHideTimer();
        }

        private void RestartHideTimer()
        {
            _hideTimer?.Stop();
            _hideTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(AutoHideDelay),
                DispatcherPriority.Normal,
                (_, _) =>
                {
                    IsVisible = false;
                    _hideTimer?.Stop();
                });
            _hideTimer.Start();
        }
    }
}
