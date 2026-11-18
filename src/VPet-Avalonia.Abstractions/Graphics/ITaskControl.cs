namespace VPetAvalonia.Abstractions.Graphics
{
    /// <summary>
    /// 任务控制抽象接口
    /// </summary>
    public interface ITaskControl
    {
        /// <summary>
        /// 当前动画播放状态
        /// </summary>
        bool PlayState { get; }

        /// <summary>
        /// 通知控制: 本次播放已自然结束 (与 Stop 的区别是不重置回调链)
        /// PlayState 必须在动画退出后置 false, 否则下次重播时
        /// Run 会误以为循环还在运行, 把重播挂在一个已退出的循环上, 动画从此卡死
        /// </summary>
        void Finish();
        
        /// <summary>
        /// 设置为继续播放
        /// </summary>
        void SetContinue();
        
        /// <summary>
        /// 停止播放
        /// </summary>
        /// <param name="endAction">结束动作</param>
        void Stop(Action? endAction = null);
        
        /// <summary>
        /// 控制类型
        /// </summary>
        ControlType Type { get; set; }
        
        /// <summary>
        /// 结束动作
        /// </summary>
        Action? EndAction { get; set; }
    }
    
    /// <summary>
    /// 控制类型枚举
    /// </summary>
    public enum ControlType
    {
        /// <summary>
        /// 维持现状, 不进行任何超控
        /// </summary>
        Status_Quo,
        
        /// <summary>
        /// 停止当前动画
        /// </summary>
        Stop,
        
        /// <summary>
        /// 播放完成后继续播放,仅生效一次, 之后将恢复为Status_Quo
        /// </summary>
        Continue,
        
        /// <summary>
        /// 动画已停止
        /// </summary>
        Status_Stoped,
    }
}
