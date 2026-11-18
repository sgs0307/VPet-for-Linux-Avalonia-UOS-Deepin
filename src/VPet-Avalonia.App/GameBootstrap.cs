using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LinePutScript;
using VPetAvalonia.Abstractions.Interfaces;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// 启动引导: 定位 MOD 资源、加载宠物动画、建立存档与控制器
    /// 移植时这一整块被遗漏了 —— 之前只有 new GameCore(), 所有字段都是 null,
    /// 所以界面上除了写死的占位文字什么都不会出现
    /// </summary>
    public static class GameBootstrap
    {
        /// <summary>
        /// 初始化游戏核心
        /// </summary>
        /// <param name="dispatcher">UI 调度器</param>
        /// <param name="onMessage">日志回调, 便于在界面上排查资源加载问题</param>
        public static GameCore? Initialize(IDispatcher dispatcher, Action<string>? onMessage = null)
        {
            void Log(string msg)
            {
                Console.WriteLine("[VPet] " + msg);
                onMessage?.Invoke(msg);
            }

            try
            {
                var modDir = FindModDirectory();
                if (modDir == null)
                {
                    Log("未找到 mod 目录, 桌宠资源无法加载。请把 mod 文件夹放到程序目录下。");
                    return null;
                }
                Log("MOD 目录: " + modDir.FullName);

                var pet = LoadFirstPet(modDir, Log);
                if (pet == null)
                {
                    Log("未在 mod 目录中找到任何宠物定义(*.lps)");
                    return null;
                }
                Log($"加载宠物: {pet.Name} (默认名 {pet.PetName})");

                var graph = pet.Graph(1000, dispatcher);
                Log($"动画加载完成, 共 {pet.GraphCount} 组, 索引 {graph.GraphsName.Count} 类");

                var save = new GameSave(string.IsNullOrWhiteSpace(pet.PetName) ? pet.Name : pet.PetName);
                if (File.Exists(GameSave.SavePath))
                    save.Load();
                else
                    save.Strength = save.StrengthFood = save.StrengthDrink = 100;

                var core = new GameCore
                {
                    Graph = graph,
                    Save = save
                };

                // 食物与背包: 没有它们就没有投喂/商店, 只能干看着数值往下掉
                core.Foods = FoodLoader.LoadAll(modDir, Log);
                core.Inventory.Load();
                if (core.Inventory.IsNew && core.Foods.Count > 0)
                {
                    // 首次运行给一点新手补给, 避免一上来背包为空无处投喂
                    foreach (var f in core.Foods
                                 .Where(x => x.Type == Food.FoodType.Meal || x.Type == Food.FoodType.Snack || x.Type == Food.FoodType.Drink)
                                 .OrderBy(x => x.Price)
                                 .Take(2))
                    {
                        core.Inventory.Add(f.Name, 2);
                    }
                    core.Inventory.Save();
                    Log($"已发放新手补给: {core.Inventory.TotalCount} 份食物");
                }

                return core;
            }
            catch (Exception e)
            {
                Log("初始化失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 查找 mod 目录
        /// 依次尝试: 环境变量 -> 程序目录 -> 上级目录 -> 源码目录(开发态)
        /// </summary>
        public static DirectoryInfo? FindModDirectory()
        {
            var candidates = new List<string>();

            var env = Environment.GetEnvironmentVariable("VPET_MOD");
            if (!string.IsNullOrWhiteSpace(env))
                candidates.Add(env!);

            var baseDir = AppContext.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "mod"));
            candidates.Add(Path.Combine(baseDir, "..", "mod"));
            candidates.Add(Path.Combine(baseDir, "..", "..", "mod"));

            // 向上查找最多 6 层, 兼容 dotnet run 与发布后的各种目录布局
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                candidates.Add(Path.Combine(dir.FullName, "mod"));
                candidates.Add(Path.Combine(dir.FullName, "VPet-Simulator.Windows", "mod"));
                dir = dir.Parent;
            }

            foreach (var c in candidates)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(c))
                        continue;
                    var d = new DirectoryInfo(Path.GetFullPath(c));
                    if (d.Exists && d.EnumerateDirectories().Any(x => x.EnumerateFileSystemInfos().Any()))
                        return d;
                }
                catch
                {
                    // 继续尝试下一个候选
                }
            }
            return null;
        }

        /// <summary>
        /// 加载 mod 中第一个可用的宠物
        /// </summary>
        private static PetLoader? LoadFirstPet(DirectoryInfo modDir, Action<string> log)
        {
            // 按 mod 目录约定: mod/xxxx_core/pet/*.lps
            foreach (var mod in modDir.EnumerateDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var petDir = Path.Combine(mod.FullName, "pet");
                // Linux 区分大小写, 目录名可能是 Pet
                if (!Directory.Exists(petDir))
                {
                    var found = Function.FindEntryCaseInsensitive(mod.FullName, "pet");
                    if (found == null || !Directory.Exists(found))
                        continue;
                    petDir = found;
                }

                var petDirInfo = new DirectoryInfo(petDir);
                foreach (var file in petDirInfo.GetFiles("*.lps").OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var lps = new LpsDocument(File.ReadAllText(file.FullName));
                        if (lps.First() == null)
                            continue;
                        var loader = new PetLoader(lps, petDirInfo);
                        if (loader.path.Count == 0)
                            continue;
                        // 确认资源目录真实存在
                        var first = Function.NormalizePath(loader.path[0]);
                        if (!Path.IsPathRooted(first))
                            first = Path.GetFullPath(first);
                        if (!Directory.Exists(first))
                        {
                            log($"宠物资源目录不存在, 跳过: {first}");
                            continue;
                        }
                        log($"使用宠物定义: {file.Name}");
                        return loader;
                    }
                    catch (Exception e)
                    {
                        log($"读取宠物定义失败 {file.Name}: {e.Message}");
                    }
                }
            }
            return null;
        }
    }
}
