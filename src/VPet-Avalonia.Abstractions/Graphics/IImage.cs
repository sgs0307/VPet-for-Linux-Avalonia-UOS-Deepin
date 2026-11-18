namespace VPetAvalonia.Abstractions.Graphics
{
    /// <summary>
    /// 图像抽象接口
    /// </summary>
    public interface IImage
    {
        /// <summary>
        /// 图像的宽度
        /// </summary>
        int Width { get; }
        
        /// <summary>
        /// 图像的高度
        /// </summary>
        int Height { get; }
        
        /// <summary>
        /// 图像的像素数据
        /// </summary>
        byte[] Pixels { get; }
        
        /// <summary>
        /// 从文件加载图像
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>图像对象</returns>
        IImage LoadFromFile(string filePath);
        
        /// <summary>
        /// 保存图像到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        void SaveToFile(string filePath);
        
        /// <summary>
        /// 缩放图像
        /// </summary>
        /// <param name="width">目标宽度</param>
        /// <param name="height">目标高度</param>
        /// <returns>缩放后的图像</returns>
        IImage Resize(int width, int height);
        
        /// <summary>
        /// 旋转图像
        /// </summary>
        /// <param name="angle">旋转角度</param>
        /// <returns>旋转后的图像</returns>
        IImage Rotate(double angle);
        
        /// <summary>
        /// 裁剪图像
        /// </summary>
        /// <param name="x">裁剪区域X坐标</param>
        /// <param name="y">裁剪区域Y坐标</param>
        /// <param name="width">裁剪区域宽度</param>
        /// <param name="height">裁剪区域高度</param>
        /// <returns>裁剪后的图像</returns>
        IImage Crop(int x, int y, int width, int height);
    }
}
