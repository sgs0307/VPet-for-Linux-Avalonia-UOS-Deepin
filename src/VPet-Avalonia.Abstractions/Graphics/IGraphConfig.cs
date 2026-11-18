using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions.Input;
using VPetAvalonia.Abstractions.Interfaces;
using System.Collections.Generic;

namespace VPetAvalonia.Abstractions.Graphics
{
    /// <summary>
    /// 图形配置接口
    /// </summary>
    public interface IGraphConfig
    {
        /// <summary>
        /// 移动配置列表
        /// </summary>
        IReadOnlyList<Move> MovesList { get; }

        /// <summary>
        /// 工作配置列表
        /// </summary>
        IReadOnlyList<object> WorksList { get; }

        /// <summary>
        /// 获取动画持续时间
        /// </summary>
        /// <param name="graphName">动画名字</param>
        /// <returns>持续时间</returns>
        int GetDuration(string graphName);

        /// <summary>
        /// 获取触摸头部位置
        /// </summary>
        /// <param name="mode">状态</param>
        /// <returns>位置</returns>
        IPoint GetTouchHeadLocate(ModeType mode);

        /// <summary>
        /// 获取触摸抬起位置
        /// </summary>
        /// <param name="mode">状态</param>
        /// <returns>位置</returns>
        IPoint GetTouchRaisedLocate(ModeType mode);
    }
}