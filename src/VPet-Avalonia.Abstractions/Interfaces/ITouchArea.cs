namespace VPetAvalonia.Abstractions.Interfaces
{
    using VPetAvalonia.Abstractions.Input;

    /// <summary>
    /// 触摸区域抽象接口
    /// </summary>
    public interface ITouchArea
    {
        /// <summary>
        /// 触摸区域的位置
        /// </summary>
        IPoint Locate { get; }
        
        /// <summary>
        /// 触摸区域的大小
        /// </summary>
        ISize Size { get; }
        
        /// <summary>
        /// 触摸触发的动作
        /// </summary>
        System.Func<bool> DoAction { get; }
        
        /// <summary>
        /// 是否为长按触发
        /// </summary>
        bool IsPress { get; }
        
        /// <summary>
        /// 判断指定点是否在触摸区域内
        /// </summary>
        /// <param name="point">要判断的点</param>
        /// <returns>是否在触摸区域内</returns>
        bool Touch(IPoint point);
        
        /// <summary>
        /// 创建触摸区域
        /// </summary>
        /// <param name="locate">位置</param>
        /// <param name="size">大小</param>
        /// <param name="doAction">触发动作</param>
        /// <param name="isPress">是否为长按触发</param>
        /// <returns>触摸区域对象</returns>
        ITouchArea Create(IPoint locate, ISize size, System.Func<bool> doAction, bool isPress = false);
    }
}
