namespace VPetAvalonia.Abstractions.Interfaces
{
    /// <summary>
    /// 动作接口
    /// </summary>
    public interface IAction
    {
        /// <summary>
        /// 执行动作
        /// </summary>
        /// <returns>执行结果</returns>
        bool Execute();
        
        /// <summary>
        /// 取消动作
        /// </summary>
        void Cancel();
    }
}
