using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// 食物背包
    /// 原版的物品是存在存档的结构体里, 这里单独用一个纯文本文件保存,
    /// 好处是不动现有存档格式(存档格式已在上游版本间变化过多次), 也不会因为解析失败整档报废
    /// </summary>
    public class Inventory
    {
        private readonly Dictionary<string, int> _items = new(StringComparer.Ordinal);

        /// <summary>
        /// 库存文件路径 (与存档同目录)
        /// </summary>
        public static string FilePath
        {
            get
            {
                var save = GameSave.SavePath;
                var dir = Path.GetDirectoryName(save);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.GetTempPath();
                return Path.Combine(dir, "inventory.txt");
            }
        }

        /// <summary>
        /// 是否是全新的背包(文件不存在)
        /// </summary>
        public bool IsNew { get; private set; } = true;

        /// <summary>
        /// 全部物品(名称 -> 数量)
        /// </summary>
        public IReadOnlyDictionary<string, int> Items => _items;

        /// <summary>
        /// 物品总数
        /// </summary>
        public int TotalCount => _items.Values.Sum();

        /// <summary>
        /// 查询数量
        /// </summary>
        public int Count(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            return _items.TryGetValue(name, out var n) ? n : 0;
        }

        /// <summary>
        /// 添加物品
        /// </summary>
        public void Add(string name, int count = 1)
        {
            if (string.IsNullOrEmpty(name) || count <= 0)
                return;
            _items[name] = Count(name) + count;
        }

        /// <summary>
        /// 消耗一个物品, 没有则返回 false
        /// </summary>
        public bool TryConsume(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!_items.TryGetValue(name, out var n) || n <= 0)
                return false;
            if (n == 1)
                _items.Remove(name);
            else
                _items[name] = n - 1;
            return true;
        }

        /// <summary>
        /// 读档
        /// </summary>
        public void Load()
        {
            _items.Clear();
            try
            {
                var path = FilePath;
                if (!File.Exists(path))
                {
                    IsNew = true;
                    return;
                }
                IsNew = false;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    // 格式: 名称<TAB>数量
                    var idx = raw.LastIndexOf('\t');
                    if (idx <= 0)
                        continue;
                    var name = raw[..idx];
                    if (!int.TryParse(raw[(idx + 1)..], out var n) || n <= 0)
                        continue;
                    _items[name] = n;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 背包读取失败: " + e.Message);
                IsNew = true;
            }
        }

        /// <summary>
        /// 存档
        /// </summary>
        public void Save()
        {
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                foreach (var kv in _items.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (kv.Value <= 0)
                        continue;
                    sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                IsNew = false;
            }
            catch (Exception e)
            {
                Console.WriteLine("[VPet] 背包保存失败: " + e.Message);
            }
        }
    }
}
