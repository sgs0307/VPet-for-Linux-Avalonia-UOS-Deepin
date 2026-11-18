namespace VPetAvalonia.App
{
    /// <summary>
    /// 版本与构建信息。
    ///
    /// 之前版本号只存在于发布仓库的 VERSION 文件和 CHANGELOG 里, 程序自己不知道,
    /// 用户报问题时得去翻文件。这里留一个常量, 设置面板的"关于"直接显示它。
    /// 发版时和 VERSION 一起改。
    /// </summary>
    public static class AppInfo
    {
        public const string Version = "1.3.1";

        /// <summary>一句话说明, 面板/关于里用</summary>
        public const string Tagline = "VPet 统信UOS / Linux 移植版 (Avalonia 11)";
    }
}
