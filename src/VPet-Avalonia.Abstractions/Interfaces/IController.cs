namespace VPetAvalonia.Abstractions.Interfaces
{
    using VPetAvalonia.Abstractions.Input;

    /// <summary>
    /// 桌宠控制器抽象接口
    /// </summary>
    public interface IController
    {
        /// <summary>
        /// 移动桌宠位置 (自带缩放倍率)
        /// </summary>
        /// <param name="x">X轴</param>
        /// <param name="y">Y轴</param>
        void MoveWindows(double x, double y);
        
        /// <summary>
        /// 获取桌宠距离左侧的位置
        /// </summary>
        double GetWindowsDistanceLeft();
        
        /// <summary>
        /// 获取桌宠距离右侧的位置
        /// </summary>
        double GetWindowsDistanceRight();
        
        /// <summary>
        /// 获取桌宠距离上方的位置
        /// </summary>
        double GetWindowsDistanceUp();
        
        /// <summary>
        /// 获取桌宠距离下方的位置
        /// </summary>
        double GetWindowsDistanceDown();
        
        /// <summary>
        /// 获取桌宠所在屏幕是否为活动屏幕
        /// </summary>
        /// <returns>是否为活动屏幕</returns>
        bool IfInActivateScreen();
        
        /// <summary>
        /// 将当前屏幕设置为活动屏幕（如果桌宠在多个屏幕上则以主要屏幕为准）
        /// </summary>
        void SetNowScreenActivate();
        
        /// <summary>
        /// 获取缩放比例
        /// </summary>
        double ZoomRatio { get; }
        
        /// <summary>
        /// 获取按多久视为长按 单位毫秒
        /// </summary>
        int PressLength { get; }
        
        /// <summary>
        /// 在边缘时重新靠边，防止被阻挡
        /// </summary>
        void ResetPosition();
        
        /// <summary>
        /// 判断桌宠是否靠边
        /// </summary>
        /// <returns>是否靠边</returns>
        bool CheckPosition();
        
        /// <summary>
        /// 启用计算等数据功能
        /// </summary>
        bool EnableFunction { get; }
        
        /// <summary>
        /// 互动周期
        /// </summary>
        int InteractionCycle { get; }
        
        /// <summary>
        /// 是否启用边缘重新定位
        /// </summary>
        bool RePositionActive { get; set; }
        
        /// <summary>
        /// 是否自动切换活动屏幕
        /// </summary>
        bool AutoChangeWindow { get; }
    }
}
