using System;
using System.Runtime.InteropServices;

namespace VPetAvalonia.App.Native
{
    /// <summary>
    /// Linux 桌面下的窗口原生能力
    ///
    /// 背景: 桌宠要求"只有宠物本体能点, 其余透明区域鼠标穿透",
    /// Avalonia 没有跨平台 API 能做到这点(IsHitTestVisible 只影响控件命中, 不影响窗口),
    /// 在 X11 下必须用 XShape 扩展改写窗口的输入区域(input shape)。
    /// Wayland 协议不允许客户端自己设置输入区域, 只能降级为不穿透。
    /// </summary>
    public static class LinuxWindowNative
    {
        #region 会话类型探测

        /// <summary>
        /// 当前图形会话类型
        /// </summary>
        public enum SessionType
        {
            Unknown,
            X11,
            Wayland,
        }

        /// <summary>
        /// 探测实际生效的窗口后端类型
        ///
        /// 注意: 不能按 XDG_SESSION_TYPE 判断。Avalonia 的 Linux 默认后端是 X11,
        /// 只要 DISPLAY 存在(包括 Wayland 桌面下的 XWayland), 实际初始化的就是
        /// Avalonia.X11.AvaloniaX11Platform, 且 XShape 扩展在 XWayland 上同样可用。
        /// 若按"Wayland 会话"降级成宠物大小的小窗口, 会出现:
        ///   1. 拖动失效(MoveWindows 移动的是窗口内画布, 小窗口下被夹在 (0,0));
        ///   2. 数据面板只能叠在宠物身上显示;
        ///   3. 鼠标穿透直接不可用。
        /// 因此判定依据是 DISPLAY 是否存在, 而不是桌面会话类型。
        /// </summary>
        public static SessionType DetectSession()
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

            if (!string.IsNullOrEmpty(display))
                return SessionType.X11;
            if (!string.IsNullOrEmpty(waylandDisplay))
                return SessionType.Wayland;
            return SessionType.Unknown;
        }

        #endregion

        #region X11 / XShape

        private const string LibX11 = "libX11.so.6";
        private const string LibXext = "libXext.so.6";

        // XShape 常量
        private const int ShapeInput = 2;
        private const int ShapeSet = 0;
        private const int ShapeUnion = 1;
        private const int Unsorted = 0;
        private const int YXBanded = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct XRectangle
        {
            public short x;
            public short y;
            public ushort width;
            public ushort height;
        }

        [DllImport(LibX11, EntryPoint = "XOpenDisplay")]
        private static extern IntPtr XOpenDisplay(string? displayName);

        [DllImport(LibX11, EntryPoint = "XCloseDisplay")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport(LibXext, EntryPoint = "XShapeQueryExtension")]
        private static extern int XShapeQueryExtension(IntPtr display, out int eventBase, out int errorBase);

        [DllImport(LibXext, EntryPoint = "XShapeCombineRectangles")]
        private static extern void XShapeCombineRectangles(
            IntPtr display, IntPtr window, int destKind,
            int xOff, int yOff, XRectangle[] rectangles, int rectangleCount, int op, int ordering);

        [DllImport(LibX11, EntryPoint = "XFlush")]
        private static extern int XFlush(IntPtr display);

        [DllImport(LibX11, EntryPoint = "XDefaultRootWindow")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport(LibX11, EntryPoint = "XQueryPointer")]
        private static extern int XQueryPointer(
            IntPtr display, IntPtr window,
            out IntPtr rootReturn, out IntPtr childReturn,
            out int rootX, out int rootY, out int winX, out int winY, out uint maskReturn);

        [DllImport(LibX11, EntryPoint = "XMoveResizeWindow")]
        private static extern int XMoveResizeWindow(
            IntPtr display, IntPtr window, int x, int y, uint width, uint height);

