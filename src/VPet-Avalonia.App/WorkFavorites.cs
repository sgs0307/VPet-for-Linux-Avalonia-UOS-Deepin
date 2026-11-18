using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 工作/学习的收藏(星标)。
    ///
    /// 和物品栏的 Favorites 是同一套思路, 但存另一个文件(workfavorites.txt):
    /// 食物名和工作名可能重名(比如都叫「学习」), 混在一个文件里会互相污染。
    /// 工作配置本身在 mod 的 lps 里, 往里加标志会破坏 MOD 格式, 所以单独存。
    /// </summary>
    public static class WorkFavorites
    {
        private static HashSet<string>? _cache;

        public static string FilePath
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(GameSave.SavePath);
                    return string.IsNullOrEmpty(dir) ? "workfavorites.txt" : Path.Combine(dir, "workfavorites.txt");
                }
                catch
                {
                    return "workfavorites.txt";
                }
            }
        }

        private static HashSet<string> Set
        {
            get
            {
                if (_cache == null)
                    Load();
                return _cache!;
            }
        }

        public static bool Contains(string? name)
            => !string.IsNullOrEmpty(name) && Set.Contains(name!);

        public static int Count => Set.Count;

        /// <summary>收藏/取消收藏, 返回现在是否收藏</summary>
        public static bool Toggle(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            var on = !Set.Remove(name);
            if (on)
                Set.Add(name);
            Save();
            return on;
        }

        /// <summary>清掉 MOD 里已经不存在的工作</summary>
        public static void Prune(IEnumerable<string> alive)
        {
            var keep = new HashSet<string>(alive, StringComparer.Ordinal);
            if (Set.RemoveWhere(x => !keep.Contains(x)) > 0)
                Save();
        }

        private static void Load()
        {
            _cache = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var path = FilePath;
                if (!File.Exists(path))
                    return;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    _cache.Add(line);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 读取工作收藏失败: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllLines(path, new[] { "# VPet 工作/学习收藏" }
                    .Concat(Set.OrderBy(x => x, StringComparer.Ordinal)));
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 保存工作收藏失败: " + e.Message);
            }
        }
    }
}
