using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions;
using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// APNG 动画 (Avalonia 版本)
    ///
    /// 原实现是"把 APNG 当单帧静态图", 等于没播。现在走完整链路:
    ///   ApngDecoder 解析 acTL/fcTL/fdAT → 按 dispose/blend 合成每帧 → 写成帧图
    ///   → 本类按帧时长逐帧 SetContent(path), 复用显示层的位图缓存
    ///
    /// 生命周期要点(与 PNGAnimation 保持一致, 这两条都是踩过坑的):
    /// 1. 自然结束必须 Control.Finish(), 否则 PlayState 恒为 true, 下次重播会挂在已退出的循环上
    /// 2. 播放循环用 async/await, 不要每帧新建线程 + Thread.Sleep (长循环会堆积线程把机器拖死)
    /// </summary>
    public class APNGAnimation : IImageRun
    {
        /// <summary>
        /// 是否循环播放(lps 里的 loop=true)
        /// </summary>
        public bool IsLoop { get; set; }

        /// <summary>
        /// 动画信息
        /// </summary>
        public GraphInfo? GraphInfo { get; private set; }

        /// <summary>
        /// 是否准备完成
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 是否读取失败
        /// </summary>
        public bool IsFail { get; private set; }

        /// <summary>
        /// 失败报错信息
        /// </summary>
        public string FailMessage { get; private set; } = "";

        /// <summary>
        /// 当前动画播放状态和控制
        /// </summary>
        public ITaskControl? Control { get; set; }

        /// <summary>
        /// 图片资源
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>帧数</summary>
        public int FrameCount => _framePaths.Length;

        /// <summary>解码耗时(毫秒)</summary>
        public long DecodeMs { get; private set; }

        /// <summary>帧图是否直接来自缓存目录(没有重新合成)</summary>
        public bool FromCache { get; private set; }

        /// <summary>APNG acTL 里声明的播放次数, 0 = 无限</summary>
        public int LoopCount { get; private set; }

        private GraphCoreAvalonia? _graphCore;

        private string[] _framePaths = Array.Empty<string>();
        private int[] _frameTimes = Array.Empty<int>();

        private int nowid;

        /// <summary>解码任务: 首次播放会等它完成(解析+合成可能要几百毫秒到几秒)</summary>
        private Task? _loadTask;

        private IUIElement? _lastParent;
        private Action? _lastEndAction;

        public long LastUseTimeTicks { get; private set; }

        public APNGAnimation(GraphCoreAvalonia graphCore, string path, GraphInfo graphinfo, bool isLoop = false)
        {
            _graphCore = graphCore;
            Path = path;
            GraphInfo = graphinfo;
            IsLoop = isLoop;
            Touch();
            _loadTask = Task.Run(startup);
        }

        /// <summary>
        /// 从0开始运行该动画
        /// </summary>
        public void Run(IUIElement parent, Action? endAction = null) => RunCore(parent, endAction);

        /// <summary>
        /// 指定图像控件准备运行该动画
        /// </summary>
        public Task Run(object img, Action? EndAction = null)
        {
            if (img is IUIElement ui)
            {
                RunCore(ui, EndAction);
                return Task.CompletedTask;
            }
            EndAction?.Invoke();
            return Task.CompletedTask;
        }

        private void RunCore(IUIElement parent, Action? endAction)
        {
            Touch();
            if (Control?.PlayState == true)
            {
                // 上一轮还在跑: 让它下一帧检测到 Stop 后用新回调重播
                Control.Stop(() => RunCore(parent, endAction));
                return;
            }

            _lastParent = parent;
            _lastEndAction = endAction;

            var ctrl = new TaskControl(endAction);
            Control = ctrl;
            nowid = 0;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await EnsureReadyAsync().ConfigureAwait(false))
                    {
                        IsFail = true;
                        ctrl.Finish();
                        try { ctrl.EndAction?.Invoke(); }
                        catch (Exception cbErr) { Console.WriteLine($"[VPet] APNG 结束回调异常: {cbErr.Message}"); }
                        return;
                    }

                    while (true)
                    {
                        if (ctrl.Type == ControlType.Stop)
                        {
                            ctrl.Finish();
                            ctrl.EndAction?.Invoke();
                            return;
                        }
                        if (ctrl.Type == ControlType.Status_Stoped)
                            return;

                        if (nowid < 0 || nowid >= _framePaths.Length)
                            nowid = 0;

                        parent.SetContent(_framePaths[nowid]);
                        parent.Width = 500;
                        parent.Height = 500;
                        parent.Show();

                        var time = _frameTimes[nowid];
                        if (time < 16) time = 16;
                        await Task.Delay(time).ConfigureAwait(false);

                        nowid++;

                        if (nowid >= _framePaths.Length)
                        {
                            if (IsLoop)
                            {
                                nowid = 0;
                                continue;
                            }
                            if (ctrl.Type == ControlType.Continue)
                            {
                                ctrl.Type = ControlType.Status_Quo;
                                nowid = 0;
                                continue;
                            }
                            ctrl.Finish();
                            try
                            {
                                ctrl.EndAction?.Invoke();
                            }
                            catch (Exception cbErr)
                            {
                                Console.WriteLine($"[VPet] APNG 回调异常 {GraphInfo}: {cbErr.Message}");
                            }
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    IsFail = true;
                    FailMessage = e.Message;
                    Console.WriteLine($"[VPet] APNG 播放异常 {GraphInfo}: {e.Message}");
                    ctrl.Finish();
                    try { ctrl.EndAction?.Invoke(); }
                    catch (Exception cbErr) { Console.WriteLine($"[VPet] APNG 回调异常: {cbErr.Message}"); }
                }
            });
        }

        private async Task<bool> EnsureReadyAsync()
        {
            if (IsReady && _framePaths.Length > 0)
                return true;
            if (_loadTask != null)
            {
                try { await _loadTask.ConfigureAwait(false); } catch { }
            }
            return IsReady && _framePaths.Length > 0;
        }

        /// <summary>
        /// 停止动画
        /// </summary>
        public void Stop(bool stopEndAction)
        {
            if (Control == null) return;
            if (stopEndAction)
                Control.EndAction = null;
            Control.Type = ControlType.Stop;
        }

        /// <summary>
        /// 设置为继续播放
        /// </summary>
        public void SetContinue()
        {
            if (Control == null || Control.PlayState != true)
            {
                if (_lastParent != null)
                    RunCore(_lastParent, _lastEndAction);
                return;
            }
            Control.SetContinue();
        }

        /// <summary>
        /// 修改最后使用时间为当前时间
        /// </summary>
        public void Touch()
        {
            LastUseTimeTicks = DateTime.Now.Ticks;
        }

        /// <summary>
        /// 清理空闲缓存
        /// 帧图在磁盘缓存目录里, 帧索引只是字符串数组, 这里不能清 —
        /// 清了这个动画就再也播不出来(索引只在 startup 时建立一次)
        /// </summary>
        public void CleanupIdleCache(long nowTicks)
        {
        }

        /// <summary>
        /// 释放资源(帧图留在磁盘缓存中, 下次启动直接复用)
        /// </summary>
        public void Dispose()
        {
            _graphCore = null;
            _framePaths = Array.Empty<string>();
            _frameTimes = Array.Empty<int>();
            IsReady = false;
        }

        /// <summary>
        /// 加载 APNG 动画
        /// </summary>
        public static void LoadGraph(GraphCoreAvalonia graph, FileSystemInfo path, ILine info)
        {
            if (!(path is FileInfo file))
                return;
            // 早期 MOD 把动图存成 .apng, 主流做法是直接 .png, 两种都收
            var ext = path.Extension.ToLowerInvariant();
            if (ext != ".png" && ext != ".apng")
                return;

            // 声明成 apnganimation 但其实是普通 PNG: 退回静态图, 不然会什么都不显示
            if (!ApngDecoder.LooksLikeApng(file.FullName))
            {
                Picture.LoadGraph(graph, path, info);
                return;
            }

            bool isLoop = false;
            try
            {
                isLoop = info[(gbol)"loop"];
            }
            catch
            {
                // lps 没写 loop 就用 false: 与 PNGAnimation 一致
                // (APNG 的 acTL 播放次数只做记录, 不改变状态机语义)
            }

            var pa = new APNGAnimation(graph, file.FullName, new GraphInfo(path, info), isLoop);
            graph.AddGraph(pa);
        }

        /// <summary>
        /// 解析 + 合成帧
        /// </summary>
        private void startup()
        {
            try
            {
                if (!File.Exists(Path))
                    throw new FileNotFoundException($"Can not find file: {Path}");

                int maxWidth = 0;
                try { maxWidth = _graphCore?.Resolution ?? 0; } catch { }

                var dir = string.IsNullOrEmpty(GraphCoreAvalonia.CachePath)
                    ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VPetCache")
                    : GraphCoreAvalonia.CachePath;

                var info = ApngDecoder.Load(Path, dir, maxWidth);
                DecodeMs = info.DecodeMs;
                FromCache = info.FromCache;
                LoopCount = info.LoopCount;

                if (!info.IsAnimated || info.Frames.Count == 0)
                    throw new InvalidDataException("文件里没有可播放的 APNG 帧");

                var paths = new string[info.Frames.Count];
                var times = new int[info.Frames.Count];
                for (int i = 0; i < info.Frames.Count; i++)
                {
                    paths[i] = info.Frames[i].FilePath;
                    times[i] = info.Frames[i].DelayMs;
                }

                _framePaths = paths;
                _frameTimes = times;
                IsReady = true;
                IsFail = false;
                Console.WriteLine($"[VPet] APNG: {System.IO.Path.GetFileName(Path)} 帧={info.Frames.Count} " +
                                  $"画布={info.Width}x{info.Height} 播放次数={info.LoopCount} " +
                                  $"{(info.FromCache ? "缓存命中" : $"解码 {info.DecodeMs}ms")}");
            }
            catch (Exception e)
            {
                IsFail = true;
                FailMessage = $"--APNGAnimation--{GraphInfo}--\nPath: {Path}\n{e.Message}";
                Console.WriteLine($"[VPet] APNG 加载失败 {System.IO.Path.GetFileName(Path)}: {e.Message}");

                // 兜底: 解码失败也要把宠物画出来(显示首帧/默认图), 否则那个动作会是一片空白,
                // 用户只会看到"宠物消失了", 比不播动画更糟
                try
                {
                    if (File.Exists(Path))
                    {
                        _framePaths = new[] { Path };
                        _frameTimes = new[] { 1000 };
                        IsReady = true;
                        Console.WriteLine($"[VPet] APNG 退化为静态图: {System.IO.Path.GetFileName(Path)}");
                    }
                }
                catch { }
            }
        }
    }
}
