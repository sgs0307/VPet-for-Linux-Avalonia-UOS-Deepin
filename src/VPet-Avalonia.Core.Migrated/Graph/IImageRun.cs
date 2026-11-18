using System;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 图像运行接口 (Avalonia版本)
    /// </summary>
    public interface IImageRun : IGraph
    {
        /// <summary>
        /// 指定图像图像控件准备运行该动画
        /// </summary>
        /// <param name="img">用于显示的图像控件</param>
        /// <param name="EndAction">结束动画</param>
        /// <returns>准备好的任务</returns>
        Task Run(object img, Action? EndAction = null);
    }
}
