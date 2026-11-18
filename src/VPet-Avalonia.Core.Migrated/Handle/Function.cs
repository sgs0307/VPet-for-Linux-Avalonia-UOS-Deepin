using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace VPetAvalonia.Core
{
    public static partial class Function
    {
        #region Linux/统信UOS 文件系统适配

        /// <summary>
        /// 当前是否运行在 Unix(统信UOS/Linux/macOS) 上
        /// </summary>
        public static readonly bool IsUnix =
            Environment.OSVersion.Platform == PlatformID.Unix ||
            Environment.OSVersion.Platform == PlatformID.MacOSX;

        /// <summary>
        /// 规范化资源路径: MOD 里的 info.lps 多为 Windows 下编写, 可能使用 '\' 作为分隔符
        /// Linux 下 '\' 是合法文件名字符, 必须转换为 '/', 否则资源加载全部失败
        /// </summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            return path.Replace('\\', Path.DirectorySeparatorChar)
                       .Replace('/', Path.DirectorySeparatorChar)
                       .Trim();
        }

        /// <summary>
        /// 组合路径(自动规范化分隔符)
        /// </summary>
        public static string CombinePath(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return "";
            var list = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                            .Select(NormalizePath)
                            .Where(p => p.Length > 0)
                            .ToList();
            if (list.Count == 0)
                return "";
            return Path.Combine(list.ToArray());
        }

        /// <summary>
        /// 大小写不敏感地查找文件/目录
        /// Windows 文件系统不区分大小写, Linux(ext4/xfs) 区分,
        /// MOD 资源里 "Image.PNG" 与代码里的 "*.png" 在 UOS 上会匹配失败
        /// name 支持多级相对路径(如 "Happy\back_lay"), 逐段大小写不敏感解析
        /// 注意: 绝不递归搜索子目录 —— 递归会把别的目录的 info.lps 错配到当前目录
        /// </summary>
        public static string? FindEntryCaseInsensitive(string directory, string name)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(name))
                    return null;

                // 先尝试精确匹配, 绝大多数情况下直接命中, 避免目录枚举
                var direct = Path.Combine(directory, NormalizePath(name));
                if (File.Exists(direct) || Directory.Exists(direct))
                    return direct;

                // 逐段(目录层级)大小写不敏感解析
                var segments = NormalizePath(name)
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                    return null;

                var current = directory;
                foreach (var segment in segments)
                {
                    if (!Directory.Exists(current))
                        return null;
                    var target = segment.ToLowerInvariant();
                    string? hit = null;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (Path.GetFileName(entry).ToLowerInvariant() == target)
                        {
                            hit = entry;
                            break;
                        }
                    }
                    if (hit == null)
                        return null;
                    current = hit;
                }
                return File.Exists(current) || Directory.Exists(current) ? current : null;
            }
            catch
            {
                // 忽略 IO 异常
            }
            return null;
        }

        /// <summary>
        /// 大小写不敏感地获取指定扩展名的文件
        /// </summary>
        public static FileInfo[] GetFilesByExtension(DirectoryInfo dir, string extension)
        {
            var ext = extension.StartsWith(".") ? extension : "." + extension;
            ext = ext.ToLowerInvariant();
            try
            {
                return dir.GetFiles()
                          .Where(f => f.Extension.ToLowerInvariant() == ext)
                          .ToArray();
            }
            catch
            {
                return Array.Empty<FileInfo>();
            }
        }

        /// <summary>
        /// 自然排序比较: 保证 1,2,3...10,11 而不是 1,10,11,2,3
        /// 动画帧文件名形如 xxx_1.png xxx_10.png, 字典序会导致播放顺序完全错乱
        /// </summary>
        public static int NaturalCompare(string? a, string? b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    long na = 0, nb = 0;
                    while (ia < a.Length && char.IsDigit(a[ia]) && na < long.MaxValue / 16)
                        na = na * 10 + (a[ia++] - '0');
                    while (ib < b.Length && char.IsDigit(b[ib]) && nb < long.MaxValue / 16)
                        nb = nb * 10 + (b[ib++] - '0');
                    if (na != nb)
                        return na < nb ? -1 : 1;
                }
                else
                {
                    if (a[ia] != b[ib])
                        return a[ia] < b[ib] ? -1 : 1;
                    ia++;
                    ib++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }

        #endregion

        /// <summary>
        /// HEX值转颜色 (返回ARGB整数)
        /// </summary>
        /// <param name="HEX">HEX值</param>
        /// <returns>颜色ARGB值</returns>
        public static uint HEXToColor(string HEX)
        {
            string h = HEX.TrimStart('#');
            if (h.Length == 6)
                h = "FF" + h;
            return Convert.ToUInt32(h, 16);
        }
        
        /// <summary>
        /// 颜色转HEX值
        /// </summary>
        /// <param name="color">颜色ARGB值</param>
        /// <returns>HEX值</returns>
        public static string ColorToHEX(uint color)
        {
            byte a = (byte)((color >> 24) & 0xFF);
            byte r = (byte)((color >> 16) & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)(color & 0xFF);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }
        
        public static Random Rnd = new Random();
        
        /// <summary>
        /// 获取资源笔刷 (Avalonia版本)
        /// </summary>
        /// <param name="name">笔刷名称</param>
        /// <returns>颜色ARGB值</returns>
        public static uint ResourcesBrush(BrushType name)
        {
            // 简化版：返回默认颜色
            return 0xFF81D4FA;
        }
        
        public enum BrushType
        {
            Primary,
            PrimaryTrans,
            PrimaryTrans4,
            PrimaryTransA,
            PrimaryTransE,
            PrimaryLight,
            PrimaryLighter,
            PrimaryDark,
            PrimaryDarker,
            PrimaryText,
            Secondary,
            SecondaryTrans,
            SecondaryTrans4,
            SecondaryTransA,
            SecondaryTransE,
            SecondaryLight,
            SecondaryLighter,
            SecondaryDark,
            SecondaryDarker,
            SecondaryText,
            DARKPrimary,
            DARKPrimaryTrans,
            DARKPrimaryTrans4,
            DARKPrimaryTransA,
            DARKPrimaryTransE,
            DARKPrimaryLight,
            DARKPrimaryLighter,
            DARKPrimaryDark,
            DARKPrimaryDarker,
            DARKPrimaryText,
        }
        
        public class LPSConvertToLower : LinePutScript.Converter.LPSConvert.ConvertFunction
        {
            public override string Convert(dynamic value) => value;
            public override dynamic ConvertBack(string info) => info.ToLowerInvariant();
        }
        
        /// <summary>
        /// 获取内存使用情况(MB)
        /// </summary>
        public static double MemoryUsage()
        {
            return Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
        }
        
        public static double MemoryAvailable()
        {
            try
            {
                var info = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0 / 3 * 2;
                return info;
            }
            catch
            {
                return 4000;
            }
        }
        
        /// <summary>
        /// 用于区分句子数量的标点符号
        /// </summary>
        public static List<char> com { get; } = new List<char> { '，', '。', '！', '？', '；', '：', '\n', '.', ',', '!', '?', ';', ':' };
        
        /// <summary>
        /// 计算说话内容的句子数量
        /// </summary>
        /// <param name="text">句子</param>
        public static int ComCheck(string text)
        {
            return text.Replace("\r", "").Replace("\n\n", "\n").Count(com.Contains);
        }
    }
}
