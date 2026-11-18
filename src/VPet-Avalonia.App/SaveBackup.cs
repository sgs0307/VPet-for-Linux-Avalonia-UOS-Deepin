using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 手动备份存档。
    ///
    /// 桌宠的进度全在几个文本文件里(save.lps / inventory.txt / stats.txt ...),
    /// 一旦某次异常退出写坏, 手动备份就是唯一的后悔药。
    /// 原版 winSaveManager 的"存档管理"是整套多存档切换, 这里只做最小可用的一环:
    /// 一键把当前全部数据文件复制到 backup/&lt;时间戳&gt;/, 并只保留最近 N 份。
    /// </summary>
    public static class SaveBackup
    {
        /// <summary>保留最近多少份备份</summary>
        public const int KeepCount = 8;

        private static readonly string[] DataFiles =
        {
            "save.lps", "inventory.txt", "stats.txt", "achievements.txt", "settings.txt", "favorites.txt",
        };

        /// <summary>备份根目录(与存档同目录)</summary>
        public static string Root
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(GameSave.SavePath);
                    if (string.IsNullOrEmpty(dir))
                        return "backup";
                    return Path.Combine(dir, "backup");
                }
                catch
                {
                    return "backup";
                }
            }
        }

        /// <summary>
        /// 立即备份一次, 返回备份目录(失败返回 null) 与复制的文件数
        /// </summary>
        public static string? Create(out int copied)
        {
            copied = 0;
            try
            {
                var srcDir = Path.GetDirectoryName(GameSave.SavePath);
                if (string.IsNullOrEmpty(srcDir) || !Directory.Exists(srcDir))
                    return null;

                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var target = Path.Combine(Root, stamp);
                // 同一秒内点两次: 加后缀而不是覆盖
                var suffix = 1;
                while (Directory.Exists(target))
                    target = Path.Combine(Root, stamp + "-" + (++suffix));

                Directory.CreateDirectory(target);
                foreach (var name in DataFiles)
                {
                    var src = Path.Combine(srcDir, name);
                    if (!File.Exists(src))
                        continue;
                    File.Copy(src, Path.Combine(target, name), true);
                    copied++;
                }

                if (copied == 0)
                {
                    try { Directory.Delete(target); } catch { }
                    return null;
                }

                Trim();
                Console.WriteLine($"[VPet] 备份完成: {target} ({copied} 个文件)");
                return target;
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 备份失败: " + e.Message);
                return null;
            }
        }

        /// <summary>已有备份数量</summary>
        public static int Count()
        {
            try
            {
                return Directory.Exists(Root)
                    ? Directory.GetDirectories(Root).Length
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>只保留最近 KeepCount 份, 旧的删掉</summary>
        private static void Trim()
        {
            try
            {
                if (!Directory.Exists(Root))
                    return;
                var dirs = Directory.GetDirectories(Root)
                    .OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal)
                    .ToList();
                foreach (var old in dirs.Skip(KeepCount))
                    Directory.Delete(old, true);
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 清理旧备份失败: " + e.Message);
            }
        }
    }
}
