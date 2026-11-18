using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 物品栏的收藏(星标)。
    ///
    /// 原版 winInventory 有收藏栏, 但把收藏标志塞进了存档的 item 结构里;
    /// 移植版的背包是 inventory.txt, 往里加标志会破坏"名称\t数量"的两列格式,
    /// 所以单开一个 favorites.txt, 每行一个食物名。丢了也只是少了星标, 不影响物品。
    /// </summary>
    public static class Favorites
    {
        private static HashSet<string>? _cache;

        public static string FilePath
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(GameSave.SavePath);
                    return string.IsNullOrEmpty(dir) ? "favorites.txt" : Path.Combine(dir, "favorites.txt");
                }
                catch
                {
                    return "favorites.txt";
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

        /// <summary>清掉已经不存在的物品(背包里没有的)</summary>
        public static void Prune(IEnumerable<string> alive)
        {
            var keep = new HashSet<string>(alive, StringComparer.Ordinal);
            var removed = Set.RemoveWhere(x => !keep.Contains(x));
            if (removed > 0)
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
                Console.WriteLine("[VPet] 读取收藏失败: " + e.Message);
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
                File.WriteAllLines(path, new[] { "# VPet 物品栏收藏" }
                    .Concat(Set.OrderBy(x => x, StringComparer.Ordinal)));
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 保存收藏失败: " + e.Message);
            }
        }
    }
}
