using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 说话文本池。
    ///
    /// 原版把台词放在创意工坊的聊天 MOD 里（mod 的 text 行是界面文案，不是台词），
    /// 移植版之前只有一句硬编码的"你好呀~"，说来说去都一样。
    ///
    /// 这里内置一批按场景分组的台词，并支持用户自定义：
    /// 把台词写进 ~/.local/share/VPet/speech.txt（一行一句，# 开头是注释），
    /// 会追加到"闲聊"池里。这样不装 MOD 也有内容，想加词也不用改程序。
    /// </summary>
    public static class SpeechPool
    {
        public enum Scene
        {
            Greeting,   // 启动见面
            Idle,       // 闲聊
            TouchHead,  // 摸头
            TouchBody,  // 摸身体
            Pinch,      // 捏脸
            Feed,       // 投喂
            Work,       // 开始工作/学习
            WorkDone,   // 工作结束
            Sleep,      // 去睡觉
            Wake,       // 醒来
            Ill,        // 生病
            Hungry,     // 饿了
            Thirsty,    // 渴了
            Tired,      // 体力低
            LevelUp,    // 升级
        }

        private static readonly Dictionary<Scene, string[]> Corpus = new()
        {
            [Scene.Greeting] = new[]
            {
                "来陪你啦~",
                "今天也要一起加油哦",
                "我在这儿呢，随时叫我",
                "又见面啦！",
                "桌面上有我陪着，不会孤单的",
            },
            [Scene.Idle] = new[]
            {
                "你在忙什么呀？",
                "要不要休息一下眼睛？",
                "窗外好像有点亮……",
                "我刚刚打了个小小的哈欠",
                "有点想吃甜的东西",
                "今天的你也很努力呢",
                "嗯……在想事情",
                "桌子好大，我好像很小一只",
                "要不要摸摸我的头？",
                "时间过得好快呀",
                "喝口水吧，别忘了",
                "我在这儿看着你干活",
            },
            [Scene.TouchHead] = new[]
            {
                "嘿嘿……好舒服",
                "再摸一下嘛",
                "头发要乱掉了啦",
                "唔……喜欢这样",
                "这样摸我会变笨的（才怪）",
            },
            [Scene.TouchBody] = new[]
            {
                "呀！痒痒的",
                "别、别挠那里……",
                "抱抱也是可以的",
                "这样靠着好安心",
            },
            [Scene.Pinch] = new[]
            {
                "呀、脸被捏住了！",
                "唔……脸要捏软了啦",
                "呜，我脸又不是面团",
                "再捏要生气了哦(鼓起脸)",
                "轻轻的啦……",
            },
            [Scene.Feed] = new[]
            {
                "好好吃！",
                "谢谢～我开动了",
                "这个我最喜欢了",
                "吃饱和心情好是两件事哦",
                "唔…有点撑",
            },
            [Scene.Work] = new[]
            {
                "那我开始咯",
                "交给我吧！",
                "认真模式，启动",
                "这次要赚多一点",
            },
            [Scene.WorkDone] = new[]
            {
                "做完啦——",
                "累是累，但值得",
                "今天的收获还不错",
                "下次也要一起努力",
            },
            [Scene.Sleep] = new[]
            {
                "那我先睡一会儿……",
                "晚安，别熬太晚",
                "呼……好困",
            },
            [Scene.Wake] = new[]
            {
                "唔……我醒了",
                "睡得好饱",
                "早上好呀",
            },
            [Scene.Ill] = new[]
            {
                "头有点晕……",
                "好像感冒了，有药吗",
                "咳咳……不太舒服",
            },
            [Scene.Hungry] = new[]
            {
                "肚子在叫了……",
                "有点饿了，能吃点东西吗",
                "饱腹度掉下去了呢",
            },
            [Scene.Thirsty] = new[]
            {
                "好渴……想喝水",
                "嘴里干干的",
            },
            [Scene.Tired] = new[]
            {
                "有点累了，想歇一会儿",
                "力气快用完啦",
                "站不太住了……",
            },
            [Scene.LevelUp] = new[]
            {
                "我升级啦！",
                "又变强了一点点",
            },
        };

        /// <summary>用户自定义台词 (speech.txt), 追加进闲聊池</summary>
        private static readonly List<string> UserIdle = new();

        private static readonly Random Rnd = new();

        /// <summary>上一次说过的句子, 避免连着重复</summary>
        private static string? _last;

        /// <summary>
        /// 用户自定义台词文件: 与存档同目录
        /// </summary>
        public static string UserSpeechPath
        {
            get
            {
                try
                {
                    var save = GameSave.SavePath;
                    var dir = Path.GetDirectoryName(save);
                    return string.IsNullOrEmpty(dir) ? "speech.txt" : Path.Combine(dir, "speech.txt");
                }
                catch
                {
                    return "speech.txt";
                }
            }
        }

        /// <summary>
        /// 载入用户台词; 文件不存在就写一份空模板, 方便用户照着改
        /// </summary>
        public static void LoadUserSpeech()
        {
            UserIdle.Clear();
            try
            {
                var path = UserSpeechPath;
                if (!File.Exists(path))
                {
                    File.WriteAllLines(path, new[]
                    {
                        "# 一行一句, # 开头是注释; 改完重启桌宠生效",
                        "# 这些台词会混进宠物的日常闲聊里",
                    });
                    return;
                }

                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    UserIdle.Add(line);
                }
                if (UserIdle.Count > 0)
                    Console.WriteLine($"[VPet] 载入自定义台词 {UserIdle.Count} 句");
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取自定义台词失败: " + e.Message);
            }
        }

        /// <summary>
        /// 从指定场景随机取一句
        /// </summary>
        public static string Pick(Scene scene)
        {
            var list = Corpus.TryGetValue(scene, out var arr) ? arr : Array.Empty<string>();
            if (scene == Scene.Idle && UserIdle.Count > 0)
                list = list.Concat(UserIdle).ToArray();
            if (list.Length == 0)
                return "";

            // 最多试 6 次挑一句和上次不一样的
            var text = list[0];
            for (int i = 0; i < 6; i++)
            {
                var t = list[Rnd.Next(list.Length)];
                if (t != _last)
                {
                    text = t;
                    break;
                }
                text = t;
            }
            _last = text;
            return text;
        }

        /// <summary>
        /// 按当前状态挑一句: 生病 &gt; 饿 &gt; 渴 &gt; 累 &gt; 其它闲聊。
        /// 这样宠物的自言自语能反映它自己的处境, 而不是随机复读。
        /// </summary>
        public static string PickIdle(VPetAvalonia.Core.IGameSave? save)
        {
            if (save == null)
                return Pick(Scene.Idle);

            if (save.Mode == ModeType.Ill && Rnd.Next(3) == 0)
                return Pick(Scene.Ill);
            if (save.StrengthFood < 30 && Rnd.Next(2) == 0)
                return Pick(Scene.Hungry);
            if (save.StrengthDrink < 30 && Rnd.Next(2) == 0)
                return Pick(Scene.Thirsty);
            if (save.Strength < 25 && Rnd.Next(2) == 0)
                return Pick(Scene.Tired);

            // 按时间段给一点氛围
            var hour = DateTime.Now.Hour;
            if (hour >= 23 || hour < 6)
            {
                if (Rnd.Next(3) == 0)
                    return Rnd.Next(2) == 0 ? "这个点了还没睡吗？" : "夜深了，早点休息吧";
            }
            else if (hour < 9 && Rnd.Next(4) == 0)
            {
                return "早上好，今天也一起吧";
            }

            return Pick(Scene.Idle);
        }
    }
}
