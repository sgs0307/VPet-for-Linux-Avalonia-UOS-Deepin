using System;
using VPetAvalonia.Abstractions.Graphics;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 任务控制实现
    /// </summary>
    public class TaskControl : ITaskControl
    {
        /// <summary>
        /// 当前动画播放状态
        /// </summary>
        public bool PlayState { get; private set; } = true;

        /// <summary>
        /// 控制类型
        /// </summary>
        public ControlType Type { get; set; } = ControlType.Status_Quo;

        /// <summary>
        /// 结束动作
        /// </summary>
        public Action? EndAction { get; set; }

        /// <summary>
        /// 创建任务控制
        /// </summary>
        /// <param name="endAction">结束动作</param>
        public TaskControl(Action? endAction = null)
        {
            EndAction = endAction;
        }

        /// <summary>
        /// 设置为继续播放
        /// </summary>
        public void SetContinue()
        {
            Type = ControlType.Continue;
            PlayState = true;
        }

        /// <summary>
        /// 通知: 播放已自然结束
        /// </summary>
        public void Finish()
        {
            Type = ControlType.Status_Stoped;
            PlayState = false;
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        /// <param name="endAction">结束动作</param>
        public void Stop(Action? endAction = null)
        {
            Type = ControlType.Stop;
            PlayState = false;
            if (endAction != null)
                EndAction = endAction;
        }
    }
}