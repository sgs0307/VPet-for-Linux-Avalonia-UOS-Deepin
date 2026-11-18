using LinePutScript;
using LinePutScript.Converter;
using System;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 工作/学习配置
    /// </summary>
    public class Work : ICloneable
    {
        /// <summary>
        /// 类型
        /// </summary>
        public enum WorkType { Work, Study, Play }

        /// <summary>
        /// 工作/学习
        /// </summary>
        [Line(ignoreCase: true)]
        public WorkType Type { get; set; }

        /// <summary>
        /// 工作名称
        /// </summary>
        [Line(ignoreCase: true)]
        public string Name { get; set; } = "";

        /// <summary>
        /// 使用动画名称
        /// </summary>
        [Line(ignoreCase: true)]
        public string Graph { get; set; } = "";

        /// <summary>
        /// 工作盈利/学习基本倍率
        /// </summary>
        [Line(ignoreCase: true)]
        public double MoneyBase { get; set; }

        /// <summary>
        /// 工作体力(食物)消耗倍率
        /// </summary>
        [Line(ignoreCase: true)]
        public double StrengthFood { get; set; }

        /// <summary>
        /// 工作体力(饮料)消耗倍率
        /// </summary>
        [Line(ignoreCase: true)]
        public double StrengthDrink { get; set; }

        /// <summary>
        /// 心情消耗倍率
        /// </summary>
        [Line(ignoreCase: true)]
        public double Feeling { get; set; }

        /// <summary>
        /// 等级限制
        /// </summary>
        [Line(ignoreCase: true)]
        public int LevelLimit { get; set; }

        /// <summary>
        /// 花费时间(分钟)
        /// </summary>
        [Line(ignoreCase: true)]
        public int Time { get; set; }

        /// <summary>
        /// 完成奖励倍率(0+)
        /// </summary>
        [Line(ignoreCase: true)]
        public double FinishBonus { get; set; }

        // ---- 进度条外观: 原版每个工作一套配色 (vup.lps 里 work 条目就带这些键) ----
        // 移植时漏了这组字段, LPSConvert 只映射带 [Line] 的属性, 所以进度条一直是同一套天蓝,
        // 烧烤的棕、学画画的橄榄绿全丢了。恢复后由 WorkTimer.ApplyStyle 应用。

        /// <summary>进度条边框色 (RRGGBB, 不带 #)</summary>
        [Line(ignoreCase: true)]
        public string BorderBrush { get; set; } = "0290D5";

        /// <summary>进度条底色</summary>
        [Line(ignoreCase: true)]
        public string Background { get; set; } = "81d4fa";

        /// <summary>按钮底色</summary>
        [Line(ignoreCase: true)]
        public string ButtonBackground { get; set; } = "0286C6";

        /// <summary>按钮文字色</summary>
        [Line(ignoreCase: true)]
        public string ButtonForeground { get; set; } = "ffffff";

        /// <summary>进度条文字色</summary>
        [Line(ignoreCase: true)]
        public string Foreground { get; set; } = "0286C6";

        /// <summary>显示位置 X (相对窗口左上)</summary>
        [Line(ignoreCase: true)]
        public double Left { get; set; } = 100;

        /// <summary>显示位置 Y</summary>
        [Line(ignoreCase: true)]
        public double Top { get; set; } = 160;

        /// <summary>进度条宽度 (高度按 180/300 等比)</summary>
        [Line(ignoreCase: true)]
        public double Width { get; set; } = 300;

        /// <summary>收益效率估值 (对应原版 ExtensionFunction.Get)</summary>
        public double Get()
        {
            var raw = Math.Abs(MoneyBase) * (1 + FinishBonus / 2) + 1;
            return Type == WorkType.Work
                ? Math.Pow(Math.Abs(raw), 1.25) * Math.Sign(raw)
                : Math.Pow(Math.Abs(raw / 10), 1.25) * Math.Sign(raw);
        }

        /// <summary>
        /// 显示工作/学习动画
        /// </summary>
        /// <param name="m">主显示区域</param>
        public void Display(object m)
        {
            if (m is VPetAvalonia.Abstractions.Interfaces.IDisplayHost host)
            {
                host.Display(Graph, VPetAvalonia.Abstractions.GraphInfo.AnimatType.A_Start, () => host.DisplayBLoopingForce(Graph));
            }
        }

        /// <summary>
        /// 克隆相同的工作/学习
        /// </summary>
        public object Clone()
        {
            return new Work
            {
                Type = this.Type,
                Name = this.Name,
                Graph = this.Graph,
                MoneyBase = this.MoneyBase,
                StrengthFood = this.StrengthFood,
                StrengthDrink = this.StrengthDrink,
                Feeling = this.Feeling,
                LevelLimit = this.LevelLimit,
                Time = this.Time,
                FinishBonus = this.FinishBonus,
                BorderBrush = this.BorderBrush,
                Background = this.Background,
                ButtonBackground = this.ButtonBackground,
                ButtonForeground = this.ButtonForeground,
                Foreground = this.Foreground,
                Left = this.Left,
                Top = this.Top,
                Width = this.Width,
            };
        }
    }
}