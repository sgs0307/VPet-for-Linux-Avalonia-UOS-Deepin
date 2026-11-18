using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 宠物加载器
    /// </summary>
    public class PetLoader
    {
        /// <summary>
        /// 动画数量
        /// </summary>
        public int GraphCount { get; private set; }

        /// <summary>
        /// 宠物图像
        /// </summary>
        public GraphCoreAvalonia Graph(int Resolution, IDispatcher dispatcher)
        {
            GraphCount = 0;
            var g = new GraphCoreAvalonia(Resolution, dispatcher, Config);
            foreach (var p in path)
            {
                var dir = Function.NormalizePath(p);
                if (!Path.IsPathRooted(dir))
                    dir = Path.GetFullPath(dir);
                GraphCount += LoadGraph(g, new DirectoryInfo(dir), dir);
            }
            return g;
        }

        /// <summary>
        /// 图像位置
        /// </summary>
        public List<string> path = new List<string>();

        /// <summary>
        /// 宠物介绍名字
        /// </summary>
        public string Name = "";

        /// <summary>
        /// 宠物介绍
        /// </summary>
        public string Intor = "";

        /// <summary>
        /// 宠物默认名字
        /// </summary>
        public string PetName = "";

        public GraphCoreAvalonia.Config Config = new GraphCoreAvalonia.Config();

        /// <summary>
        /// 仅供测试使用的空构造
        /// </summary>
        public PetLoader()
        {
        }

        public PetLoader(LpsDocument lps, DirectoryInfo directory)
        {
            var first = lps.First();
            if (first == null)
                throw new ArgumentException("宠物描述文件为空: " + directory.FullName, nameof(lps));

            Name = first.Info;
            Intor = first["intor"].Info;
            PetName = first["petname"].Info;
            // Linux 下路径分隔符与大小写均敏感, 统一规范化
            path.Add(Function.CombinePath(directory.FullName, first["path"].Info));
            Config = new GraphCoreAvalonia.Config(lps);
        }

        public delegate void LoadGraphDelegate(GraphCoreAvalonia graph, FileSystemInfo path, ILine info);

        /// <summary>
        /// 自定义图片加载方法
        /// </summary>
        public static Dictionary<string, LoadGraphDelegate> IGraphConvert = new Dictionary<string, LoadGraphDelegate>()
        {
            { "pnganimation", PNGAnimation.LoadGraph},
            { "apnganimation", APNGAnimation.LoadGraph },
            { "picture", Picture.LoadGraph },
            { "foodanimation", FoodAnimation.LoadGraph },
        };

        /// <summary>
        /// 加载图像动画
        /// </summary>
        /// <param name="graph">要加载的动画核心</param>
        /// <param name="di">当前历遍的目录</param>
        /// <param name="startuppath">起始目录</param>
        public static int LoadGraph(GraphCoreAvalonia graph, DirectoryInfo di, string startuppath)
        {
            if (di == null || !di.Exists)
                return 0;
            int GraphCount = 0;
            var list = di.EnumerateDirectories();

            // info.lps 在 Linux 上大小写敏感, 做一次不敏感查找
            var infoPath = Function.FindEntryCaseInsensitive(di.FullName, "info.lps");
            if (infoPath != null)
            {
                //如果自带描述信息,则手动加载
                LpsDocument lps = new LpsDocument(File.ReadAllText(infoPath));
                foreach (ILine line in lps)
                {
                    if (IGraphConvert.TryGetValue(line.Name.ToLowerInvariant(), out var func))
                    {
                        line.Add(new Sub("startuppath", startuppath));
                        var str = Function.NormalizePath(line.GetString("path"));
                        if (!string.IsNullOrEmpty(str))
                        {
                            // 支持 '\' 分隔的多级路径
                            var p = Path.Combine(di.FullName, str);
                            var real = File.Exists(p) || Directory.Exists(p)
                                ? p
                                : Function.FindEntryCaseInsensitive(di.FullName, str);
                            if (real != null && Directory.Exists(real))
                                func.Invoke(graph, new DirectoryInfo(real), line);
                            else if (real != null && File.Exists(real))
                                func.Invoke(graph, new FileInfo(real), line);
                            else
                                Console.WriteLine("[VPet] 资源不存在: " + p);
                        }
                        else
                            func.Invoke(graph, di, line);
                        GraphCount++;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(line.Name))
                            Console.WriteLine("[VPet] 未知动画类型: " + line.Name.ToLowerInvariant());
                    }
                }
            }
            else if (!list.Any())
            {//开始自动生成
                var paths = di.GetFiles()
                    .Where(x => IsImageFile(x.Extension))
                    .ToArray();
                if (paths.Length == 0)
                    return GraphCount;
                if (paths.Length == 1)
                    Picture.LoadGraph(graph, paths[0], new Line("picture", "", "", new Sub("startuppath", startuppath)));
                else
                    PNGAnimation.LoadGraph(graph, di, new Line("pnganimation", "", "", new Sub("startuppath", startuppath)));
                GraphCount++;
            }
            else
            {
                // 目录遍历顺序在 Linux 上不保证稳定, 排序保证每次启动表现一致
                foreach (var p in list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    GraphCount += LoadGraph(graph, p, startuppath);
                }
            }
            return GraphCount;
        }

        /// <summary>
        /// 判断是否为支持的图片扩展名(大小写不敏感)
        /// </summary>
        public static bool IsImageFile(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;
            var ext = extension.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif";
        }
    }
}
