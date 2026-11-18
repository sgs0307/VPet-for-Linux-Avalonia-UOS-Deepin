namespace VPetAvalonia.Abstractions.Graphics
{
    /// <summary>
    /// 图形信息抽象接口
    /// </summary>
    public interface IGraphInfo
    {
        /// <summary>
        /// 图形类型
        /// </summary>
        GraphType Type { get; }
        
        /// <summary>
        /// 图形名称
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 图形路径
        /// </summary>
        string Path { get; }
        
        /// <summary>
        /// 图形描述
        /// </summary>
        string Description { get; }
    }
    
    /// <summary>
    /// 图形类型枚举
    /// </summary>
    public enum GraphType
    {
        /// <summary>
        /// 通用图形
        /// </summary>
        Common,
        
        /// <summary>
        /// 桌宠图形
        /// </summary>
        Pet,
        
        /// <summary>
        /// 其他图形
        /// </summary>
        Other
    }
}
