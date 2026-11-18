using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions;
using LinePutScript;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions.Interfaces;
using VPetAvalonia.Abstractions.Input;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 图像显示核心 (Avalonia版本)
    /// </summary>
    public class GraphCoreAvalonia : IGraphCore
    {
        /// <summary>
        /// 桌宠图形渲染的分辨率,越高图形越清晰
        /// </summary>
        public int Resolution { get; set; } = 1000;
        
        /// <summary>
        /// 动画缓存空闲超时时间,超过该时间未使用的动画将被释放,单位 Ticks
        /// </summary>
        public long IdleCacheTimeout { get; set; } = TimeSpan.FromMinutes(2).Ticks;

        private readonly IDispatcher _dispatcher;
        private readonly Timer _cleanTimer;
        
        public GraphCoreAvalonia(int resolution, IDispatcher dispatcher, Config config)
        {
            _dispatcher = dispatcher;
            if (!Directory.Exists(CachePath))
                Directory.CreateDirectory(CachePath);
            CommConfig["Cache"] = new List<string>();
            Resolution = resolution;
            
            _cleanTimer = new Timer((_) =>
            {
                if (GraphsALL == null)
                    return;
                long cleanTicks = DateTime.Now.Ticks - IdleCacheTimeout;
                foreach (var graph in GraphsALL)
                {
                    graph.CleanupIdleCache(cleanTicks);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            
            GraphConfigInternal = config;
        }
        
        /// <summary>
        /// 缓存路径,用于缓存图像
        /// 统信UOS 上程序常安装在 /opt 或 /usr 等只读目录, 写程序目录会直接抛异常,
        /// 因此统一改到当前用户的本地数据目录 (~/.local/share/VPet/cache)
        /// </summary>
        public static string CachePath = BuildCachePath();

        private static string BuildCachePath()
        {
            try
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(baseDir))
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    baseDir = string.IsNullOrEmpty(home)
                        ? Path.GetTempPath()
                        : Path.Combine(home, ".local", "share");
                }
                return Path.Combine(baseDir, "VPet", "cache");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "VPetCache");
            }
        }
        
        /// <summary>
        /// 提供给缓存文件生成的锁,用于防止多线程同时生成同一个缓存文件
        /// </summary>
        public static readonly ConcurrentDictionary<string, SemaphoreSlim> SpriteSheetBuildLocks = 
            new ConcurrentDictionary<string, SemaphoreSlim>();

        /// <summary>
        /// 图像字典 动画名字->状态+动作->动画
        /// </summary>
        public Dictionary<string, Dictionary<GraphInfo.AnimatType, List<IGraph>>> GraphsList = 
            new Dictionary<string, Dictionary<GraphInfo.AnimatType, List<IGraph>>>();
        
        /// <summary>
        /// 所有图像列表, 用于释放资源
        /// </summary>
        public List<IGraph> GraphsALL = new List<IGraph>();
        
        /// <summary>
        /// 通用UI资源 (Avalonia版本)
        /// </summary>
        public Dictionary<string, object> CommUIElements = new Dictionary<string, object>();
        
        /// <summary>
        /// 通用设置属性/方法
        /// </summary>
        public Dictionary<string, object> CommConfig = new Dictionary<string, object>();

/// <summary>
        /// 图形配置 (内部使用)
        /// </summary>
        public Config GraphConfigInternal { get; private set; } = null!;

        /// <summary>
        /// 图形配置
        /// </summary>
        public IGraphConfig GraphConfig => GraphConfigInternal;

        /// <summary>
        /// 获取调度器
        /// </summary>
        public IDispatcher Dispatcher => _dispatcher;
        
        /// <summary>
        /// 动画设置
        /// </summary>
