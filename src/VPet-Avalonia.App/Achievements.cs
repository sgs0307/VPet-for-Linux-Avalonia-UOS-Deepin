using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 本地成就系统 (对应原版 Interface/Statistics.cs + Steam 成就)。
    ///
    /// 原版的成就走 SteamUserStats, 移植版没有 Steam, 就地做一份:
    /// 成就定义内置, 解锁状态落在 achievements.txt (和 stats.txt 一样与存档分开,
    /// 删档不清成就)。
    /// </summary>
    public static class Achievements
    {
        /// <summary>一条成就定义</summary>
        public sealed class Def
        {
            public string Id = "";
            public string Name = "";
            public string Desc = "";
            /// <summary>解锁目标值</summary>
            public double Goal;
            /// <summary>当前进度 (实时读取, 不落盘)</summary>
            public Func<CharacterStats, IGameSave?, double> Progress;
            /// <summary>进度展示单位</summary>
            public string Unit = UnitCount;
        }

        /// <summary>进度来源标签: Def.Unit 取值</summary>
        public const string UnitCount = "次";
        public const string UnitHour = "小时";
        public const string UnitLevel = "级";
        public const string UnitMoney = "$";

        /// <summary>全部成就 (顺序即面板展示顺序)</summary>
        public static readonly Def[] Defs =
        {
            new Def
            {
                Id = "touch_10", Name = "顺毛大师", Desc = "摸头 10 次", Unit = UnitCount,
                Goal = 10, Progress = (s, _) => s.TouchCount,
            },
            new Def
            {
                Id = "touch_100", Name = "头号粉丝", Desc = "摸头 100 次", Unit = UnitCount,
                Goal = 100, Progress = (s, _) => s.TouchCount,
            },
            new Def
            {
                Id = "pinch_10", Name = "捏脸初体验", Desc = "长按捏脸 10 次", Unit = UnitCount,
                Goal = 10, Progress = (s, _) => s.PinchCount,
            },
            new Def
            {
                Id = "feed_10", Name = "投喂好人", Desc = "投喂 10 次", Unit = UnitCount,
                Goal = 10, Progress = (s, _) => s.FeedCount,
            },
            new Def
            {
                Id = "play_1h", Name = "一小时之约", Desc = "累计陪伴 1 小时", Unit = UnitHour,
                Goal = 3600, Progress = (s, _) => s.PlaySeconds,
            },
            new Def
            {
                Id = "play_10h", Name = "长久陪伴", Desc = "累计陪伴 10 小时", Unit = UnitHour,
                Goal = 36000, Progress = (s, _) => s.PlaySeconds,
            },
            new Def
            {
                Id = "work_1h", Name = "一起努力", Desc = "累计干活/学习 1 小时", Unit = UnitHour,
                Goal = 3600, Progress = (s, _) => s.WorkSeconds,
            },
            new Def
            {
                Id = "buy_50", Name = "超市常客", Desc = "商店购买 50 次", Unit = UnitCount,
                Goal = 50, Progress = (s, _) => s.BuyCount,
            },
            new Def
            {
                Id = "workfinish_5", Name = "今日事今日毕", Desc = "完成 5 次工作/学习", Unit = UnitCount,
                Goal = 5, Progress = (s, _) => s.WorkFinishCount,
            },
            new Def
            {
                Id = "level_10", Name = "茁壮成长", Desc = "等级达到 10 级", Unit = UnitLevel,
                Goal = 10, Progress = (s, _) => s.MaxLevel,
            },
            new Def
            {
                Id = "money_1000", Name = "小有积蓄", Desc = "持有金钱达到 $1000", Unit = UnitMoney,
                Goal = 1000, Progress = (s, _) => s.MaxMoney,
            },
            new Def
            {
                Id = "night_owl", Name = "夜猫子", Desc = "累计睡觉 1 小时(它睡你也算)", Unit = UnitHour,
                Goal = 3600, Progress = (s, _) => s.SleepSeconds,
            },
        };

        /// <summary>已解锁的成就 Id 集合</summary>
        public static readonly HashSet<string> Unlocked = new(StringComparer.Ordinal);

        /// <summary>本次会话里"新"解锁的(避免重启后再次播报)</summary>
        public static readonly HashSet<string> NewlyUnlocked = new(StringComparer.Ordinal);

        public static string AchPath
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(GameSave.SavePath);
                    return string.IsNullOrEmpty(dir) ? "achievements.txt" : Path.Combine(dir, "achievements.txt");
                }
                catch
                {
                    return "achievements.txt";
                }
            }
        }

        public static void Load()
        {
            Unlocked.Clear();
            try
            {
                var path = AchPath;
                if (!File.Exists(path))
                    return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    Unlocked.Add(line);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取成就失败: " + e.Message);
            }
        }

        public static void Save()
        {
            try
            {
                var lines = new List<string> { "# VPet 成就解锁记录" };
                lines.AddRange(Unlocked.OrderBy(x => x, StringComparer.Ordinal));
                File.WriteAllLines(AchPath, lines);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 保存成就失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查所有未解锁成就, 返回这一轮新解锁的定义列表 (可能为空)。
        /// 解锁状态会立即写盘。
        /// </summary>
        public static List<Def> CheckAll(CharacterStats stats, IGameSave? save)
        {
            var fresh = new List<Def>();
            foreach (var def in Defs)
            {
                if (Unlocked.Contains(def.Id))
                    continue;
                double cur;
                try { cur = def.Progress(stats, save); }
                catch { continue; }
                if (cur >= def.Goal)
                {
                    Unlocked.Add(def.Id);
                    NewlyUnlocked.Add(def.Id);
                    fresh.Add(def);
                }
            }
            if (fresh.Count > 0)
                Save();
            return fresh;
        }

        /// <summary>进度文本: "3/10 次"</summary>
        public static string ProgressText(Def def, CharacterStats stats, IGameSave? save)
        {
            double cur;
            try { cur = def.Progress(stats, save); }
            catch { cur = 0; }
            var shown = Math.Min(cur, def.Goal);
            var unit = def.Unit;
            if (unit == UnitMoney)
                return $"{shown:0.#}/{def.Goal:0.#} $";
            if (unit == UnitHour)
                return $"{shown / 3600.0:0.#}/{def.Goal / 3600.0:0.#} 小时";
            if (unit == UnitLevel)
                return $"{shown:0}/{def.Goal:0} 级";
            return $"{shown:0}/{def.Goal:0} {unit}";
        }

        /// <summary>解锁时间记录</summary>
        public static Dictionary<string, DateTime> LoadUnlockTimes()
        {
            var map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            try
            {
                var path = AchPath + ".time";
                if (!File.Exists(path))
                    return map;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var idx = raw.IndexOf('=');
                    if (idx <= 0) continue;
                    if (DateTime.TryParseExact(raw[(idx + 1)..].Trim(), "yyyy-MM-dd HH:mm",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                        map[raw[..idx].Trim()] = t;
                }
            }
            catch { }
            return map;
        }

        public static void AppendUnlockTime(string id)
        {
            try
            {
                File.AppendAllText(AchPath + ".time",
                    id + "=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "\n");
            }
            catch { }
        }
    }
}
