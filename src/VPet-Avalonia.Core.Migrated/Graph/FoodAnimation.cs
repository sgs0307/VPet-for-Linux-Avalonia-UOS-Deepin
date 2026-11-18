using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions;
using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 食物动画 (Avalonia版本)
    /// 支持显示前中后3层夹心动画
    /// </summary>
    public class FoodAnimation : IImageRun
    {
        /// <summary>
        /// 前层名字
        /// </summary>
        public string Front_Lay;
        /// <summary>
        /// 后层名字
        /// </summary>
        public string Back_Lay;
        /// <summary>
        /// 所有动画帧
        /// </summary>
        public List<Animation> Animations = new List<Animation>();

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
        public bool IsReady { get; private set; } = false;
        
        /// <summary>
        /// 是否读取失败
        /// </summary>
        public bool IsFail { get; private set; } = false;
        
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
        
        private GraphCoreAvalonia? _graphCore;

        /// <summary>
        /// 当前帧索引
        /// </summary>
        private int nowid;
        
        /// <summary>
        /// 上次使用时间戳
        /// </summary>
        public long LastUseTimeTicks { get; private set; }

        /// <summary>
        /// 创建食物动画 第二层夹心为运行时提供
        /// </summary>
        /// <param name="graphCore">动画核心</param>
        /// <param name="graphinfo">动画信息</param>
        /// <param name="front_Lay">前层 动画名</param>
        /// <param name="back_Lay">后层 动画名</param>
        /// <param name="animations">中间层运动轨迹</param>
        /// <param name="isLoop">是否循环</param>
        public FoodAnimation(GraphCoreAvalonia graphCore, GraphInfo graphinfo, string front_Lay,
            string back_Lay, ILine animations, bool isLoop = false)
        {
            _graphCore = graphCore;
            IsLoop = isLoop;
            GraphInfo = graphinfo;
            Front_Lay = front_Lay;
            Back_Lay = back_Lay;
            Animations = new List<Animation>();
            int i = 0;
            ISub sub = animations.Find("a" + i)!;
            while (sub != null)
            {
                Animations.Add(new Animation(this, sub));
                sub = animations.Find("a" + ++i)!;
            }
            IsReady = true;
        }

        /// <summary>
        /// 从0开始运行该动画
        /// </summary>
        /// <param name="parent">显示位置</param>
        /// <param name="endAction">停止动作</param>
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
            nowid = 0;
            var newControl = new TaskControl(endAction);
            Control = newControl;
            Task.Run(() => Animations[0].Run(parent, newControl));
        }
        
        /// <summary>
        /// 指定图像控件准备运行该动画
        /// </summary>
        /// <param name="img">用于显示的图像控件</param>
        /// <param name="EndAction">结束动画</param>
        /// <returns>准备好的任务</returns>
        public Task Run(object img, Action? EndAction = null)
        {
            Touch();
            if (!IsReady)
            {
                EndAction?.Invoke();
                return Task.CompletedTask;
            }
            if (Control?.PlayState == true)
            {
                Control.Stop(() => Run(img, EndAction));
                return Task.CompletedTask;
            }
            nowid = 0;
            var newControl = new TaskControl(EndAction);
            Control = newControl;
            return Task.Run(() => Animations[0].Run(img, newControl));
        }
        
        /// <summary>
        /// 停止动画
        /// </summary>
        /// <param name="stopEndAction">停止动画时是否不运行结束动画</param>
        public void Stop(bool stopEndAction)
        {
            if (Control == null) return;
            if (stopEndAction)
            {
                Control.EndAction = null;
            }
            Control.Type = ControlType.Stop;
        }
        
        /// <summary>
        /// 设置为继续播放
        /// </summary>
        public void SetContinue()
        {
            Control?.SetContinue();
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
        /// <param name="nowTicks">当前时间</param>
        public void CleanupIdleCache(long nowTicks)
        {
            if (Control?.PlayState == true)
                return;
            if (nowTicks < LastUseTimeTicks)
                return;
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _graphCore = null;
            Animations.Clear();
        }
        
        /// <summary>
        /// 加载食物动画
        /// </summary>
        /// <param name="graph">图形核心</param>
        /// <param name="path">动画路径</param>
        /// <param name="info">信息</param>
        public static void LoadGraph(GraphCoreAvalonia graph, FileSystemInfo path, ILine info)
        {
            bool isLoop = info[(gbol)"loop"];
            FoodAnimation pa = new FoodAnimation(graph, new GraphInfo(path, info), info[(gstr)"front_lay"]!, info[(gstr)"back_lay"]!, info, isLoop);
            graph.AddGraph(pa);
        }

        /// <summary>
        /// 单帧动画
        /// </summary>
        public class Animation
        {
            private FoodAnimation parent;
            /// <summary>
            /// 边距
            /// </summary>
            public double MarginX;
            /// <summary>
            /// 边距Y
            /// </summary>
            public double MarginY;
            /// <summary>
            /// 旋转角度
            /// </summary>
            public double Rotate = 0;
            /// <summary>
            /// 透明度
            /// </summary>
            public double Opacity = 1;
            /// <summary>
            /// 是否可见
            /// </summary>
            public bool IsVisiable = true;
            /// <summary>
            /// 宽度
            /// </summary>
            public double Width;
            /// <summary>
            /// 帧时间
            /// </summary>
            public int Time;

            /// <summary>
            /// 创建单帧动画
            /// </summary>
            /// <param name="parent">FoodAnimation</param>
            /// <param name="time">持续时间</param>
            /// <param name="wx">边距</param>
            /// <param name="width">宽度</param>
            /// <param name="rotate">旋转</param>
            /// <param name="isVisiable">是否可见</param>
            /// <param name="opacity">透明度</param>
            public Animation(FoodAnimation parent, int time, double wx, double width, double rotate = 0, bool isVisiable = true, double opacity = 1)
            {
                this.parent = parent;
                Time = time;
                MarginX = wx;
                Width = width;
                Rotate = rotate;
                IsVisiable = isVisiable;
                Opacity = opacity;
            }

            /// <summary>
            /// 从配置创建单帧动画
            /// </summary>
            /// <param name="parent">FoodAnimation</param>
            /// <param name="sub">配置</param>
            public Animation(FoodAnimation parent, ISub sub)
            {
                this.parent = parent;
                var strs = sub.GetInfos();
                Time = int.Parse(strs[0]);//0: Time
                if (strs.Length == 1)
                    IsVisiable = false;
                else
                {//1,2: Margin X,Y
                    Width = double.Parse(strs[3]);//3:Width
                    MarginX = double.Parse(strs[1]);
                    MarginY = double.Parse(strs[2]);
                    if (strs.Length > 4)
                    {
                        Rotate = double.Parse(strs[4]);//Rotate
                        if (strs.Length > 5)
                            Opacity = double.Parse(strs[5]);//Opacity
                    }
                }
            }

            /// <summary>
            /// 运行该图层
            /// </summary>
            /// <param name="parent">显示的图层</param>
            /// <param name="control">动画控制</param>
            public void Run(IUIElement parent, ITaskControl control)
            {
                // 先显示该图层
                if (IsVisiable)
                {
                    parent.SetContent("");
                    parent.Width = Width;
                    parent.Height = Width;
                    parent.X = MarginX;
                    parent.Y = MarginY;
                    parent.Show();
                }
                else
                {
                    parent.Hide();
                }

                // 等待帧时间
                Thread.Sleep(Time);

                // 判断是否要下一步
                switch (control.Type)
                {
                    case ControlType.Stop:
                        control.EndAction?.Invoke();
                        return;
                    case ControlType.Status_Stoped:
                        return;
                    case ControlType.Status_Quo:
                    case ControlType.Continue:
                        if (++this.parent.nowid >= this.parent.Animations.Count)
                        {
                            if (this.parent.IsLoop)
                            {
                                this.parent.nowid = 0;
                                Task.Run(() => this.parent.Animations[0].Run(parent, control));
                                return;
                            }
                            else if (control.Type == ControlType.Continue)
                            {
                                control.Type = ControlType.Status_Quo;
                                this.parent.nowid = 0;
                            }
                            else
                            {
                                control.Type = ControlType.Status_Stoped;
                                control.EndAction?.Invoke();
                                return;
                            }
                        }
                        this.parent.Animations[this.parent.nowid].Run(parent, control);
                        return;
                }
            }

            /// <summary>
            /// 运行该图层 (object版本)
            /// </summary>
            /// <param name="img">显示的图像控件</param>
            /// <param name="control">动画控制</param>
            public void Run(object img, ITaskControl control)
            {
                if (img is IUIElement uiElement)
                {
                    // 先显示该图层
                    if (IsVisiable)
                    {
                        uiElement.SetContent("");
                        uiElement.Width = Width;
                        uiElement.Height = Width;
                        uiElement.X = MarginX;
                        uiElement.Y = MarginY;
                        uiElement.Show();
                    }
                    else
                    {
                        uiElement.Hide();
                    }
                }

                // 等待帧时间
                Thread.Sleep(Time);

                // 判断是否要下一步
                switch (control.Type)
                {
                    case ControlType.Stop:
                        control.EndAction?.Invoke();
                        return;
                    case ControlType.Status_Stoped:
                        return;
                    case ControlType.Status_Quo:
                    case ControlType.Continue:
                        if (++parent.nowid >= parent.Animations.Count)
                        {
                            if (parent.IsLoop)
                            {
                                parent.nowid = 0;
                                Task.Run(() => parent.Animations[0].Run(img, control));
                                return;
                            }
                            else if (control.Type == ControlType.Continue)
                            {
                                control.Type = ControlType.Status_Quo;
                                parent.nowid = 0;
                            }
                            else
                            {
                                control.Type = ControlType.Status_Stoped;
                                control.EndAction?.Invoke();
                                return;
                            }
                        }
                        parent.Animations[parent.nowid].Run(img, control);
                        return;
                }
            }
        }
    }
}
