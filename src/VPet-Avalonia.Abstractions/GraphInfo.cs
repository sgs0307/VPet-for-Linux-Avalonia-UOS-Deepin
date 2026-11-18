using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LinePutScript;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Abstractions
{
    /// <summary>
    /// 动画信息
    /// </summary>
    /// 新版本动画类型是根据整体类型+名字定义而成
    /// 动画类型->动画名字
    /// 动画名字->状态+动作->动画
    /// 类型: 为主要动作分类
    /// 动画名字: 用户自定义, 同名字动画支持相同随机,不再使用StoreRand
    /// 动作: 动画的动作 Start Loop End
    /// 状态: 动画的状态 Save.GameSave.ModeType
    public class GraphInfo
    {
        /// <summary>
        /// 用于Convert的空动画信息
        /// </summary>
        public GraphInfo()
        {
        }
        /// <summary>
        /// 创建动画信息
        /// </summary>
        /// <param name="name">动画名字: 用户自定义 同名字动画支持相同随机,不再使用StoreRand</param>
        /// <param name="animat">动作: 动画的动作 Start Loop End</param>
        /// <param name="type">类型: 主要动作分类</param>
        /// <param name="modeType">状态: 4种状态</param>
        public GraphInfo(string name, GraphType type = GraphType.Common, AnimatType animat = AnimatType.Single, ModeType modeType = ModeType.Nomal)
        {
            Name = name;
            Animat = animat;
            Type = type;
            ModeType = modeType;
        }
        /// <summary>
        /// 动画类型默认前文本: 用于从目录名推断动画类型
        /// 例: "touch_head" -> ["touch","head"]
        /// </summary>
        public static string[][] GraphTypeValue
        {
            get
            {
                if (_graphTypeValue == null)
                {
                    var list = new List<string[]>();
                    foreach (string v in Enum.GetNames(typeof(GraphType)))
                        list.Add(v.ToLowerInvariant().Split('_'));
                    _graphTypeValue = list.ToArray();
                }
                return _graphTypeValue;
            }
        }
        private static string[][]? _graphTypeValue;

        /// <summary>
        /// 通过文件位置和信息获取动画信息
        /// 规则与原版 VPet 一致: 先从 info.lps 显式声明读取, 未声明则从路径名推断
        /// </summary>
        /// <param name="path">文件夹位置</param>
        /// <param name="info">信息</param>
        public GraphInfo(FileSystemInfo path, ILine info)
        {
            // 取相对 startuppath 之后的路径片段
            string startup = "";
            try
            {
                startup = info[(gstr)"startuppath"] ?? "";
            }
            catch
            {
                startup = "";
            }
            if (string.IsNullOrEmpty(startup))
                startup = "";

            string pn;
            var full = path.FullName;
            if (path is DirectoryInfo)
                pn = full.ToLowerInvariant();
            else
                pn = full.Substring(0, full.Length - path.Extension.Length).ToLowerInvariant();

            // 去掉 startuppath 前缀, 只保留 MOD 内部相对路径
            var startupLower = startup.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
            var fullLower = pn.Replace('\\', '/');
            if (startupLower.Length > 0 && fullLower.StartsWith(startupLower))
                pn = fullLower.Substring(startupLower.Length).TrimStart('/');

            var path_name = pn.Replace('\\', '_').Replace('/', '_').Split('_').ToList();
            path_name.RemoveAll(string.IsNullOrWhiteSpace);

            // 状态 ModeType
            ModeType modetype;
            var modeStr = "";
            try { modeStr = info[(gstr)"mode"] ?? ""; } catch { }
            if (!string.IsNullOrWhiteSpace(modeStr) &&
                Enum.TryParse(modeStr, true, out ModeType parsedMode))
            {
                modetype = parsedMode;
            }
            else if (path_name.Remove("happy"))
            {
                modetype = ModeType.Happy;
            }
            else if (path_name.Remove("nomal"))
            {
                modetype = ModeType.Nomal;
            }
            else if (path_name.Remove("poorcondition"))
            {
                modetype = ModeType.PoorCondition;
            }
            else if (path_name.Remove("ill"))
            {
                modetype = ModeType.Ill;
            }
            else
            {
                modetype = ModeType.Nomal;
            }

            // 动画类型 GraphType
            GraphType graphtype;
            var graphStr = "";
            try { graphStr = info[(gstr)"graph"] ?? ""; } catch { }
            if (!string.IsNullOrWhiteSpace(graphStr) &&
                Enum.TryParse(graphStr, true, out GraphType parsedGraph))
            {
                graphtype = parsedGraph;
            }
            else
            {
                graphtype = GraphType.Common;
                var gv = GraphTypeValue;
                for (int i = 0; i < gv.Length; i++)
                {
                    int index = path_name.IndexOf(gv[i][0]);
                    if (index < 0)
                        continue;
                    bool ismatch = true;
                    for (int b = 1; b < gv[i].Length && b + index < path_name.Count; b++)
                    {
                        if (path_name[index + b] != gv[i][b])
                        {
                            ismatch = false;
                            break;
                        }
                    }
                    if (ismatch)
                    {
                        graphtype = (GraphType)i;
                        path_name.RemoveRange(index, Math.Min(gv[i].Length, path_name.Count - index));
                        break;
                    }
                }
            }

            // 动画动作 AnimatType
            AnimatType animatType;
            var animatStr = "";
            try { animatStr = info[(gstr)"animat"] ?? ""; } catch { }
            if (!string.IsNullOrWhiteSpace(animatStr) &&
                Enum.TryParse(animatStr, true, out AnimatType parsedAnimat))
            {
                animatType = parsedAnimat;
            }
            else if (path_name.Remove("a") || path_name.Remove("start"))
            {
                animatType = AnimatType.A_Start;
            }
            else if (path_name.Remove("b") || path_name.Remove("loop"))
            {
                animatType = AnimatType.B_Loop;
            }
            else if (path_name.Remove("c") || path_name.Remove("end"))
            {
                animatType = AnimatType.C_End;
            }
            else if (path_name.Remove("single"))
            {
                animatType = AnimatType.Single;
            }
            else
            {
                animatType = AnimatType.Single;
            }

            // 动画名字
            Name = info.Info ?? "";
            if (string.IsNullOrWhiteSpace(Name))
            {
                while (path_name.Count > 0 && (double.TryParse(path_name[path_name.Count - 1], out _) || path_name[path_name.Count - 1].StartsWith("~")))
                {
                    path_name.RemoveAt(path_name.Count - 1);
                }
                if (path_name.Count > 0)
                    Name = path_name[path_name.Count - 1];
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = graphtype.ToString().ToLowerInvariant();
            }

            Type = graphtype;
            Animat = animatType;
            ModeType = modetype;
        }
        /// <summary>
        /// 类型: 主要动作分类
        /// </summary>
        /// * 为必须有的动画
        public enum GraphType
        {
            /// <summary>
            /// 通用动画,用于被被其他动画调用或者mod等用途
            /// </summary>
            /// 不被默认启用/使用的 不包含在GrapType
            Common,
            /// <summary>
            /// 被提起动态 *
            /// </summary>
            Raised_Dynamic,
            /// <summary>
            /// 被提起静态 (开始&循环&结束) *
            /// </summary>
            Raised_Static,
            /// <summary>
            /// 现在所有会动的东西都是MOVE
            /// </summary>
            Move,
            /// <summary>
            /// 呼吸 *
            /// </summary>
            Default,
            /// <summary>
            /// 摸头 (开始&循环&结束)
            /// </summary>
            Touch_Head,
            /// <summary>
            /// 摸身体 (开始&循环&结束)
            /// </summary>
            Touch_Body,
            /// <summary>
            /// 空闲 (包括下蹲/无聊等通用空闲随机动画) (开始&循环&结束)
            /// </summary>
            Idel,
            /// <summary>
            /// 睡觉 (开始&循环&结束) *
            /// </summary>
            Sleep,
            /// <summary>
            /// 说话 (开始&循环&结束) *
            /// </summary>
            Say,
            /// <summary>
            /// 待机 模式1 (开始&循环&结束)
            /// </summary>
            StateONE,
            /// <summary>
            /// 待机 模式2 (开始&循环&结束)
            /// </summary>
            StateTWO,
            /// <summary>
            /// 开机 *
            /// </summary>
            StartUP,
            /// <summary>
            /// 关机
            /// </summary>
            Shutdown,
            /// <summary>
            /// 工作 (开始&循环&结束) *
            /// </summary>
            Work,
            /// <summary>
            /// 向上切换状态
            /// </summary>
            Switch_Up,
            /// <summary>
            /// 向下切换状态
            /// </summary>
            Switch_Down,
            /// <summary>
            /// 口渴
            /// </summary>
            Switch_Thirsty,
            /// <summary>
            /// 饥饿
            /// </summary>
            Switch_Hunger,
            /// <summary>
            /// 躲藏(侧面)
            /// </summary>
            SideHide_Left_Main,
            /// <summary>
            /// 躲藏(侧面)显示
            /// </summary>
            SideHide_Left_Rise,
            /// <summary>
            /// 躲藏(侧面)
            /// </summary>
            SideHide_Right_Main,
            /// <summary>
            /// 躲藏(侧面)显示
            /// </summary>
            SideHide_Right_Rise,
        }
        /// <summary>
        /// 动作: 动画的动作 Start Loop End
        /// </summary>
        public enum AnimatType
        {
            /// <summary>
            /// 动画只有一个动作
            /// </summary>
            Single,
            /// <summary>
            /// 开始动作
            /// </summary>
            A_Start,
            /// <summary>
            /// 循环动作
            /// </summary>
            B_Loop,
            /// <summary>
            /// 结束动作
            /// </summary>
            C_End,
        }
        /// <summary>
        /// 动画名字: 用户自定义 同名字动画支持相同随机,不再使用StoreRand
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// 动作: 动画的动作 Start Loop End
        /// </summary>
        public AnimatType Animat { get; set; }
        /// <summary>
        /// 类型: 主要动作分类
        /// </summary>
        public GraphType Type { get; set; }
        /// <summary>
        /// 状态: 4种状态
        /// </summary>
        public ModeType ModeType { get; set; }
        ///// <summary>
        ///// 其他附带的储存信息
        ///// </summary>
        //public ILine Info { get; set; }
        public override string ToString()
        {
            return $"[{Name}]{Type}_{ModeType.ToString()[0]}{Animat.ToString()[0]}";
        }
    }
}