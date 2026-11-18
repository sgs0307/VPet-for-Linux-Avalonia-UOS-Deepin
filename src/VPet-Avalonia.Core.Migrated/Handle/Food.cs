using LinePutScript;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 食物/物品 (对应原版 VPet_Simulator.Windows.Interface.Food)
    /// 数据来自 mod/&lt;mod&gt;/food/*.lps 里的 food 行:
    /// food:|name#果汁:|type#Drink:|Exp#8:|Strength#10:|StrengthDrink#40:|price#10.5:|graph#drink:|desc#...:|
    /// </summary>
    public class Food : IFood
    {
        /// <summary>
        /// 食物类型
        /// </summary>
        public enum FoodType
        {
            /// <summary>食物(默认)</summary>
            Food,
            /// <summary>收藏</summary>
            Star,
            /// <summary>正餐</summary>
            Meal,
            /// <summary>零食</summary>
            Snack,
            /// <summary>饮料</summary>
            Drink,
            /// <summary>功能性</summary>
            Functional,
            /// <summary>药品</summary>
            Drug,
            /// <summary>礼品</summary>
            Gift,
        }

        /// <summary>名称</summary>
        public string Name { get; set; } = "";
        /// <summary>类型</summary>
        public FoodType Type { get; set; } = FoodType.Food;
        /// <summary>经验值</summary>
        public int Exp { get; set; }
        /// <summary>体力</summary>
        public double Strength { get; set; }
        /// <summary>饱腹度</summary>
        public double StrengthFood { get; set; }
        /// <summary>口渴度</summary>
        public double StrengthDrink { get; set; }
        /// <summary>心情</summary>
        public double Feeling { get; set; }
        /// <summary>健康</summary>
        public double Health { get; set; }
        /// <summary>好感度</summary>
        public double Likability { get; set; }
        /// <summary>价格</summary>
        public double Price { get; set; }
        /// <summary>食用时播放的动画名, 空则按类型推导</summary>
        public string Graph { get; set; } = "";
        /// <summary>描述</summary>
        public string Desc { get; set; } = "";

        /// <summary>
        /// 获取食用时显示的动画名
        /// </summary>
        public string GetGraph()
        {
            if (!string.IsNullOrEmpty(Graph))
                return Graph;
            switch (Type)
            {
                case FoodType.Drink:
                    return "drink";
                case FoodType.Gift:
                    return "gift";
                default:
                    return "eat";
            }
        }

        /// <summary>
        /// 类型的中文名, 用于界面展示
        /// </summary>
        public string TypeName => Type switch
        {
            FoodType.Drink => "饮料",
            FoodType.Meal => "正餐",
            FoodType.Snack => "零食",
            FoodType.Functional => "功能性",
            FoodType.Drug => "药品",
            FoodType.Gift => "礼品",
            _ => "食物",
        };

        /// <summary>
        /// 效果摘要, 例如 "+经验8 +口渴400 +饱腹4 -健康1"
        /// </summary>
        public string EffectText
        {
            get
            {
                var parts = new List<string>();
                void Add(string label, double v)
                {
                    if (Math.Abs(v) < 0.001)
                        return;
                    // 饱腹/口渴在数据里是百分比量级, 展示时按数据原值即可
                    parts.Add($"{(v > 0 ? "+" : "")}{v:0.#}{label}");
                }
                Add("经验", Exp);
                Add("饱腹", StrengthFood);
                Add("口渴", StrengthDrink);
                Add("体力", Strength);
                Add("心情", Feeling);
                Add("健康", Health);
                Add("好感", Likability);
                return parts.Count == 0 ? "无效果" : string.Join(" ", parts);
            }
        }

        /// <summary>
        /// 是否为药品
        /// </summary>
        public bool IsDrug => Type == FoodType.Drug;
    }

    /// <summary>
    /// 食物数据加载器
    /// 扫描 mod/&lt;mod&gt;/food/*.lps 中的所有 food 行
    /// </summary>
    public static class FoodLoader
    {
        /// <summary>
        /// 加载全部食物
        /// </summary>
        /// <param name="modDir">mod 根目录</param>
        /// <param name="log">日志回调</param>
        public static List<Food> LoadAll(DirectoryInfo? modDir, Action<string>? log = null)
        {
            var result = new List<Food>();
            if (modDir == null || !modDir.Exists)
                return result;

            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var mod in modDir.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                // 找 food 目录 (Linux 区分大小写, 做一次不敏感查找)
                var foodDir = Path.Combine(mod.FullName, "food");
                if (!Directory.Exists(foodDir))
                {
                    var found = Function.FindEntryCaseInsensitive(mod.FullName, "food");
                    if (found == null || !Directory.Exists(found))
                        continue;
                    foodDir = found;
                }

                foreach (var file in Directory.GetFiles(foodDir, "*.lps").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var doc = new LpsDocument(File.ReadAllText(file));
                        var lines = doc.FindAllLine("food");
                        foreach (var line in lines)
                        {
                            var food = Parse(line);
                            if (food == null || string.IsNullOrEmpty(food.Name))
                                continue;
                            if (!names.Add(food.Name))
                                continue;
                            result.Add(food);
                        }
                    }
                    catch (Exception e)
                    {
                        log?.Invoke($"读取食物数据失败 {Path.GetFileName(file)}: {e.Message}");
                    }
                }
            }

            // mod 里的食物名有一部分是英文 key(yearchicken 之类),
            // 中文名在 lang/zh-Hans/food_v2.lps 里, 这里做一次替换
            ApplyChineseNames(modDir, result);

            // 按 类型 -> 价格 排序, 界面上看起来更整齐
            result.Sort((a, b) =>
            {
                var t = a.Type.CompareTo(b.Type);
                return t != 0 ? t : a.Price.CompareTo(b.Price);
            });
            log?.Invoke($"食物数据加载完成: {result.Count} 种");
            return result;
        }

        /// <summary>
        /// 用 mod 自带的中文语言表替换英文食物名
        /// 文件格式为每行: 英文key#中文名:
        /// </summary>
        private static void ApplyChineseNames(DirectoryInfo? modDir, List<Food> foods)
        {
            if (modDir == null || foods.Count == 0)
                return;
            foreach (var mod in modDir.EnumerateDirectories())
            {
                try
                {
                    var langRoot = Path.Combine(mod.FullName, "lang");
                    if (!Directory.Exists(langRoot))
                        continue;
                    var zh = Directory.GetDirectories(langRoot)
                        .FirstOrDefault(d => Path.GetFileName(d).Equals("zh-Hans", StringComparison.OrdinalIgnoreCase));
                    if (zh == null)
                        continue;

                    // 食物名分散在 food_v2.lps / Limit.lps 等多个语言文件里, 全部合并
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.GetFiles(zh, "*.lps"))
                    {
                        try
                        {
                            var doc = new LpsDocument(File.ReadAllText(file));
                            foreach (var line in doc)
                            {
                                if (line == null || string.IsNullOrEmpty(line.Name))
                                    continue;
                                var translated = line.Info.ToString();
                                if (!string.IsNullOrWhiteSpace(translated))
                                    map[line.Name] = translated;
                            }
                        }
                        catch
                        {
                            // 单个语言文件损坏不影响其它
                        }
                    }
                    if (map.Count == 0)
                        continue;

                    foreach (var food in foods)
                    {
                        if (map.TryGetValue(food.Name, out var cn) && !string.IsNullOrWhiteSpace(cn) && cn != food.Name)
                            food.Name = cn;
                    }
                }
                catch
                {
                    // 语言表损坏不影响食物本身
                }
            }
        }

        /// <summary>
        /// 手动逐字段解析
        /// 不用 LPSConvert 的原因: 它对没有 [Line] 特性的属性不会映射,
        /// 移植过程中已经因此踩过 Move.graph 全空的坑, 这里直接取值最稳
        /// </summary>
        private static Food? Parse(ILine line)
        {
            try
            {
                var food = new Food
                {
                    Name = GetStr(line, "name"),
                    Type = ParseType(GetStr(line, "type")),
                    Exp = (int)GetDouble(line, "exp"),
                    Strength = GetDouble(line, "strength"),
                    StrengthFood = GetDouble(line, "strengthfood"),
                    StrengthDrink = GetDouble(line, "strengthdrink"),
                    Feeling = GetDouble(line, "feeling"),
                    Health = GetDouble(line, "health"),
                    Likability = GetDouble(line, "likability"),
                    Price = GetDouble(line, "price"),
                    Graph = GetStr(line, "graph"),
                    Desc = GetStr(line, "desc"),
                };
                return food;
            }
            catch
            {
                return null;
            }
        }

        private static Food.FoodType ParseType(string s)
        {
            if (Enum.TryParse<Food.FoodType>(s, true, out var t))
                return t;
            return Food.FoodType.Food;
        }

        private static string GetStr(ILine line, string name)
        {
            try
            {
                // 必须忽略大小写: food.lps 里数值字段写作 StrengthFood#100 / Exp#4 (首字母大写),
                // 而这里查的是全小写, LinePutScript 的 Find 区分大小写, 之前因此把所有
                // 食物效果都读成了 0(投喂后数值完全不变)
                var sub = FindSub(line, name);
                if (sub == null)
                    return "";
                var info = sub.Info.ToString();
                return info ?? "";
            }
            catch
            {
                return "";
            }
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

        private static double GetDouble(ILine line, string name)
        {
            try
            {
                var s = GetStr(line, name);
                if (string.IsNullOrWhiteSpace(s))
                    return 0;
                return double.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
