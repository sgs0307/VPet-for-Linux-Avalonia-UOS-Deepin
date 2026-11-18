using System;
using Avalonia;
using Avalonia.Controls;
using VPetAvalonia.Core;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// MainWindow 的控制器部分: 实现桌宠控制器接口
    /// 桌宠窗口铺满全屏, 所以"移动桌宠"实际上是移动宠物在画布中的位置
    /// </summary>
    public partial class MainWindow : IController
    {
        /// <summary>
        /// 宠物逻辑尺寸(与 MOD 资源分辨率一致)
        /// </summary>
        public const double PetDesignSize = 500.0;

        /// <summary>
        /// 缩放比例
        /// </summary>
        public double ZoomRatio { get; set; } = 1.0;

        /// <summary>
        /// 启用数据计算
        /// </summary>
        public bool EnableFunction { get; set; } = true;

        /// <summary>
        /// 互动周期
        /// </summary>
        public int InteractionCycle { get; set; } = 30;

        /// <summary>
        /// 长按判定时间(毫秒)
        /// </summary>
        public int PressLength => 1000;

        /// <summary>
        /// 是否启用边缘重新定位
        /// </summary>
        public bool RePositionActive { get; set; } = true;

        /// <summary>
        /// 是否自动切换活动屏幕
        /// </summary>
        public bool AutoChangeWindow => false;

        /// <summary>
        /// 移动桌宠位置 (参数已含缩放倍率)
        /// </summary>
        public void MoveWindows(double X, double Y)
        {
            SetPetPosition(Canvas.GetLeft(MainDisplay) + X, Canvas.GetTop(MainDisplay) + Y);
        }

        /// <summary>
        /// 拖动中: 直接把宠物摆到"指针位置 - 按下时的抓取偏移"。
        ///
        /// 为什么不用逐帧累加 MoveWindows(delta):
        /// 累加式在宠物顶到屏幕边缘时, clamp 会把多出来的位移"吃掉"。指针再往回拖时,
        /// 必须先把被吃掉的行程补回来, 宠物才开始跟手 —— 手感就是"拖了一下才动"。
        /// 绝对定位每帧都按指针真实位置算, 指针一动宠物就到, 越界时稳定贴在边上。
        /// </summary>
        internal void DragTo(Avalonia.Point cursor, Avalonia.Vector grab)
        {
            SetPetPosition(cursor.X - grab.X, cursor.Y - grab.Y);
        }

        /// <summary>
        /// 宠物左上角在窗口内的逻辑坐标(可能为负, 跨屏瞬间允许部分出界)
        /// </summary>
        internal Avalonia.Vector PetTopLeft()
        {
            var l = Canvas.GetLeft(MainDisplay);
            var t = Canvas.GetTop(MainDisplay);
            return new Avalonia.Vector(double.IsNaN(l) ? 0 : l, double.IsNaN(t) ? 0 : t);
        }

        /// <summary>
        /// 桌宠距左边界距离
        /// </summary>
        public double GetWindowsDistanceLeft() => Canvas.GetLeft(MainDisplay);

        /// <summary>
        /// 桌宠距右边界距离。
        ///
        /// 用 _viewWidth 而不是 Bounds.Width: Bounds 是窗口客户区的实测值, 换屏后
        /// 要等 WM 回 ConfigureNotify 才更新。那几帧里它还是旧屏宽度, 算出来的
        /// "距离"会凭空多出/少掉两屏的宽度差(1.0x 屏 2160 vs 1.25x 屏 2048),
        /// 于是宠物明明在主屏贴右边缘, 却被判成"越界 112" 又去换屏 —— 来回闪的根源。
        /// _viewWidth 是 ApplyScreen 主动设定的目标值, 换屏那一刻就是对的。
        /// </summary>
        public double GetWindowsDistanceRight() => _viewWidth - Canvas.GetLeft(MainDisplay) - MainDisplay.Width;

        /// <summary>
        /// 桌宠距上边界距离
        /// </summary>
        public double GetWindowsDistanceUp() => Canvas.GetTop(MainDisplay);

        /// <summary>
        /// 桌宠距下边界距离 (同样用 _viewHeight, 理由见 GetWindowsDistanceRight)
        /// </summary>
        public double GetWindowsDistanceDown() => _viewHeight - Canvas.GetTop(MainDisplay) - MainDisplay.Height;

        /// <summary>
        /// 桌宠所在屏幕是否为活动屏幕
        /// </summary>
        public bool IfInActivateScreen() => true;

        /// <summary>
        /// 将当前屏幕设置为活动屏幕
        /// </summary>
        public void SetNowScreenActivate() { }

        /// <summary>
        /// 显示数据面板
        /// </summary>
        public void ShowPanel()
        {
            // 原版工具条的面板区(BdrPanel)默认是隐藏的, 只切 ToolBar 会显示一个空壳,
            // 这就是"数据面板打不开"的直接原因。TogglePanel 连数值区一起切换并刷新数据
            ToolBar.IsVisible = !ToolBar.IsVisible;
            ToolBar.TogglePanel();
            Console.WriteLine($"[VPet] 数据面板: ToolBar={ToolBar.IsVisible}");
            LayoutPetUI();
            UpdateClickThrough();
        }

        /// <summary>
        /// 在边缘时重新靠边, 防止宠物被拖出屏幕或被任务栏挡住
        /// </summary>
        public void ResetPosition()
        {
            SetPetPosition(Canvas.GetLeft(MainDisplay), Canvas.GetTop(MainDisplay));
        }

        /// <summary>
        /// 判断桌宠是否靠边(贴底)
        /// </summary>
        public bool CheckPosition() => GetWindowsDistanceDown() < 5;
    }
}
