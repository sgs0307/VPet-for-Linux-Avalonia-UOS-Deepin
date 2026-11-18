namespace VPetAvalonia.Abstractions.Interfaces
{
    using VPetAvalonia.Abstractions.Graphics;

    /// <summary>
    /// 游戏核心抽象接口
    /// </summary>
    public interface IGameCore
    {
        /// <summary>
        /// 控制器
        /// </summary>
        IController? Controller { get; set; }
        
        /// <summary>
        /// 触摸范围和事件列表
        /// </summary>
        IList<ITouchArea> TouchEvent { get; }
        
        /// <summary>
        /// 图形核心
        /// </summary>
        IGraphCore? Graph { get; set; }
        
        /// <summary>
        /// 游戏数据
        /// </summary>
        IGameSave? Save { get; set; }
    }
}
