using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using System;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 完成工作信息
    /// </summary>
    public struct FinishWorkInfo
    {
        /// <summary>
        /// 当前完成工作
        /// </summary>
        public Work work;
        /// <summary>
        /// 当前完成工作收入
        /// </summary>
        public double count;
        /// <summary>
        /// 当前完成工作花费时间 (分钟)
        /// </summary>
        public double spendtime;
        /// <summary>
        /// 停止原因
        /// </summary>
        public StopReason Reason;
        /// <summary>
        /// 停止工作的原因
        /// </summary>
        public enum StopReason
        {
            /// <summary>
            /// 时间结束完成
            /// </summary>
            TimeFinish,
            /// <summary>
            /// 玩家手动停止
            /// </summary>
            MenualStop,
            /// <summary>
            /// 因为状态等停止
            /// </summary>
            StateFail,
            /// <summary>
            /// 其他原因
            /// </summary>
            Other,
        }
    }
}