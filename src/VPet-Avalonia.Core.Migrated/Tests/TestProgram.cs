using System;
using System.Collections.Generic;
using System.Linq;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Graphics;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.Core.Tests
{
    /// <summary>
    /// 测试调度器实现
    /// </summary>
    public class TestDispatcher : IDispatcher
    {
        public void Invoke(Action action) => action();
        public T Invoke<T>(Func<T> func) => func();
        public void BeginInvoke(Action action) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>
    /// 测试用图形实现
    /// </summary>
    public class TestGraph : IGraph
    {
        public GraphInfo GraphInfo { get; set; } = new GraphInfo("test");
        public bool IsLoop { get; set; }
        public bool IsReady => true;
        public bool IsFail => false;
        public string FailMessage => "";
        public ITaskControl? Control { get; set; }
        public string? Path { get; set; }
        public long LastUseTimeTicks { get; set; }
        
        public void Run(IUIElement parent, Action? endAction = null) => endAction?.Invoke();
        public void Stop(bool stopEndAction) { }
        public void SetContinue() { }
        public void Touch() => LastUseTimeTicks = DateTime.Now.Ticks;
        public void CleanupIdleCache(long nowTicks) { }
        public void Dispose() { }
    }

    public class TestProgram
    {
        static int _passCount = 0;
        static int _failCount = 0;

        static void Assert(bool condition, string testName)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"  [PASS] {testName}");
            }
            else
            {
                _failCount++;
                Console.WriteLine($"  [FAIL] {testName}");
            }
        }

        static void AssertEqual<T>(T expected, T actual, string testName)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
            {
                _passCount++;
                Console.WriteLine($"  [PASS] {testName} (={expected})");
            }
            else
            {
                _failCount++;
                Console.WriteLine($"  [FAIL] {testName}: 期望 {expected}, 实际 {actual}");
            }
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("=== VPet-Avalonia 框架测试 ===\n");

            TestGraphInfo();
            TestGraphCore();
            TestGameSave();
            TestFunction();
            TestPetLoader();

            Console.WriteLine($"\n=== 测试结果: {_passCount} 通过, {_failCount} 失败 ===");
            Environment.Exit(_failCount > 0 ? 1 : 0);
        }

        static void TestGraphInfo()
        {
            Console.WriteLine("[测试] GraphInfo 类");
            var info = new GraphInfo("test");
            AssertEqual("test", info.Name, "Name 属性");
            AssertEqual(GraphInfo.GraphType.Common, info.Type, "Type 默认值");
            AssertEqual(GraphInfo.AnimatType.Single, info.Animat, "Animat 默认值");
            AssertEqual(ModeType.Nomal, info.ModeType, "ModeType 默认值");
            Console.WriteLine();
        }

        static void TestGraphCore()
        {
            Console.WriteLine("[测试] GraphCoreAvalonia");
            var dispatcher = new TestDispatcher();
            var config = new GraphCoreAvalonia.Config();
            var core = new GraphCoreAvalonia(1000, dispatcher, config);

            // 测试 AddGraph
            var graph1 = new TestGraph { GraphInfo = new GraphInfo("idle") { Type = GraphInfo.GraphType.Idel, Animat = GraphInfo.AnimatType.A_Start } };
            var graph2 = new TestGraph { GraphInfo = new GraphInfo("idle") { Type = GraphInfo.GraphType.Idel, Animat = GraphInfo.AnimatType.B_Loop } };
            core.AddGraph(graph1);
            core.AddGraph(graph2);

            AssertEqual(2, core.GetAllGraphs().Count, "AddGraph 添加 2 个动画");
            AssertEqual(1, core.GraphsName.Count, "GraphsName 有 1 个类型");

            // 测试 FindGraph
            var found = core.FindGraph("idle", GraphInfo.AnimatType.A_Start, ModeType.Nomal);
            Assert(found != null, "FindGraph 找到动画");
            AssertEqual("idle", found?.GraphInfo.Name, "FindGraph 名字正确");

            // 测试 FindName
            var name = core.FindName(GraphInfo.GraphType.Idel);
            AssertEqual("idle", name, "FindName 返回 idle");

            // 测试 FindGraphs
            var graphs = core.FindGraphs("idle", GraphInfo.AnimatType.A_Start, ModeType.Nomal);
            AssertEqual(1, graphs?.Count ?? 0, "FindGraphs 找到 1 个");

            // 测试 RemoveGraph
            core.RemoveGraph(graph1);
            AssertEqual(1, core.GetAllGraphs().Count, "RemoveGraph 移除后剩 1 个");

            // 测试 Config
            Assert(core.GraphConfig != null, "GraphConfig 不为空");
            AssertEqual(10, core.GraphConfig.GetDuration("idle"), "GetDuration 返回 10");

            core.Dispose();
            Console.WriteLine();
        }

        static void TestGameSave()
        {
            Console.WriteLine("[测试] GameSave");
            var save = new GameSave();
            Assert(save != null, "GameSave 创建成功");
            AssertEqual("", save.Name, "Name 默认空");
            AssertEqual(0, save.Money, "Money 默认 0");
            AssertEqual(100, save.Strength, "Strength 默认 100");
            AssertEqual(100, save.Feeling, "Feeling 默认 100");
            AssertEqual(100, save.StrengthMax, "StrengthMax 默认 100");
            AssertEqual(100, save.FeelingMax, "FeelingMax 默认 100");
            Console.WriteLine();
        }

        static void TestFunction()
        {
            Console.WriteLine("[测试] Function");
            // 测试 HEXToColor
            var color = Function.HEXToColor("#FF0000");
            AssertEqual(0xFFFF0000u, color, "HEXToColor 红色");

            // 测试 ColorToHEX
            var hex = Function.ColorToHEX(0xFFFF0000u);
            AssertEqual("#FFFF0000", hex, "ColorToHEX 红色");

            // 测试 Rnd
            var rnd = Function.Rnd.Next(100);
            Assert(rnd >= 0 && rnd < 100, "Rnd 在范围内");
            Console.WriteLine();
        }

        static void TestPetLoader()
        {
            Console.WriteLine("[测试] PetLoader");
            var loader = new PetLoader();
            Assert(loader != null, "PetLoader 创建成功");
            Assert(PetLoader.IGraphConvert != null, "IGraphConvert 字典存在");
            Assert(PetLoader.IGraphConvert.ContainsKey("pnganimation"), "包含 pnganimation 类型");
            Assert(PetLoader.IGraphConvert.ContainsKey("apnganimation"), "包含 apnganimation 类型");
            Assert(PetLoader.IGraphConvert.ContainsKey("picture"), "包含 picture 类型");
            Assert(PetLoader.IGraphConvert.ContainsKey("foodanimation"), "包含 foodanimation 类型");
            Console.WriteLine();
        }
    }
}