public class Config : IGraphConfig
        {
            /// <summary>
            /// 创建空配置
            /// </summary>
            public Config()
            {
                TouchRaisedLocate = new Point[4];
                TouchRaisedSize = new Size[4];
                RaisePoint = new Point[4];
                for (int i = 0; i < 4; i++)
                {
                    TouchRaisedLocate[i] = new Point(0, 0);
                    TouchRaisedSize[i] = new Size(0, 0);
                    RaisePoint[i] = new Point(0, 0);
                }
            }

            /// <summary>
            /// 通过 LinePutScript 创建配置
            /// </summary>
            /// <param name="lps">配置数据</param>
            public Config(object lps) : this()
            {
                if (lps is LpsDocument doc)
                    Set(doc);
            }

            /// <summary>
            /// 摸头触发位置
            /// </summary>
            public Point TouchHeadLocate;

            /// <summary>
            /// 摸头触发大小
            /// </summary>
            public Size TouchHeadSize;

            /// <summary>
            /// 摸身体触发位置
            /// </summary>
            public Point TouchBodyLocate;

            /// <summary>
            /// 摸身体触发大小
            /// </summary>
            public Size TouchBodySize;

            /// <summary>
            /// 捏脸触发位置 (vup.lps 的 pinch 行: px/py/sw/sh)
            /// 原版 MainWindow: 有 pinch 行就 Insert(0) 一个长按触摸区
            /// </summary>
            public Point PinchLocate;

            /// <summary>
            /// 捏脸触发大小 (默认 0 = 该宠物不支持捏脸)
            /// </summary>
            public Size PinchSize;

            /// <summary>
            /// 抬起位置 (按 ModeType: Happy/Nomal/PoorCondition/Ill)
            /// </summary>
            public Point[] TouchRaisedLocate;

            /// <summary>
            /// 抬起大小
            /// </summary>
            public Size[] TouchRaisedSize;

            /// <summary>
            /// 提起定位点
            /// </summary>
            public Point[] RaisePoint;

            /// <summary>
            /// 所有移动
            /// </summary>
            public List<Move> Moves = new List<Move>();

            /// <summary>
            /// 所有工作/学习
            /// </summary>
            public List<Work> Works = new List<Work>();

            /// <summary>
            /// 动画持续时间配置
            /// </summary>
            private Dictionary<string, int> _duration = new Dictionary<string, int>();

            /// <summary>
            /// 移动配置列表
            /// </summary>
            public IReadOnlyList<Move> MovesList => Moves.AsReadOnly();

            /// <summary>
            /// 工作配置列表
            /// </summary>
            public IReadOnlyList<object> WorksList => Works.Cast<object>().ToList().AsReadOnly();

            /// <summary>
            /// 获取持续时间
            /// </summary>
            /// <param name="graphName">动画名称</param>
            /// <returns>持续时间</returns>
            public int GetDuration(string graphName)
            {
                if (string.IsNullOrEmpty(graphName))
                    return 10;
                if (_duration.TryGetValue(graphName.ToLowerInvariant(), out var v))
                    return v;
                return 10;
            }

            /// <summary>
            /// 获取触摸头部位置
            /// </summary>
            public IPoint GetTouchHeadLocate(ModeType mode) => TouchHeadLocate;

            /// <summary>
            /// 获取触摸抬起位置
            /// </summary>
            public IPoint GetTouchRaisedLocate(ModeType mode)
            {
                int i = (int)mode;
                if (TouchRaisedLocate == null || i < 0 || i >= TouchRaisedLocate.Length)
                    return new Point(0, 0);
                return TouchRaisedLocate[i];
            }

            /// <summary>
            /// 读取配置中的 double, 失败时使用默认值
            /// </summary>
            private static double GD(ILine line, string name, double def = 0)
            {
                try
                {
                    return line.GetDouble(name, def);
                }
                catch
                {
                    return def;
                }
            }

            /// <summary>
            /// 加载更多设置,新的替换后来的,允许空内容
            /// </summary>
            public void Set(object lps)
            {
                if (!(lps is LpsDocument doc))
                    return;
                try
                {
                    var th = doc["touchhead"];
                    if (th != null)
                    {
                        var px = GD(th, "px");
                        var py = GD(th, "py");
                        var sw = GD(th, "sw");
                        var sh = GD(th, "sh");
                        if (py != 0)
                            TouchHeadLocate = new Point(px, py);
                        if (sw != 0 || sh != 0)
                            TouchHeadSize = new Size(sw, sh);
                    }
                    var tb = doc["touchbody"];
                    if (tb != null)
                    {
                        var px = GD(tb, "px");
                        var py = GD(tb, "py");
                        var sw = GD(tb, "sw");
                        var sh = GD(tb, "sh");
                        if (py != 0)
                            TouchBodyLocate = new Point(px, py);
                        if (sw != 0 || sh != 0)
                            TouchBodySize = new Size(sw, sh);
                    }
                    var pn = doc["pinch"];
                    if (pn != null)
                    {
                        var px = GD(pn, "px");
                        var py = GD(pn, "py");
                        var sw = GD(pn, "sw");
                        var sh = GD(pn, "sh");
                        if (py != 0)
                            PinchLocate = new Point(px, py);
                        if (sw != 0 || sh != 0)
                            PinchSize = new Size(sw, sh);
                    }

                    var tr = doc["touchraised"];
                    if (tr != null)
                    {
                        string[] modes = { "happy", "nomal", "poorcondition", "ill" };
                        for (int i = 0; i < 4; i++)
                        {
                            var m = modes[i];
                            var py = GD(tr, m + "_py");
                            if (py != 0)
                                TouchRaisedLocate[i] = new Point(GD(tr, m + "_px"), py);
                            var sh = GD(tr, m + "_sh");
                            if (sh != 0)
                                TouchRaisedSize[i] = new Size(GD(tr, m + "_sw"), sh);
                        }
                    }

                    var rp = doc["raisepoint"];
                    if (rp != null)
                    {
                        string[] modes = { "happy", "nomal", "poorcondition", "ill" };
                        for (int i = 0; i < 4; i++)
                        {
                            var m = modes[i];
                            var ry = GD(rp, m + "_y");
                            if (ry != 0)
                                RaisePoint[i] = new Point(GD(rp, m + "_x"), ry);
                        }
                    }

                    var dur = doc["duration"];
                    if (dur != null)
                    {
                        foreach (ISub sub in dur)
                        {
                            if (sub == null || string.IsNullOrEmpty(sub.Name))
                                continue;
                            if (int.TryParse(sub.Info, out var v) && v > 0)
                                _duration[sub.Name.ToLowerInvariant()] = v;
                        }
                    }

                    foreach (var line in doc.FindAllLine("work"))
                    {
                        try
                        {
                            var work = LinePutScript.Converter.LPSConvert.DeserializeObject<Work>(line);
                            if (work != null)
                                Works.Add(work);
                        }
                        catch
                        {
                            // 单个工作配置损坏不应影响整体
                        }
                    }
                    foreach (var line in doc.FindAllLine("move"))
                    {
                        try
                        {
                            var move = LinePutScript.Converter.LPSConvert.DeserializeObject<Move>(line);
                            if (move != null)
                            {
                                // LPSConvert 不会自动映射 graph 字段(原版靠 [Line] 特性),
                                // 手动补取, 否则所有 move 的动画名都是空串,
                                // EventTimer 触发移动时整条动画链断掉
                                if (string.IsNullOrEmpty(move.Graph))
                                {
                                    var g = line.Find("graph");
                                    if (g != null)
                                        move.Graph = g.Info.ToString().ToLowerInvariant();
                                }
                                if (!string.IsNullOrEmpty(move.Graph))
                                    Moves.Add(move);
                            }
                        }
                        catch
                        {
                            // 忽略
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[VPet] 宠物配置解析失败: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 获取所有动画
        /// </summary>
        public IReadOnlyList<IGraph> GetAllGraphs()
        {
            var all = new List<IGraph>();
            foreach (var graphs in GraphsList.Values)
            {
                foreach (var graphList in graphs.Values)
                {
                    all.AddRange(graphList);
                }
            }
            return all.AsReadOnly();
        }

        /// <summary>
        /// 添加动画
        /// </summary>
        public void AddGraph(IGraph graph)
        {
            if (graph == null) return;
            
            var name = graph.GraphInfo.Name;
            var animat = graph.GraphInfo.Animat;
            
            if (!GraphsList.TryGetValue(name, out var animatDict))
            {
                animatDict = new Dictionary<GraphInfo.AnimatType, List<IGraph>>();
                GraphsList[name] = animatDict;
            }
            
            if (!animatDict.TryGetValue(animat, out var graphList))
            {
                graphList = new List<IGraph>();
                animatDict[animat] = graphList;
            }
            
            graphList.Add(graph);

            // 登记到总表, 否则空闲缓存清理永远遍历不到任何动画
            if (!GraphsALL.Contains(graph))
                GraphsALL.Add(graph);

            // 添加到 GraphsName
            if (!GraphsName.TryGetValue(graph.GraphInfo.Type, out var names))
            {
                names = new HashSet<string>();
                GraphsName[graph.GraphInfo.Type] = names;
            }
            names.Add(name);
        }

        /// <summary>
        /// 移除动画
        /// </summary>
        public void RemoveGraph(IGraph graph)
        {
            if (graph == null) return;
            
            var name = graph.GraphInfo.Name;
            var animat = graph.GraphInfo.Animat;
            
            if (GraphsList.TryGetValue(name, out var animatDict) && 
                animatDict.TryGetValue(animat, out var graphList))
            {
                graphList.Remove(graph);
                if (graphList.Count == 0)
                {
                    animatDict.Remove(animat);
                    if (animatDict.Count == 0)
                    {
                        GraphsList.Remove(name);
                    }
                }
            }
            
            // 从 GraphsName 移除
            if (GraphsName.TryGetValue(graph.GraphInfo.Type, out var names))
            {
                names.Remove(name);
                if (names.Count == 0)
                {
                    GraphsName.Remove(graph.GraphInfo.Type);
                }
            }
        }

        /// <summary>
        /// 清理空闲缓存
        /// </summary>
        public void CleanupIdleCache()
        {
            long cleanTicks = DateTime.Now.Ticks - IdleCacheTimeout;
            foreach (var graph in GraphsALL)
            {
                graph.CleanupIdleCache(cleanTicks);
            }
        }

        /// <summary>
        /// 通过名字查找动画
        /// 与原版 GraphCore.FindGraph 一致: 当前状态找不到时按
        /// 「当前 → 相邻状态 → 任意非生病」的顺序降级。
        /// 少了这层降级会怎样: mod 里很多表情(say/shy/serious)只在 Nomal 下定义,
        /// 宠物处于 Happy/PoorCondition 时按精确匹配就查不到, 表现为
        /// "说话没表情 / 投喂没动画 / 找不到动画" 并打断回调链。
        /// 注意原版枚举顺序是 Happy=0, Nomal=1, PoorCondition=2, Ill=3,
        /// 所以"向下兼容"其实是 mode+1(更普通),"向上兼容"是 mode-1。
        /// </summary>
        public IGraph? FindGraph(string? graphName, GraphInfo.AnimatType animat, ModeType mode)
        {
            if (string.IsNullOrEmpty(graphName) || !GraphsList.TryGetValue(graphName, out var animatDict))
                return null;

            if (!animatDict.TryGetValue(animat, out var graphList) || graphList.Count == 0)
                return null;

            // 1. 精确匹配当前状态
            var list = graphList.Where(g => g.GraphInfo.ModeType == mode).ToList();
            if (list.Count > 0)
                return list[Function.Rnd.Next(list.Count)];

            // 2. 生病状态不做降级(原版行为: 生病就是要躺着, 不能拿别的状态顶替)
            if (mode == ModeType.Ill)
                return null;

            int i = (int)mode + 1;
            if (i < 3)
            {
                // 向下兼容的动画 (Happy -> Nomal -> PoorCondition)
                list = graphList.Where(g => (int)g.GraphInfo.ModeType == i).ToList();
                if (list.Count > 0)
                    return list[Function.Rnd.Next(list.Count)];
            }

            i = (int)mode - 1;
            if (i >= 1)
            {
                // 向上兼容的动画
                list = graphList.Where(g => (int)g.GraphInfo.ModeType == i).ToList();
                if (list.Count > 0)
                    return list[Function.Rnd.Next(list.Count)];
            }

            // 3. 实在找不到, 在非生病动画里随便挑一个
            list = graphList.Where(g => g.GraphInfo.ModeType != ModeType.Ill).ToList();
            if (list.Count > 0)
                return list[Function.Rnd.Next(list.Count)];

            return null;
        }

        /// <summary>
        /// 通过类型查找动画名字
        /// </summary>
        public string? FindName(GraphInfo.GraphType type)
        {
            if (GraphsName.TryGetValue(type, out var names) && names.Count > 0)
            {
                return names.ElementAt(Function.Rnd.Next(names.Count));
            }
            return null;
        }

        /// <summary>
        /// 通过名字查找所有匹配的动画
        /// </summary>
        public List<IGraph>? FindGraphs(string? graphName, GraphInfo.AnimatType animat, ModeType mode)
        {
            if (string.IsNullOrEmpty(graphName) || !GraphsList.TryGetValue(graphName, out var animatDict))
                return null;

            if (!animatDict.TryGetValue(animat, out var graphList))
                return null;

            var result = graphList.Where(g => g.GraphInfo.ModeType == mode).ToList();
            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// 动画名字字典 (类型 -> 名字集合)
        /// </summary>
        public Dictionary<GraphInfo.GraphType, HashSet<string>> GraphsName { get; } = new Dictionary<GraphInfo.GraphType, HashSet<string>>();

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cleanTimer?.Dispose();
            GraphsALL?.Clear();
            GraphsList.Clear();
            GraphsName.Clear();
        }
    }
}
