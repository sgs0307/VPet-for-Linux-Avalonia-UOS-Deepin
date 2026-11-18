using LinePutScript.Converter;
using VPetAvalonia.Abstractions.Input;

namespace VPetAvalonia.Abstractions
{
    /// <summary>
    /// 移动配置
    /// </summary>
    public class Move
    {
        /// <summary>
        /// 显示名称 (配置里没有 name 字段时为空)
        /// </summary>
        [Line(ignoreCase: true)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 动画名: 配置里的 graph 字段, 如 "walk.left",
        /// 与 MOVE/ 目录下的动画目录名对应。
        /// 必须带 [Line] 特性, LPSConvert 才会映射该字段
        /// </summary>
        [Line(ignoreCase: true)]
        public string Graph { get; set; } = "";

        public int Duration { get; set; } = 0;
        public IPoint[] Points { get; set; } = new IPoint[0];

        /// <summary>
        /// 用于查找动画的名字: graph 优先, 其次 name
        /// </summary>
        public string GraphName => !string.IsNullOrEmpty(Graph) ? Graph : Name;
    }
}