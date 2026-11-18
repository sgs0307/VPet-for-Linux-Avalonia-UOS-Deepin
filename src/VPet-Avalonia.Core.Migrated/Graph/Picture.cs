using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions;
using LinePutScript;
using System;
using System.IO;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 静态图像 (Avalonia版本)
    /// 负责: 从磁盘登记一张图片, 并在运行时把它交给 IUIElement 显示
    /// </summary>
    public class Picture : IImageRun
    {
        /// <summary>
        /// 新建新静态图像
        /// </summary>
        public Picture(GraphCoreAvalonia graphCore, string path, GraphInfo graphinfo, int length = 1000, bool isloop = false)
        {
            GraphInfo = graphinfo;
            IsLoop = isloop;
            Length = length;
            Path = path;
            GraphCore = graphCore;
            IsReady = !string.IsNullOrEmpty(path) && File.Exists(path);
            if (!IsReady)
            {
                IsFail = true;
                FailMessage = $"图片不存在: {path}";
            }
        }

        /// <summary>
        /// 图形信息
        /// </summary>
        public GraphInfo GraphInfo { get; private set; }

        /// <summary>
        /// 是否循环播放
        /// </summary>
        public bool IsLoop { get; set; }

        /// <summary>
        /// 显示长度
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 图片路径
        /// </summary>
        public string Path { get; set; } = "";

        private GraphCoreAvalonia? GraphCore;

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
        /// 上次使用时间戳
        /// </summary>
        public long LastUseTimeTicks { get; private set; }

        /// <summary>
        /// 从0开始运行该动画
        /// </summary>
        public void Run(IUIElement parent, Action? endAction = null)
        {
            Touch();
            if (!IsReady)
            {
                endAction?.Invoke();
                return;
            }
            if (Control?.PlayState == true)
            {
                Control.Stop(() => Run(parent, endAction));
                return;
            }

            _lastParent = parent;
            _lastEndAction = endAction;

            var ctrl = new TaskControl(endAction);
            Control = ctrl;

            _ = Task.Run(async () =>
            {
                try
                {
                    // 旧实现把 loop 播放写成"结束后递归调用 Run",
                    // 而 Run 会因 PlayState==true 走 Stop 分支, 等待一个已退出的任务, 永远挂死。
                    // 这里改成循环体内continue, 播放永不离开当前任务
                    while (true)
                    {
                        parent.SetContent(Path);
                        parent.Width = 500;
                        parent.Height = 500;
                        parent.Show();

                        await Task.Delay(Math.Max(16, Length));

                        if (ctrl.Type == ControlType.Stop)
                        {
                            ctrl.Finish();
                            ctrl.EndAction?.Invoke();
                            return;
                        }
                        if (ctrl.Type == ControlType.Status_Stoped)
                            return;

                        if (IsLoop)
                        {
                            if (ctrl.Type == ControlType.Continue)
                                ctrl.Type = ControlType.Status_Quo;
                            continue;
                        }
                        if (ctrl.Type == ControlType.Continue)
                        {
                            ctrl.Type = ControlType.Status_Quo;
                            continue;
                        }
                        // 自然结束必须 Finish, 否则 PlayState 恒为 true,
                        // 下次重播会被挂在已退出的任务上, 图片从此定格
                        ctrl.Finish();
                        try { ctrl.EndAction?.Invoke(); }
                        catch (Exception cbErr) { Console.WriteLine($"[VPet] 动画回调异常 {GraphInfo}: {cbErr.Message}"); }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    IsFail = true;
                    FailMessage = $"显示图片失败: {ex.Message}";
                    Console.WriteLine($"[VPet] 图片播放异常 {GraphInfo}: {ex.Message}");
                    ctrl.Finish();
                    try { ctrl.EndAction?.Invoke(); }
                    catch (Exception cbErr) { Console.WriteLine($"[VPet] 回调异常: {cbErr.Message}"); }
                }
            });
        }

        /// <summary>
        /// 上次播放的显示元素与结束回调 (供 SetContinue 在已停止时重播)
        /// </summary>
        private IUIElement? _lastParent;
        private Action? _lastEndAction;

        /// <summary>
        /// 指定图像控件准备运行该动画
        /// </summary>
        public Task Run(object img, Action? EndAction = null)
        {
            if (img is IUIElement ui)
            {
                Run(ui, EndAction);
                return Task.CompletedTask;
            }
            EndAction?.Invoke();
            return Task.CompletedTask;
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
            // 已停止时直接重播, 否则"继续"落在已退出的任务上, 画面定格
            if (Control == null || Control.PlayState != true)
            {
                if (_lastParent != null)
                    Run(_lastParent, _lastEndAction);
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
            // 静态图只有一条路径, 无需释放
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            GraphCore = null;
        }

        /// <summary>
        /// 加载静态图像
        /// </summary>
        public static void LoadGraph(GraphCoreAvalonia graph, FileSystemInfo path, ILine info)
        {
            if (path is DirectoryInfo dir)
            {
                // 传进来的是目录时, 取目录内第一张图片
                var files = Function.GetFilesByExtension(dir, ".png");
                if (files.Length == 0)
                    return;
                LoadGraph(graph, files[0], info);
                return;
            }

            if (!(path is FileInfo file) || !file.Exists)
                return;

            // 扩展名是 .png 但内部带 acTL 块 = APNG。MOD 只要把动图扔进资源目录就能用,
            // 不必在 info.lps 里改成 apnganimation(很多现成 MOD 都写成 picture)
            if (ApngDecoder.LooksLikeApng(file.FullName))
            {
                APNGAnimation.LoadGraph(graph, file, info);
                return;
            }

            int length = 1000;
            try
            {
                length = info.GetInt("length", 1000);
            }
            catch
            {
                // 保留默认值
            }
            if (length <= 0)
                length = 1000;

            bool isLoop = false;
            try
            {
                isLoop = info[(gbol)"loop"];
            }
            catch
            {
                // 保留默认值
            }

            var picture = new Picture(graph, file.FullName, new GraphInfo(path, info), length, isLoop);
            graph.AddGraph(picture);
        }
    }
}
