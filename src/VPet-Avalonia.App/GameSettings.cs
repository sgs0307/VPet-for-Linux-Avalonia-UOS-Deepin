using System;
using System.Collections.Generic;
using System.IO;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 运行设置: 对应原版 winGameSetting 里最常用的几项。
    ///
    /// 之前这些值全部硬编码 —— 数据计算开关写死在 IController.EnableFunction 的默认值,
    /// 计算间隔写死在 Main.EventTimer 的 15000ms, 自动保存写死 5 分钟, 用户改不了。
    ///
    /// 存储用最简单的 key=value 文本(~/.local/share/VPet/settings.txt),
    /// 和存档放在一起; 手改坏了也能自愈(读不出来的键保留默认值)。
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>启用数据计算 (关掉就是挂机模式, 数值不再变化)</summary>
        public bool EnableFunction = true;

        /// <summary>逻辑计算间隔(秒): 原版 SetLogicInterval, 决定 EventTimer 周期</summary>
        public int LogicIntervalSec = 15;

        /// <summary>互动周期: 越小宠物越爱自己动</summary>
        public int InteractionCycle = 30;

        /// <summary>自动保存间隔(分钟), 0 = 关闭</summary>
        public int AutoSaveMinutes = 5;

        /// <summary>允许宠物主动说话</summary>
        public bool SayEnabled = true;

        /// <summary>说话频率: 每隔几个计算周期尝试说一句(1 = 每周期都试)</summary>
        public int SayPeriod = 4;

        /// <summary>开机自启 (写 ~/.config/autostart/vpet.desktop)</summary>
        public bool AutoStart = false;

        /// <summary>气泡停留时长(秒): 对应 MessageBar.AutoHideDelay</summary>
        public int BubbleSeconds = 4;

        /// <summary>允许宠物自己走动: 关掉后只在原地发呆, 不来回爬</summary>
        public bool AllowRandomMove = true;

        // ---- 取值范围, 面板上的 + / - 按钮用它夹紧 ----
        public const int MinInterval = 5;
        public const int MaxInterval = 60;
        public const int MinCycle = 5;
        public const int MaxCycle = 120;
        public const int MaxAutoSave = 30;
        public const int MinSayPeriod = 1;
        public const int MaxSayPeriod = 20;
        public const int MinBubble = 1;
        public const int MaxBubble = 15;

        /// <summary>
        /// 设置文件路径: 与 save.lps 同目录
        /// </summary>
        public static string SettingsPath
        {
            get
            {
                try
                {
                    var save = GameSave.SavePath;
                    var dir = Path.GetDirectoryName(save);
                    if (string.IsNullOrEmpty(dir))
                        return "settings.txt";
                    return Path.Combine(dir, "settings.txt");
                }
                catch
                {
                    return "settings.txt";
                }
            }
        }

        /// <summary>
        /// 读取设置; 文件不存在或读失败时返回默认值
        /// </summary>
        public static GameSettings Load()
        {
            var s = new GameSettings();
            try
            {
                var path = SettingsPath;
                if (!File.Exists(path))
                    return s;

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
                        case "enablefunction": s.EnableFunction = ParseBool(val, s.EnableFunction); break;
                        case "logicinterval": s.LogicIntervalSec = ParseInt(val, s.LogicIntervalSec, MinInterval, MaxInterval); break;
                        case "interactioncycle": s.InteractionCycle = ParseInt(val, s.InteractionCycle, MinCycle, MaxCycle); break;
                        case "autosaveminutes": s.AutoSaveMinutes = ParseInt(val, s.AutoSaveMinutes, 0, MaxAutoSave); break;
                        case "sayenabled": s.SayEnabled = ParseBool(val, s.SayEnabled); break;
                        case "sayperiod": s.SayPeriod = ParseInt(val, s.SayPeriod, MinSayPeriod, MaxSayPeriod); break;
                        case "autostart": s.AutoStart = ParseBool(val, s.AutoStart); break;
                        case "bubbleseconds": s.BubbleSeconds = ParseInt(val, s.BubbleSeconds, MinBubble, MaxBubble); break;
                        case "allowrandommove": s.AllowRandomMove = ParseBool(val, s.AllowRandomMove); break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取设置失败, 使用默认值: " + e.Message);
            }
            return s;
        }

        /// <summary>
        /// 写入设置
        /// </summary>
        public void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# VPet 设置 (改完重启生效的项: 计算间隔 / 互动周期)",
                    "enablefunction=" + (EnableFunction ? "1" : "0"),
                    "logicinterval=" + LogicIntervalSec,
                    "interactioncycle=" + InteractionCycle,
                    "autosaveminutes=" + AutoSaveMinutes,
                    "sayenabled=" + (SayEnabled ? "1" : "0"),
                    "sayperiod=" + SayPeriod,
                    "autostart=" + (AutoStart ? "1" : "0"),
                    "bubbleseconds=" + BubbleSeconds,
                    "allowrandommove=" + (AllowRandomMove ? "1" : "0"),
                };
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 保存设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 按当前 AutoStart 写入/删除自启动项
        /// </summary>
        public void ApplyAutoStart()
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                    return;
                var dir = Path.Combine(home, ".config", "autostart");
                var file = Path.Combine(dir, "vpet.desktop");

                if (!AutoStart)
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    return;
                }

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // 优先用安装脚本生成的 vpet.sh (它会带好 DOTNET_ROOT 等环境)
                var baseDir = AppContext.BaseDirectory;
                var launcher = Path.Combine(baseDir, "vpet.sh");
                var exec = File.Exists(launcher)
                    ? launcher
                    : Path.Combine(baseDir, "VPet-Avalonia.App");

                File.WriteAllText(file,
                    "[Desktop Entry]\n" +
                    "Type=Application\n" +
                    "Name=VPet 桌宠\n" +
                    "Comment=虚拟桌宠\n" +
                    "Exec=" + exec + "\n" +
                    "Terminal=false\n" +
                    "X-GNOME-Autostart-enabled=true\n");
                Console.WriteLine("[VPet] 已写入自启动项: " + file);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 设置自启动失败: " + e.Message);
            }
        }

        private static bool ParseBool(string v, bool fallback)
        {
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            return fallback;
        }

        private static int ParseInt(string v, int fallback, int min, int max)
        {
            if (!int.TryParse(v, out var n))
                return fallback;
            return Math.Max(min, Math.Min(max, n));
        }
    }
}
