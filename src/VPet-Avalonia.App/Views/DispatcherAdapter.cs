using System;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// Avalonia Dispatcher 适配器 - 将 Avalonia.Threading.Dispatcher 适配为 IDispatcher 接口
    /// </summary>
    public class DispatcherAdapter : IDispatcher
    {
        private readonly Avalonia.Threading.Dispatcher _dispatcher;

        public DispatcherAdapter(Avalonia.Threading.Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void Invoke(Action action)
        {
            // UI 线程上直接执行: Avalonia 的 Invoke 语义不如 WPF 可靠,
            // 从 UI 线程调用 Invoke 存在死锁风险
            if (_dispatcher.CheckAccess())
            {
                action();
                return;
            }
            _dispatcher.Invoke(action);
        }

        public T Invoke<T>(Func<T> func)
        {
            if (_dispatcher.CheckAccess())
                return func();
            return _dispatcher.Invoke(func);
        }

        public void BeginInvoke(Action action)
        {
            if (_dispatcher.CheckAccess())
                action();
            else
                _dispatcher.Post(action);
        }

        public bool CheckAccess()
        {
            return _dispatcher.CheckAccess();
        }
    }
}
