using System;

namespace VPetAvalonia.Abstractions.Interfaces
{
    /// <summary>
    /// 显示宿主接口 - 供 Work 等配置类调用显示方法
    /// </summary>
    public interface IDisplayHost
    {
        /// <summary>
        /// 显示动画
        /// </summary>
        /// <param name="name">动画名字</param>
        /// <param name="animat">动画动作</param>
        /// <param name="endAction">结束动作</param>
        void Display(string name, GraphInfo.AnimatType animat, Action? endAction = null);

        /// <summary>
        /// 显示B循环 (强制)
        /// </summary>
        /// <param name="graphname">动画名字</param>
        void DisplayBLoopingForce(string graphname);
    }
}