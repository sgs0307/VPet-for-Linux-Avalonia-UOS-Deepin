namespace VPetAvalonia.Abstractions.Graphics
{
    using VPetAvalonia.Abstractions.Interfaces;
    using VPetAvalonia.Abstractions;
    using System.Collections.Generic;

    /// <summary>
    /// 图形核心抽象接口
    /// </summary>
    public interface IGraphCore : IDisposable
    {
        /// <summary>
        /// 桌宠图形渲染的分辨率,越高图形越清晰
        /// </summary>
        int Resolution { get; set; }
        
        /// <summary>
        /// 动画缓存空闲超时时间,超过该时间未使用的动画将被释放,单位 Ticks
        /// </summary>
        long IdleCacheTimeout { get; set; }
        
        /// <summary>
        /// 添加动画
        /// </summary>
        /// <param name="graph">动画</param>
        void AddGraph(IGraph graph);
        
        /// <summary>
        /// 移除动画
        /// </summary>
        /// <param name="graph">动画</param>
        void RemoveGraph(IGraph graph);
        
        /// <summary>
        /// 获取所有动画
        /// </summary>
        /// <returns>动画列表</returns>
        IReadOnlyList<IGraph> GetAllGraphs();
        
        /// <summary>
        /// 清理空闲缓存
        /// </summary>
        void CleanupIdleCache();
        
        /// <summary>
        /// 获取调度器
        /// </summary>
        IDispatcher Dispatcher { get; }

        /// <summary>
        /// 图形配置
        /// </summary>
        IGraphConfig GraphConfig { get; }

        /// <summary>
        /// 通过名字查找动画
        /// </summary>
        /// <param name="graphName">动画名字</param>
        /// <param name="animat">动画动作类型</param>
        /// <param name="mode">状态类型</param>
        /// <returns>找到的动画，未找到返回null</returns>
        IGraph? FindGraph(string? graphName, GraphInfo.AnimatType animat, ModeType mode);

        /// <summary>
        /// 通过类型查找动画名字
        /// </summary>
        /// <param name="type">动画类型</param>
        /// <returns>随机动画名字，未找到返回null</returns>
        string? FindName(GraphInfo.GraphType type);

        /// <summary>
        /// 通过名字查找所有匹配的动画
        /// </summary>
        /// <param name="graphName">动画名字</param>
        /// <param name="animat">动画动作类型</param>
        /// <param name="mode">状态类型</param>
        /// <returns>匹配的动画列表</returns>
        List<IGraph>? FindGraphs(string? graphName, GraphInfo.AnimatType animat, ModeType mode);

        /// <summary>
        /// 动画名字字典 (类型 -> 名字集合)
        /// </summary>
        Dictionary<GraphInfo.GraphType, HashSet<string>> GraphsName { get; }
    }
}
