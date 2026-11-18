using LinePutScript;
using LinePutScript.Converter;
using System;
using System.IO;
using System.Linq;
using VPetAvalonia.Abstractions.Interfaces;
using static VPetAvalonia.Abstractions.Interfaces.ModeType;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 游戏存档
    /// </summary>
    public class GameSave : IGameSave
    {
        /// <summary>
        /// 宠物名字
        /// </summary>
        [Line(name: "name")]
        public string Name { get; set; } = "";
        [Line(name: "hostname")]
        public string HostName { get; set; } = "";

        /// <summary>
        /// 金钱
        /// </summary>
        [Line(Type = LPSConvert.ConvertType.ToFloat, Name = "money")]
        public double Money { get; set; }
        /// <summary>
        /// 经验值
        /// </summary>
        [Line(type: LPSConvert.ConvertType.ToFloat, name: "exp")] public double Exp { get; set; }
        /// <summary>
        /// 等级
        /// </summary>
        public int Level => Exp < 0 ? 1 : (int)(Math.Sqrt(Exp) / 10) + 1;
        /// <summary>
        /// 升级所需经验值
        /// </summary>
        /// <returns></returns>
        public int LevelUpNeed() => (int)(Math.Pow((Level) * 10, 2));
        /// <summary>
        /// 体力 0-100
        /// </summary>
        public double Strength { get => strength; set => strength = Math.Min(StrengthMax, Math.Max(0, value)); }

        public double StrengthMax { get; } = 100;

        [Line(Type = LPSConvert.ConvertType.ToFloat, IgnoreCase = true)]
        protected double strength { get; set; }
        /// <summary>
        /// 待补充的体力,随着时间缓慢加给桌宠
        /// </summary>//让游戏更有游戏性
        [Line(Type = LPSConvert.ConvertType.ToFloat, IgnoreCase = true)]
        public double StoreStrength { get; set; }
        /// <summary>
        /// 变化 体力
        /// </summary>
        public double ChangeStrength { get; set; } = 0;
        public void StrengthChange(double value)
        {
            ChangeStrength += value;
            Strength += value;
        }
        /// <summary>
        /// 饱腹度
        /// </summary>
        public double StrengthFood
        {
            get => strengthFood; set
            {
                value = Math.Min(100, value);
                if (value <= 0)
                {
                    Health += value;
                    strengthFood = 0;
                }
                else
                    strengthFood = value;
            }
        }
        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        protected double strengthFood { get; set; }
        /// <summary>
        /// 待补充的饱腹度,随着时间缓慢加给桌宠
        /// </summary>//让游戏更有游戏性
        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        public double StoreStrengthFood { get; set; }
        public void StrengthChangeFood(double value)
        {
            ChangeStrengthFood += value;
            StrengthFood += value;
        }
        /// <summary>
        /// 变化 食物
        /// </summary>
        public double ChangeStrengthFood { get; set; } = 0;
        /// <summary>
        /// 口渴度
        /// </summary>
        public double StrengthDrink
        {
            get => strengthDrink; set
            {
                value = Math.Min(100, value);
                if (value <= 0)
                {
                    Health += value;
                    strengthDrink = 0;
                }
                else
                    strengthDrink = value;
            }
        }

        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        protected double strengthDrink { get; set; }
        /// <summary>
        /// 待补充的口渴度,随着时间缓慢加给桌宠
        /// </summary>//让游戏更有游戏性
        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        public double StoreStrengthDrink { get; set; }
        /// <summary>
        /// 变化 口渴度
        /// </summary>
        public double ChangeStrengthDrink { get; set; } = 0;
        public void StrengthChangeDrink(double value)
        {
            ChangeStrengthDrink += value;
            StrengthDrink += value;
        }
        /// <summary>
        /// 心情
        /// </summary>
        public double Feeling
        {
            get => feeling; set
            {

                value = Math.Min(100, value);
                if (value <= 0)
                {
                    Health += value / 2;
                    Likability += value / 2;
                    feeling = 0;
                }
                else
                    feeling = value;
            }
        }

        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        protected double feeling { get; set; }       
        /// <summary>
        /// 变化 心情
        /// </summary>
        public double ChangeFeeling { get; set; } = 0;
        public void FeelingChange(double value)
        {
            ChangeFeeling += value;
            Feeling += value;
        }
        /// <summary>
        /// 健康(生病)(隐藏)
        /// </summary>
        public double Health { get => health; set => health = Math.Min(100, Math.Max(0, value)); }

        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        protected double health { get; set; }
        /// <summary>
        /// 好感度(隐藏)(累加值)
        /// </summary>
        public double Likability
        {
            get => likability; set
            {
                var max = LikabilityMax;
                value = Math.Max(0, value);
                if (value > max)
                {
                    likability = max;
                    Health += value - max;
                }
                else
                    likability = value;
            }
        }

        [Line(Type = LPSConvert.ConvertType.ToFloat)]
        protected double likability { get; set; }

        /// <summary>
        /// 清除变化
        /// </summary>
        public void CleanChange()
        {
            ChangeStrength /= 2;
            ChangeFeeling /= 2;
            ChangeStrengthDrink /= 2;
            ChangeStrengthFood /= 2;
        }
        /// <summary>
        /// 取回被储存的体力
        /// </summary>
        public void StoreTake()
        {
            const int t = 10;

            var s = StoreStrength / t;
            StoreStrength -= s;
            if (Math.Abs(StoreStrength) < 1)
                StoreStrength = 0;
            else
                StrengthChange(s);

            s = StoreStrengthDrink / t;
            StoreStrengthDrink -= s;
            if (Math.Abs(StoreStrengthDrink) < 1)
                StoreStrengthDrink = 0;
            else
                StrengthChangeDrink(s);

            s = StoreStrengthFood / t;
            StoreStrengthFood -= s;
            if (Math.Abs(StoreStrengthFood) < 1)
                StoreStrengthFood = 0;
            else
                StrengthChangeFood(s);
        }
        /// <summary>
        /// 吃食物
        /// </summary>
        /// <param name="food">食物类</param>
        public void EatFood(IFood food)
        {
            Exp += food.Exp;
            var tmp = food.Strength / 2;
            StrengthChange(tmp);
            StoreStrength += tmp;
            tmp = food.StrengthFood / 2;
            StrengthChangeFood(tmp);
            StoreStrengthFood += tmp;
            tmp = food.StrengthDrink / 2;
            StrengthChangeDrink(tmp);
            StoreStrengthDrink += tmp;
            FeelingChange(food.Feeling);
            Health += food.Health;
            Likability += food.Likability;
        }
        /// <summary>
        /// 宠物当前状态
        /// </summary>
        [Line(name: "mode")]
        public ModeType Mode { get; set; } = ModeType.Nomal;

        public double LikabilityMax => 90 + Level * 10;

        public double FeelingMax => 100;

        public double ExpBonus => 1;

        /// <summary>
        /// 计算宠物当前状态
        /// </summary>
        public ModeType CalMode()
        {
            int realhel = 60 - (Feeling >= 80 ? 12 : 0) - (Likability >= 80 ? 12 : (Likability >= 40 ? 6 : 0));
            //先从最次的开始
            if (Health <= realhel)
            {
                //可以确认从状态不佳和生病二选一
                if (Health <= realhel / 2)
                {//生病
                    return ModeType.Ill;
                }
                else
                {
                    return ModeType.PoorCondition;
                }
            }
            //然后判断是高兴还是普通
            double realfel = .90 - (Likability >= 80 ? .20 : (Likability >= 40 ? .10 : 0));
            double felps = Feeling / FeelingMax;
            if (felps >= realfel)
            {
                return ModeType.Happy;
            }
            else if (felps <= realfel / 2)
            {
                return ModeType.PoorCondition;
            }
            return ModeType.Nomal;
        }
        /// <summary>
        /// 新游戏
        /// </summary>
        public GameSave(string name)
        {
            Name = name;
            Money = 1000;
            Exp = 0;
            Strength = 100;
            StrengthFood = 100;
            StrengthDrink = 100;
            Feeling = 60;
            Health = 100;
            Likability = 0;
            Mode = CalMode();
        }
        /// <summary>
        /// 读档
        /// </summary>
        public GameSave()
        {
            //Money = line.GetFloat("money");
            //Name = line.Info;
            //Exp = line.GetInt("exp");
            //Strength = line.GetFloat("strength");
            //StrengthDrink = line.GetFloat("strengthdrink");
            //StrengthFood = line.GetFloat("strengthfood");
            //Feeling = line.GetFloat("feeling");
            //Health = line.GetFloat("health");
            //Likability = line.GetFloat("likability");
            //Mode = CalMode();
        }
        /// <summary>
        /// 读档
        /// </summary>
        public static GameSave Load(ILine data) => LPSConvert.DeserializeObject<GameSave>(data)!;
        /// <summary>
        /// 存档
        /// </summary>
        /// <returns>存档行</returns>
        public Line ToLine()
        {
            //Line save = new Line("vpet", Name);
            //save.SetFloat("money", Money);
            //save.SetInt("exp", Exp);
            //save.SetFloat("strength", Strength);
            //save.SetFloat("strengthdrink", StrengthDrink);
            //save.SetFloat("strengthfood", StrengthFood);
            //save.SetFloat("feeling", Feeling);
            //save.SetFloat("health", Health);
            //save.SetFloat("Likability", Likability);
            return new Line("vpet", ToSaveText());
        }
        // 注: Line 的这个构造只是把文本塞进 Info, 想拿到带子项的行必须用 LpsDocument 解析
        // (Load 里已经踩过这个坑), 所以 ToLine 仅供需要"整行"的调用方使用。

        /// <summary>
        /// 生成存档文本
        ///
        /// 不用 LPSConvert.SerializeObject: 带 [Line(ConvertType.ToFloat)] 的字段
        /// 在 LinePutScript 1.11.9 里被序列化成 **原值 × 10^9** 的整数
        /// (实测 体力100 → 100000000000, 心情99.64 → 99640491967),
        /// 读回来再被 setter 夹到上限, 表面上"看起来正常";
        /// 但 money / exp 没有上限, 于是每存一次就放大 10^9 倍,
        /// 最后双双顶到 long.MaxValue —— 表现为"存档里的钱和等级莫名其妙爆炸"。
        /// 这里改成手写, 与 Load 的逐字段读取一一对应, 数值原样落盘。
        /// </summary>
        private string ToSaveText()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F(double v) => double.IsNaN(v) || double.IsInfinity(v)
                ? "0"
                : v.ToString("0.####", ci);

            var sb = new System.Text.StringBuilder();
            sb.Append("vpet:|");
            sb.Append("name#").Append(Name).Append(":|");
            sb.Append("hostname#").Append(HostName).Append(":|");
            sb.Append("money#").Append(F(Money)).Append(":|");
            sb.Append("exp#").Append(F(Exp)).Append(":|");
            sb.Append("strength#").Append(F(Strength)).Append(":|");
            sb.Append("StoreStrength#").Append(F(StoreStrength)).Append(":|");
            sb.Append("strengthFood#").Append(F(StrengthFood)).Append(":|");
            sb.Append("StoreStrengthFood#").Append(F(StoreStrengthFood)).Append(":|");
            sb.Append("strengthDrink#").Append(F(StrengthDrink)).Append(":|");
            sb.Append("StoreStrengthDrink#").Append(F(StoreStrengthDrink)).Append(":|");
            sb.Append("feeling#").Append(F(Feeling)).Append(":|");
            sb.Append("health#").Append(F(Health)).Append(":|");
            sb.Append("likability#").Append(F(Likability)).Append(":|");
            sb.Append("mode#").Append(Mode).Append(":|");
            return sb.ToString();
        }

        /// <summary>
        /// 存档文件路径
        /// 统信UOS 上程序目录常为只读(/opt、/usr), 写不进去会导致每次启动都丢档,
        /// 因此统一放到当前用户目录 ~/.local/share/VPet/save.lps
        /// </summary>
        public static string SavePath
        {
            get
            {
                try
                {
                    var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (string.IsNullOrEmpty(baseDir))
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        baseDir = string.IsNullOrEmpty(home) ? Path.GetTempPath() : System.IO.Path.Combine(home, ".local", "share");
                    }
                    var dir = System.IO.Path.Combine(baseDir, "VPet");
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    return System.IO.Path.Combine(dir, "save.lps");
                }
                catch
                {
                    return System.IO.Path.Combine(Path.GetTempPath(), "vpet_save.lps");
                }
            }
        }

        /// <summary>
        /// 保存游戏数据
        /// </summary>
        public void Save()
        {
            try
            {
                var path = SavePath;
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, ToSaveText());
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 存档失败: " + e.Message);
            }
        }

        /// <summary>
        /// 加载游戏数据
        /// 注意: 必须先用 LpsDocument 把文本解析成带子项的行再取值。
        /// 之前写成 new Line("vpet", text) —— Line 只把整串当作 Info, 没有 Sub,
        /// LPSConvert 映射不到任何字段, 读档结果全是 0 → Health=0 → 直接进入生病姿态,
        /// 表现为"启动后宠物躺着不出来、说话动画全都找不到"
        /// </summary>
        public void Load()
        {
            try
            {
                var path = SavePath;
                if (!System.IO.File.Exists(path))
                    return;

                var text = System.IO.File.ReadAllText(path);
                var doc = new LpsDocument(text);
                var line = doc.FirstOrDefault();
                if (line == null)
                    return;
                if (Environment.GetEnvironmentVariable("VPET_DEBUG_SAVE") == "1")
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var s in line)
                        names.Add($"{s?.Name}={s?.Info}");
                    Console.WriteLine("[VPet][DBG] 子项: " + string.Join(" ; ", names));
                }

                var name = line.GetString("name");
                if (!string.IsNullOrWhiteSpace(name))
                    Name = name;
                HostName = SubStr(line, "hostname");
                Money = SubDouble(line, "money");
                Exp = SubDouble(line, "exp");
                Strength = SubDouble(line, "strength");
                StrengthFood = SubDouble(line, "strengthfood");
                StrengthDrink = SubDouble(line, "strengthdrink");
                Feeling = SubDouble(line, "feeling");
                Health = SubDouble(line, "health");
                Likability = SubDouble(line, "likability");
                Mode = CalMode();

                // 兜底: 旧版本存档里的 money/exp 会被序列化放大 10^9 倍(见 ToSaveText 注释),
                // 顶到 long.MaxValue 后等级会算成三亿级, 金钱显示成 9.2e18 —— 直接判为损坏并重置
                var fixedSave = false;
                if (!(Money >= 0 && Money < 1e13))
                {
                    Money = 1000;
                    fixedSave = true;
                }
                if (!(Exp >= 0 && Exp < 1e8))
                {
                    Exp = 0;
                    fixedSave = true;
                }
                // 好感度上限跟等级挂钩, 等级被重置后顺手把显然不合理的值也清掉
                if (Likability > LikabilityMax)
                {
                    Likability = 0;
                    fixedSave = true;
                }
                if (fixedSave)
                    Console.WriteLine("[VPet] 存档数值异常(旧版本写入放大), 已重置金钱/经验为初始值");

                Console.WriteLine($"[VPet] 读档成功: {Name} 金钱={Money:0.#} 等级={Level} 体力={Strength:0.#} 饱腹={StrengthFood:0.#} 口渴={StrengthDrink:0.#} 心情={Feeling:0.#} 健康={Health:0.#} 状态={Mode}");
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读档失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取子项字符串 (忽略大小写)
        /// 存档里是驼峰 strengthFood, 而查询用全小写, LinePutScript 的 Find 区分大小写
        /// </summary>
        private static string SubStr(ILine line, string name)
        {
            var sub = FindSub(line, name);
            return sub?.Info.ToString() ?? "";
        }

        /// <summary>
        /// 取子项数值 (忽略大小写)
        /// </summary>
        private static double SubDouble(ILine line, string name)
        {
            var s = SubStr(line, name);
            if (string.IsNullOrWhiteSpace(s))
                return 0;
            return double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static ISub? FindSub(ILine line, string name)
        {
            try
            {
                foreach (var sub in line)
                {
                    if (sub == null || string.IsNullOrEmpty(sub.Name))
                        continue;
                    if (string.Equals(sub.Name, name, StringComparison.OrdinalIgnoreCase))
                        return sub;
                }
            }
            catch
            {
                // 忽略
            }
            return null;
        }

        /// <summary>
        /// 获取或设置存档数据
        /// </summary>
        /// <param name="key">数据键</param>
        /// <returns>数据值</returns>
        public string this[string key]
        {
            get
            {
                switch (key.ToLower())
                {
                    case "name": return Name;
                    case "hostname": return HostName;
                    case "money": return Money.ToString();
                    case "exp": return Exp.ToString();
                    case "strength": return Strength.ToString();
                    case "strengthfood": return StrengthFood.ToString();
                    case "strengthdrink": return StrengthDrink.ToString();
                    case "feeling": return Feeling.ToString();
                    case "health": return Health.ToString();
                    case "likability": return Likability.ToString();
                    default: return "";
                }
            }
            set
            {
                switch (key.ToLower())
                {
                    case "name": Name = value; break;
                    case "hostname": HostName = value; break;
                    case "money": Money = double.TryParse(value, out var m) ? m : Money; break;
                    case "exp": Exp = double.TryParse(value, out var e) ? e : Exp; break;
                    case "strength": Strength = double.TryParse(value, out var s) ? s : Strength; break;
                    case "strengthfood": StrengthFood = double.TryParse(value, out var sf) ? sf : StrengthFood; break;
                    case "strengthdrink": StrengthDrink = double.TryParse(value, out var sd) ? sd : StrengthDrink; break;
                    case "feeling": Feeling = double.TryParse(value, out var f) ? f : Feeling; break;
                    case "health": Health = double.TryParse(value, out var h) ? h : Health; break;
                    case "likability": Likability = double.TryParse(value, out var l) ? l : Likability; break;
                }
            }
        }
    }
}