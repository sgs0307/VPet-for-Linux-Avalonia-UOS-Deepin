using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 宠物图像显示元素
    /// 把 IUIElement 抽象接口落到真实的 Avalonia Image 控件上
    ///
    /// 关键点:
    /// 1. 动画层只会传图片路径(string)进来, 这里负责解码成 Bitmap — 原实现只接受 Control, 导致所有图片被静默丢弃
    /// 2. 动画播放跑在后台线程, 所有对控件的访问都必须切回 UI 线程
    /// 3. 位图缓存, 避免每帧重复读盘解码
    /// </summary>
    public class PetImageElement : IUIElement
    {
        private readonly Decorator _host;
        private readonly Image _image;

        private double _width = 500;
        private double _height = 500;
        private double _x;
        private double _y;

        /// <summary>
        /// 位图缓存: 路径 -> 位图（LRU，按最近使用淘汰）
        /// </summary>
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, LinkedListNode<CacheEntry>> BitmapCache =
            new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<CacheEntry> CacheOrder = new LinkedList<CacheEntry>();

        /// <summary>
        /// 缓存上限（张）。500x500 贴图每张约 1MB，96 张 ≈ 96MB 上限；
        /// 原实现上限 1200 张且满了整体清空 —— 峰值能到 1GB+，清空那一下还会让动画卡住。
        /// 一套动画通常只有几帧到二十几帧，96 张足够覆盖当前动画 + 前后切换的几套。
        /// </summary>
        private const int CacheLimit = 96;

        private readonly struct CacheEntry
        {
            public readonly string Path;
            public readonly Bitmap Bitmap;

            public CacheEntry(string path, Bitmap bitmap)
            {
                Path = path;
                Bitmap = bitmap;
            }
        }

        /// <summary>
        /// 取缓存（命中即提到队首）
        /// </summary>
        private static bool TryGetCache(string path, out Bitmap? bitmap)
        {
            lock (CacheLock)
            {
                if (BitmapCache.TryGetValue(path, out var node))
                {
                    if (node != CacheOrder.First)
                    {
                        CacheOrder.Remove(node);
                        CacheOrder.AddFirst(node);
                    }
                    bitmap = node.Value.Bitmap;
                    return true;
                }
                bitmap = null;
                return false;
            }
        }

        /// <summary>
        /// 写缓存，超出上限时淘汰最久未用的并释放其位图。
        /// 返回真正该显示的那张：同一路径并发解码时以先入的为准，
        /// 多解出来的这张必须就地释放，否则每次并发都漏一张约 1MB 的原生位图
        /// （而且会被 post 到 Image.Source 上，再也没人回收）。
        /// </summary>
        private static Bitmap PutCache(string path, Bitmap bitmap)
        {
            lock (CacheLock)
            {
                if (BitmapCache.TryGetValue(path, out var existing))
                {
                    try { bitmap.Dispose(); } catch { }
                    return existing.Value.Bitmap;
                }
                var node = CacheOrder.AddFirst(new CacheEntry(path, bitmap));
                BitmapCache[path] = node;
                while (CacheOrder.Count > CacheLimit)
                {
                    var last = CacheOrder.Last;
                    if (last == null)
                        break;
                    CacheOrder.RemoveLast();
                    BitmapCache.Remove(last.Value.Path);
                    try { last.Value.Bitmap.Dispose(); } catch { }
                }
                return bitmap;
            }
        }

        public PetImageElement(Decorator host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _image = new Image
            {
                Width = 500,
                Height = 500,
                Stretch = Avalonia.Media.Stretch.Uniform,
                // 桌宠贴图多为带透明通道的 PNG, 不做平滑会更贴近原版像素观感
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            };

            if (Dispatcher.UIThread.CheckAccess())
                _host.Child = _image;
            else
                Dispatcher.UIThread.Post(() => _host.Child = _image);
        }

        /// <summary>
        /// 内部 Image 控件, 供外部做命中测试/布局
        /// </summary>
        public Image ImageControl => _image;

        public double Width
        {
            get => _width;
            set
            {
                // 动画每帧都会重新设置宽高, 值没变就不要再往 UI 线程丢任务
                if (Math.Abs(_width - value) < 0.01)
                    return;
                _width = value;
                if (value > 0)
                    Post(() => _image.Width = value);
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) < 0.01)
                    return;
                _height = value;
                if (value > 0)
                    Post(() => _image.Height = value);
            }
        }

        public double X
        {
            get => _x;
            set
            {
                _x = value;
                Post(() => _image.Margin = new Thickness(value, _image.Margin.Top, _image.Margin.Right, _image.Margin.Bottom));
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                _y = value;
                Post(() => _image.Margin = new Thickness(_image.Margin.Left, value, _image.Margin.Right, _image.Margin.Bottom));
            }
        }

        /// <summary>
        /// 设置显示内容, 支持: 图片路径 / Bitmap / 任意对象的 ToString 作为路径
        /// </summary>
        public void SetContent(object content)
        {
            string? path = null;
            switch (content)
            {
                case null:
                    return;
                case string s:
                    path = s;
                    break;
                case Bitmap bmp:
                    Post(() => _image.Source = bmp);
                    return;
                default:
                    path = content.ToString();
                    break;
            }

            if (string.IsNullOrWhiteSpace(path))
                return;

            if (TryGetCache(path!, out var cached) && cached != null)
            {
                Post(() => _image.Source = cached);
                return;
            }

            // 后台解码, 不阻塞动画线程, 也不阻塞 UI
            _ = Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path))
                        return;
                    // 同一路径并发解码时可能多解一次, PutCache 会保留先入的那张并释放多余的
                    var bmp = new Bitmap(path);
                    var shown = PutCache(path!, bmp);
                    Post(() => _image.Source = shown);
                }
                catch
                {
                    // 单帧解码失败不应中断整个动画
                }
            });
        }

        public void Show()
        {
            if (_visible)
                return;
            _visible = true;
            Post(() =>
            {
                _image.IsVisible = true;
                _host.IsVisible = true;
            });
        }

        public void Hide()
        {
            if (!_visible)
                return;
            _visible = false;
            Post(() => _image.IsVisible = false);
        }

        private bool _visible = true;

        public void UpdateLayout() => Post(() => _image.InvalidateVisual());

        /// <summary>
        /// 清空当前显示内容
        /// </summary>
        public void Clear() => Post(() => _image.Source = null);

        private static void Post(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                try { action(); } catch { }
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try { action(); } catch { }
                });
            }
        }

        /// <summary>
        /// 清空全局位图缓存
        /// </summary>
        public static void ClearCache()
        {
            lock (CacheLock)
            {
                foreach (var node in CacheOrder)
                {
                    try { node.Bitmap.Dispose(); } catch { }
                }
                CacheOrder.Clear();
                BitmapCache.Clear();
            }
        }

        /// <summary>
        /// 当前缓存的位图数量
        /// </summary>
        public static int CacheCount
        {
            get { lock (CacheLock) return BitmapCache.Count; }
        }
    }
}
