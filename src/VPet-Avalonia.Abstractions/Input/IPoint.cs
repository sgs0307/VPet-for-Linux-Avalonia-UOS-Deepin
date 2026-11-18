namespace VPetAvalonia.Abstractions.Input
{
    /// <summary>
    /// 点坐标抽象接口
    /// </summary>
    public interface IPoint
    {
        /// <summary>
        /// X坐标
        /// </summary>
        double X { get; }
        
        /// <summary>
        /// Y坐标
        /// </summary>
        double Y { get; }
        
        /// <summary>
        /// 计算到另一个点的距离
        /// </summary>
        /// <param name="other">另一个点</param>
        /// <returns>距离</returns>
        double DistanceTo(IPoint other);
        
        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        /// <returns>点坐标字符串</returns>
        string ToString();
    }
}
