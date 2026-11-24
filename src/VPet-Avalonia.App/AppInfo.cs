using System;
using System.IO;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 版本与构建信息。
    ///
    /// 之前版本号只存在于发布仓库的 VERSION 文件和 CHANGELOG 里, 程序自己不知道,
    /// 用户报问题时得去翻文件。这里留一个常量, 设置面板的"关于"直接显示它。
    ///
    /// v1.5.0 修正: 这个常量在 v1.4.0 / v1.4.1 两次发版时都忘了跟着改, 于是 v1.4.1 的
    /// 「关于」里一直写着 1.3.1。现在改成**优先读 VERSION 文件**(发布包根目录、
    /// 程序目录、以及它们的上一级), 读不到才回退到常量 —— 发版脚本只改 VERSION 一处就够。
    /// </summary>
    public static class AppInfo
    {
        /// <summary>读不到 VERSION 文件时的兜底值</summary>
        private const string FallbackVersion = "1.5.0";

        public static readonly string Version = Resolve();

        /// <summary>一句话说明, 面板/关于里用</summary>
        public const string Tagline = "VPet 统信UOS / Linux 移植版 (Avalonia 11)";

        private static string Resolve()
        {
            try
            {
                // 发布包布局: <包根>/VERSION 与 <包根>/publish/程序.dll
                // 开发布局:   仓库根/VERSION 与 仓库根/publish/程序.dll
                var bases = new[]
                {
                    AppContext.BaseDirectory,
                    Path.Combine(AppContext.BaseDirectory, ".."),
                    Path.Combine(AppContext.BaseDirectory, "..", ".."),
                };
                foreach (var dir in bases)
                {
                    var file = Path.Combine(dir, "VERSION");
                    if (!File.Exists(file))
                        continue;
                    var text = File.ReadAllText(file).Trim();
                    if (text.Length > 0 && text.Length < 32)
                        return text;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取版本号失败: " + e.Message);
            }
            return FallbackVersion;
        }
    }
}
