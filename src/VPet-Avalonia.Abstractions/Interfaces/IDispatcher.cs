namespace VPetAvalonia.Abstractions.Interfaces
{
    /// <summary>
    /// 调度器抽象接口
    /// </summary>
    public interface IDispatcher
    {
        /// <summary>
        /// 在UI线程上执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        void Invoke(Action action);
        
        /// <summary>
        /// 在UI线程上执行操作（无返回值）
        /// </summary>
        /// <param name="action">要执行的操作</param>
        void BeginInvoke(Action action);
        
        /// <summary>
        /// 在UI线程上执行操作（有返回值）
        /// </summary>
        /// <typeparam name="T">返回值类型</typeparam>
        /// <param name="func">要执行的函数</param>
        /// <returns>返回值</returns>
        T Invoke<T>(Func<T> func);
        
        /// <summary>
        /// 检查是否在UI线程上
        /// </summary>
        /// <returns>是否在UI线程上</returns>
        bool CheckAccess();
    }
}
