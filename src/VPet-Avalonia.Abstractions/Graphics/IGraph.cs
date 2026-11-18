namespace VPetAvalonia.Abstractions.Graphics
{
    using VPetAvalonia.Abstractions.Interfaces;

    /// <summary>
    /// 动画显示抽象接口
    /// </summary>
    public interface IGraph : IDisposable
    {
        /// <summary>
        /// 从0开始运行该动画
        /// </summary>
        /// <param name="parent">显示位置</param>
        /// <param name="endAction">停止动作</param>
        void Run(IUIElement parent, Action? endAction = null);
        
        /// <summary>
        /// 是否循环播放
        /// </summary>
        bool IsLoop { get; set; }
        
        /// <summary>
        /// 是否准备完成
        /// </summary>
        bool IsReady { get; }
        
        /// <summary>
        /// 是否读取失败
        /// </summary>
        bool IsFail { get; }
        
        /// <summary>
        /// 失败报错信息
        /// </summary>
        string FailMessage { get; }
        
        /// <summary>
        /// 该动画信息
        /// </summary>
        GraphInfo GraphInfo { get; }
        
        /// <summary>
        /// 当前动画播放状态和控制
        /// </summary>
        ITaskControl? Control { get; }
        
        /// <summary>
        /// 停止动画
        /// </summary>
        /// <param name="stopEndAction">停止动画时是否不运行结束动画</param>
        void Stop(bool stopEndAction);
        
        /// <summary>
        /// 设置为继续播放
        /// </summary>
        void SetContinue();
        
        /// <summary>
        /// 动画文件路径, 可能是文件夹或文件
        /// </summary>
        string? Path { get; }
        
        /// <summary>
        /// 上次使用时间戳, 用于判断是否需要释放资源
        /// </summary>
        long LastUseTimeTicks { get; }
        
        /// <summary>
        /// 修改最后使用时间为当前时间，以便在清理空闲缓存时判断是否需要清理
        /// </summary>
        void Touch();
        
        /// <summary>
        /// 清理空闲缓存, 如果该动画长时间未使用, 则释放资源
        /// </summary>
        /// <param name="nowTicks">当前时间</param>
        void CleanupIdleCache(long nowTicks);
    }
}
