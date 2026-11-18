using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 陪伴统计: 角色面板里"我们一起做过什么"那一栏的数据。
    ///
    /// 原版对应 Interface/Statistics.cs（那一整套含成就解锁，工作量很大）。
    /// 这里只记最直观的几项，落盘在 ~/.local/share/VPet/stats.txt，
    /// 和存档分开，删档不会把这些回忆一起清掉。
    /// </summary>
    public sealed class CharacterStats
    {
        /// <summary>摸头 + 摸身体次数</summary>
        public long TouchCount;

        /// <summary>投喂次数</summary>
        public long FeedCount;

        /// <summary>捏脸次数</summary>
        public long PinchCount;

        /// <summary>商店购买次数</summary>
        public long BuyCount;

        /// <summary>累计睡觉时长(秒)</summary>
        public long SleepSeconds;

        /// <summary>工作/学习完成次数</summary>
        public long WorkFinishCount;

        /// <summary>历史最高等级</summary>
        public int MaxLevel;

        /// <summary>历史最高金钱</summary>
        public double MaxMoney;

        /// <summary>累计陪伴时长(秒)</summary>
        public long PlaySeconds;

        /// <summary>累计工作/学习时长(秒)</summary>
        public long WorkSeconds;

        /// <summary>吃得最多的一样东西(名字), 空 = 还没吃过</summary>
        public string TopFood = "";

        /// <summary>它被吃掉的次数</summary>
        public long TopFoodCount;

        /// <summary>第一次一起的日子</summary>
        public DateTime FirstSeen = DateTime.Now;

        /// <summary>是否需要在下次保存时写盘</summary>
        private bool _dirty;

        public static string StatsPath
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(GameSave.SavePath);
                    return string.IsNullOrEmpty(dir) ? "stats.txt" : Path.Combine(dir, "stats.txt");
                }
                catch
                {
                    return "stats.txt";
                }
            }
        }

        public static CharacterStats Load()
        {
            var s = new CharacterStats();
            try
            {
                var path = StatsPath;
                if (!File.Exists(path))
                {
                    s._dirty = true;   // 首次运行, 让退出时写一份
                    return s;
                }

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    var idx = line.IndexOf('=');
                    if (idx <= 0)
                        continue;
                    var key = line.Substring(0, idx).Trim().ToLowerInvariant();
                    var val = line.Substring(idx + 1).Trim();
                    switch (key)
                    {
                        case "touch": long.TryParse(val, out s.TouchCount); break;
                        case "feed": long.TryParse(val, out s.FeedCount); break;
                        case "pinch": long.TryParse(val, out s.PinchCount); break;
                        case "buy": long.TryParse(val, out s.BuyCount); break;
                        case "sleepseconds": long.TryParse(val, out s.SleepSeconds); break;
                        case "workfinish": long.TryParse(val, out s.WorkFinishCount); break;
                        case "maxlevel": int.TryParse(val, out s.MaxLevel); break;
                        case "maxmoney": double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out s.MaxMoney); break;
                        case "playseconds": long.TryParse(val, out s.PlaySeconds); break;
                        case "workseconds": long.TryParse(val, out s.WorkSeconds); break;
                        case "topfood": s.TopFood = Unescape(val); break;
                        case "topfoodcount": long.TryParse(val, out s.TopFoodCount); break;
                        case "firstseen":
                            if (DateTime.TryParseExact(val, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var d))
                                s.FirstSeen = d;
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取统计失败: " + e.Message);
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# VPet 陪伴统计",
                    "touch=" + TouchCount,
                    "feed=" + FeedCount,
                    "pinch=" + PinchCount,
                    "buy=" + BuyCount,
                    "sleepseconds=" + SleepSeconds,
                    "workfinish=" + WorkFinishCount,
                    "maxlevel=" + MaxLevel,
                    "maxmoney=" + MaxMoney.ToString("0.##", CultureInfo.InvariantCulture),
                    "playseconds=" + PlaySeconds,
                    "workseconds=" + WorkSeconds,
                    "topfood=" + Escape(TopFood),
                    "topfoodcount=" + TopFoodCount,
                    "firstseen=" + FirstSeen.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                };
                File.WriteAllLines(StatsPath, lines);
                _dirty = false;
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 保存统计失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清零所有计数(设置面板"清除陪伴统计")。不动 FirstSeen 的话
        /// "相伴天数"会变成从今天重新算 —— 用户要的是"清掉记忆"还是"重新认识",
        /// 这里选前者: 次数/时长清零, 但初次见面的日子保留。
        /// </summary>
        public void Clear()
        {
            TouchCount = 0;
            FeedCount = 0;
            PinchCount = 0;
            BuyCount = 0;
            SleepSeconds = 0;
            WorkFinishCount = 0;
            PlaySeconds = 0;
            WorkSeconds = 0;
            TopFood = "";
            TopFoodCount = 0;
            _dirty = true;
        }

        /// <summary>标记有变化, 由自动保存/退出时统一写盘</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>只有变化过才写, 避免每次定时器都动磁盘</summary>
        public void SaveIfDirty()
        {
            if (_dirty)
                Save();
        }

        /// <summary>累计陪伴时长 +N 秒</summary>
        public void AddPlaySeconds(long sec)
        {
            if (sec <= 0) return;
            PlaySeconds += sec;
            _dirty = true;
        }

        public void AddWorkSeconds(long sec)
        {
            if (sec <= 0) return;
            WorkSeconds += sec;
            _dirty = true;
        }

        public void CountTouch()
        {
            TouchCount++;
            _dirty = true;
        }

        public void CountFeed()
        {
            CountFeed(null);
        }

        /// <summary>
        /// 记一次投喂, 顺便维护"最常吃"。
        ///
        /// 没有给 122 种食物各记一个计数器(那要给 stats.txt 加 122 行, 还随 MOD 变),
        /// 这里用流式求众数的 Misra-Gries 单计数器: 和当前 Top 同名就 +1,
        /// 不同名就 -1, 减到 0 才换人。数据倾斜时(总吃那几样)结果就是真众数,
        /// 而且只占两行文件。
        /// </summary>
        public void CountFeed(string? foodName)
        {
            FeedCount++;
            if (!string.IsNullOrEmpty(foodName))
            {
                if (string.Equals(foodName, TopFood, StringComparison.Ordinal))
                {
                    TopFoodCount++;
                }
                else if (TopFoodCount > 1)
                {
                    TopFoodCount--;
                }
                else
                {
                    TopFood = foodName!;
                    TopFoodCount = 1;
                }
            }
            _dirty = true;
        }

        /// <summary>互动总次数(摸头+捏脸+投喂+购买), 年报用</summary>
        public long InteractionTotal => TouchCount + PinchCount + FeedCount + BuyCount;

        /// <summary>
        /// key=value 文本里值要单行: 食物名可能带 '=' 或空格, 做最小转义
        /// </summary>
        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Replace("\\", "\\\\").Replace("=", "\\=").Replace("\n", "\\n").Replace("\r", "");
        }

        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    var n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n);
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        public void CountPinch()
        {
            PinchCount++;
            _dirty = true;
        }

        public void CountBuy()
        {
            BuyCount++;
            _dirty = true;
        }

        public void CountWorkFinish()
        {
            WorkFinishCount++;
            _dirty = true;
        }

        public void AddSleepSeconds(long sec)
        {
            if (sec <= 0) return;
            SleepSeconds += sec;
            _dirty = true;
        }

        /// <summary>记录等级/金钱峰值(成就用)</summary>
        public void ObserveSave(IGameSave? save)
        {
            if (save == null) return;
            if (save.Level > MaxLevel) { MaxLevel = save.Level; _dirty = true; }
            if (save.Money > MaxMoney) { MaxMoney = save.Money; _dirty = true; }
        }

        /// <summary>把秒数说成人话: 1 小时 23 分</summary>
        public static string HumanDuration(long seconds)
        {
            // 0 秒写成"刚刚开始"更像人话(面板上"已经陪你 0 秒"很怪)
            if (seconds <= 0)
                return "刚刚开始";
            if (seconds < 60)
                return $"{seconds} 秒";
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours < 1)
                return $"{(int)ts.TotalMinutes} 分钟";
            if (ts.TotalHours < 24)
                return $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分钟";
            return $"{(int)ts.TotalDays} 天 {ts.Hours} 小时";
        }
    }
}
