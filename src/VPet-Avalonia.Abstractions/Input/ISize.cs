namespace VPetAvalonia.Abstractions.Input
{
    /// <summary>
    /// 大小抽象接口
    /// </summary>
    public interface ISize
    {
        /// <summary>
        /// 宽度
        /// </summary>
        double Width { get; }
        
        /// <summary>
        /// 高度
        /// </summary>
        double Height { get; }
        
        /// <summary>
        /// 计算面积
        /// </summary>
        /// <returns>面积</returns>
        double Area();
        
        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        /// <returns>大小字符串</returns>
        string ToString();
    }
}