        /// <summary>
        /// 一次请求同时改变窗口位置和尺寸（XMoveResizeWindow）
        ///
        /// 为什么要原子: 拖动开始时要把整屏窗口缩成宠物大小，同时把窗口挪到宠物位置。
        /// 用 Avalonia 的 Width/Height + Position 是**两个**独立请求，而且 Resize 还要等
        /// ConfigureNotify 回来，中间必然渲染一帧「窗口已缩成 508×508、原点却还在 (0,0)」——
        /// 宠物画布在窗口内 (pad,pad)，于是在屏幕上闪到左上角。用户实测能看到"拖动时往左上角闪一下"。
        /// </summary>
        public static bool MoveResizeWindow(IntPtr windowHandle, int x, int y, int width, int height)
        {
            if (windowHandle == IntPtr.Zero)
                return false;
            EnsureDisplay();
            if (_display == IntPtr.Zero)
                return false;
            try
            {
                XMoveResizeWindow(_display, windowHandle, x, y,
                    (uint)Math.Max(1, width), (uint)Math.Max(1, height));
                XFlush(_display);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 查询指针在根窗口上的位置（物理像素）
        ///
        /// 拖动改成"窗口跟着指针走"之后，指针相对窗口的坐标不再变化，
        /// Avalonia 事件里的位置没有位移信息，只能直接向 X 服务器要全局坐标。
        /// </summary>
        public static bool TryGetPointerPosition(out int x, out int y)
        {
            x = 0;
            y = 0;
            EnsureDisplay();
            if (_display == IntPtr.Zero)
                return false;
            try
            {
                var root = XDefaultRootWindow(_display);
                if (root == IntPtr.Zero)
                    return false;
                if (XQueryPointer(_display, root, out _, out _, out x, out y, out _, out _, out _) == 0)
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IntPtr _display = IntPtr.Zero;
        private static bool _displayChecked;
        private static bool _shapeAvailable;

        /// <summary>
        /// X11 输入区域穿透是否可用
        /// </summary>
        public static bool IsClickThroughAvailable
        {
            get
            {
                EnsureDisplay();
                return _display != IntPtr.Zero && _shapeAvailable;
            }
        }

        private static void EnsureDisplay()
        {
            if (_displayChecked)
                return;
            _displayChecked = true;
            try
            {
                if (DetectSession() != SessionType.X11)
                    return;

                var dpy = XOpenDisplay(null);
                if (dpy == IntPtr.Zero)
                    return;
                _display = dpy;

                int ev, er;
                _shapeAvailable = XShapeQueryExtension(dpy, out ev, out er) != 0;
            }
            catch (DllNotFoundException)
            {
                // 系统没有 libXext / libX11, 直接降级
                _shapeAvailable = false;
            }
            catch
            {
                _shapeAvailable = false;
            }
        }

        /// <summary>
        /// 累计向 X 服务器提交输入区域变更的次数。
        /// 拖动时每帧一次这个请求是"宠物不跟手"的元凶, 自检靠这个计数验证拖动期间为 0
        /// </summary>
        public static long InputRegionCalls { get; private set; }

        /// <summary>
        /// 设置窗口的输入区域
        /// </summary>
        /// <param name="windowHandle">X11 窗口句柄</param>
        /// <param name="regions">可接收输入的矩形区域(窗口坐标), 传空表示整窗穿透</param>
        /// <returns>是否设置成功</returns>
        public static bool SetInputRegions(IntPtr windowHandle, (int X, int Y, int Width, int Height)[]? regions)
        {
            if (windowHandle == IntPtr.Zero)
                return false;

            EnsureDisplay();
            if (_display == IntPtr.Zero || !_shapeAvailable)
                return false;

            InputRegionCalls++;

            try
            {
                XRectangle[] rects;
                if (regions == null || regions.Length == 0)
                {
                    // 空区域 = 整窗穿透
                    rects = new XRectangle[1];
                }
                else
                {
                    rects = new XRectangle[regions.Length];
                    for (int i = 0; i < regions.Length; i++)
                    {
                        var r = regions[i];
                        rects[i] = new XRectangle
                        {
                            x = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, r.X)),
                            y = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, r.Y)),
                            width = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, r.Width)),
                            height = (ushort)Math.Max(0, Math.Min(ushort.MaxValue, r.Height)),
                        };
                    }
                }

                int count = (regions == null || regions.Length == 0) ? 0 : rects.Length;
                // ordering 必须与矩形的排序方式一致, 单矩形场景用 Unsorted 最安全
                XShapeCombineRectangles(_display, windowHandle, ShapeInput, 0, 0, rects, count, ShapeSet, Unsorted);
                XFlush(_display);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 恢复窗口的完整输入区域(取消穿透)
        /// </summary>
        public static bool ResetInputRegion(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
                return false;
            EnsureDisplay();
            if (_display == IntPtr.Zero || !_shapeAvailable)
                return false;
            InputRegionCalls++;
            try
            {
                // 传入一个覆盖足够大的矩形即可恢复
                var rects = new[]
                {
                    new XRectangle { x = 0, y = 0, width = (ushort)short.MaxValue, height = (ushort)short.MaxValue }
                };
                XShapeCombineRectangles(_display, windowHandle, ShapeInput, 0, 0, rects, 1, ShapeSet, Unsorted);
                XFlush(_display);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
