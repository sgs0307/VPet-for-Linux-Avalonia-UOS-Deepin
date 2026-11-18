using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions;
using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// PNG动画 (Avalonia版本)
    /// 一个目录下的多张 png 组成动画, 帧时间从文件名末尾解析: xxx_100.png -> 100ms
    /// </summary>
    public class PNGAnimation : IImageRun
    {
        /// <summary>
        /// 是否循环播放
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
        /// 图片资源目录
        /// </summary>
        public string Path { get; set; } = "";

        private GraphCoreAvalonia? _graphCore;

        /// <summary>
        /// 当前帧索引
        /// </summary>
        private int nowid;

        /// <summary>
        /// 帧文件路径列表
        /// </summary>
        private string[] _framePaths = Array.Empty<string>();

        /// <summary>
        /// 帧时间列表 (毫秒)
        /// </summary>
        private int[] _frameTimes = Array.Empty<int>();

        /// <summary>
        /// 上次使用时间戳
        /// </summary>
        public long LastUseTimeTicks { get; private set; }

        /// <summary>
        /// 新建 PNG 动画 (同步建立帧索引, 保证 Run 时一定就绪)
        /// </summary>
        public PNGAnimation(GraphCoreAvalonia graphCore, string path, FileInfo[] paths, GraphInfo graphinfo, bool isLoop = false)
        {
            _graphCore = graphCore;
            Path = path;
            GraphInfo = graphinfo;
            IsLoop = isLoop;
            Startup(paths);
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
            if (!IsReady || _framePaths.Length == 0)
            {
                endAction?.Invoke();
                return;
            }
            if (Control?.PlayState == true)
            {
                // 循环还活着: 让它下一帧检测到 Stop 后用新回调重播
                Control.Stop(() => RunCore(parent, endAction));
                return;
            }

            // 记录本次播放参数, SetContinue 在播放已结束时可直接重播
            _lastParent = parent;
            _lastEndAction = endAction;

            var ctrl = new TaskControl(endAction);
            Control = ctrl;
            nowid = 0;

            // 使用异步循环代替 Thread.Sleep + 递归 Task.Run:
            // 旧实现每帧新建线程并阻塞, 长时间循环会堆积线程, 在 UOS 上最终卡死
            _ = Task.Run(async () =>
            {
                try
                {
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
                            // 关键: 自然结束必须通知 Finish, 否则 PlayState 恒为 true,
                            // 下次重播会被挂在一个已退出的循环上, 动画从此不再播放
                            ctrl.Finish();
                            try
                            {
                                ctrl.EndAction?.Invoke();
                            }
                            catch (Exception cbErr)
                            {
                                // 回调链异常必须打出来, 否则静默断链, 画面定格后无迹可查
                                Console.WriteLine($"[VPet] 动画回调异常 {GraphInfo}: {cbErr.Message}");
                            }
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    IsFail = true;
                    FailMessage = e.Message;
                    Console.WriteLine($"[VPet] 动画播放异常 {GraphInfo}: {e.Message}");
                    ctrl.Finish();
                    try { ctrl.EndAction?.Invoke(); }
                    catch (Exception cbErr) { Console.WriteLine($"[VPet] 回调异常: {cbErr.Message}"); }
                }
            });
        }

        /// <summary>
        /// 上次播放的显示元素与结束回调 (供 SetContinue 在动画已结束时重播)
        /// </summary>
        private IUIElement? _lastParent;
        private Action? _lastEndAction;

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
            // 播放已结束(循环已退出)时, "继续"无处生效:
            // 直接重新播放, 否则连续触摸会让宠物定格不动
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
        /// </summary>
        public void CleanupIdleCache(long nowTicks)
        {
            if (Control?.PlayState == true)
                return;
            if (nowTicks < LastUseTimeTicks)
                return;

            // 注意: 这里不能清空 _framePaths / 把 IsReady 置 false。
            // 帧索引只是字符串数组, 占用极小; 一旦清空, 这个动画就再也播不出来
            // (索引只在构造时建立), 表现就是宠物跑着跑着突然不动了。
            // 真正吃内存的位图缓存由显示层(PetImageElement)统一管理。
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _graphCore = null;
            _framePaths = Array.Empty<string>();
            _frameTimes = Array.Empty<int>();
            IsReady = false;
        }

        /// <summary>
        /// 加载PNG动画
        /// </summary>
        public static void LoadGraph(GraphCoreAvalonia graph, FileSystemInfo path, ILine info)
        {
            if (!(path is DirectoryInfo p))
            {
                Picture.LoadGraph(graph, path, info);
                return;
            }
            // Linux 区分大小写, 不能用 "*.png" 直接匹配 .PNG
            var paths = Function.GetFilesByExtension(p, ".png");
            if (paths.Length == 0)
                return;
            if (paths.Length == 1)
            {
                Picture.LoadGraph(graph, paths[0], info);
                return;
            }

            bool isLoop = false;
            try
            {
                isLoop = info[(gbol)"loop"];
            }
            catch
            {
                // 保留默认值
            }

            var pa = new PNGAnimation(graph, p.FullName, paths, new GraphInfo(p, info), isLoop);
            if (pa.IsReady)
                graph.AddGraph(pa);
        }

        /// <summary>
        /// 初始化动画帧 (自然排序, 保证 1,2...10,11 的播放顺序)
        /// </summary>
        private void Startup(FileInfo[] paths)
        {
            try
            {
                var sorted = paths.OrderBy(x => x.Name, Comparer<string>.Create(Function.NaturalCompare)).ToArray();
                _framePaths = new string[sorted.Length];
                _frameTimes = new int[sorted.Length];

                for (int i = 0; i < sorted.Length; i++)
                {
                    _framePaths[i] = sorted[i].FullName;
                    _frameTimes[i] = ParseFrameTime(sorted[i].Name);
                }
                IsReady = _framePaths.Length > 0;
            }
            catch (Exception e)
            {
                IsFail = true;
                FailMessage = $"--PNGAnimation--{GraphInfo}--\nPath: {Path}\n{e.Message}";
            }
        }

        /// <summary>
        /// 从文件名解析帧时间: name_100.png -> 100ms
        /// </summary>
        public static int ParseFrameTime(string fileName)
        {
            var noExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(noExt))
                return 100;
            var idx = noExt.LastIndexOf('_');
            if (idx >= 0 && idx + 1 < noExt.Length)
            {
                if (int.TryParse(noExt.Substring(idx + 1), out var t) && t > 0)
                    return Math.Max(16, t);
            }
            return 100;
        }

        /// <summary>
        /// 获取帧文件路径
        /// </summary>
        public string GetFramePath(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _framePaths.Length)
                return "";
            return _framePaths[frameIndex];
        }

        /// <summary>
        /// 帧数量
        /// </summary>
        public int FrameCount => _framePaths.Length;
    }
}
