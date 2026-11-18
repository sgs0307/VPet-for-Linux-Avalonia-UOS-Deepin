namespace VPetAvalonia.Abstractions.Interfaces
{
    /// <summary>
    /// UI元素抽象接口
    /// </summary>
    public interface IUIElement
    {
        /// <summary>
        /// 获取或设置元素的宽度
        /// </summary>
        double Width { get; set; }
        
        /// <summary>
        /// 获取或设置元素的高度
        /// </summary>
        double Height { get; set; }
        
        /// <summary>
        /// 获取或设置元素的X坐标
        /// </summary>
        double X { get; set; }
        
        /// <summary>
        /// 获取或设置元素的Y坐标
        /// </summary>
        double Y { get; set; }
        
        /// <summary>
        /// 显示元素
        /// </summary>
        void Show();
        
        /// <summary>
        /// 隐藏元素
        /// </summary>
        void Hide();
        
        /// <summary>
        /// 更新元素布局
        /// </summary>
        void UpdateLayout();

        /// <summary>
        /// 设置元素内容
        /// </summary>
        /// <param name="content">内容（可以是路径、位图等）</param>
        void SetContent(object content);
    }
}
