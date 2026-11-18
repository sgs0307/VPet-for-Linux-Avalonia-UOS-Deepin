namespace VPetAvalonia.Abstractions.Graphics
{
    /// <summary>
    /// 颜色抽象接口
    /// </summary>
    public interface IColor
    {
        /// <summary>
        /// 获取颜色的红色分量 (0-255)
        /// </summary>
        byte R { get; }
        
        /// <summary>
        /// 获取颜色的绿色分量 (0-255)
        /// </summary>
        byte G { get; }
        
        /// <summary>
        /// 获取颜色的蓝色分量 (0-255)
        /// </summary>
        byte B { get; }
        
        /// <summary>
        /// 获取颜色的透明度分量 (0-255)
        /// </summary>
        byte A { get; }
        
        /// <summary>
        /// 从HEX字符串创建颜色
        /// </summary>
        /// <param name="hex">HEX颜色字符串</param>
        /// <returns>颜色对象</returns>
        IColor FromHex(string hex);
        
        /// <summary>
        /// 转换为HEX字符串
        /// </summary>
        /// <returns>HEX颜色字符串</returns>
        string ToHex();
        
        /// <summary>
        /// 转换为字符串表示
        /// </summary>
        /// <returns>颜色字符串</returns>
        string ToString();
    }
}
