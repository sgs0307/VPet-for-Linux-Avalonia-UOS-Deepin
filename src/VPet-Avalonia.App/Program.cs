using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using VPetAvalonia.App.Views;

namespace VPetAvaloniaApp
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // 隐藏的 APNG 自测入口: 设了 VPET_APNG_TEST 就只跑解码/播放验证然后退出,
                // 不开窗口, 方便在无图形环境下定位 APNG 问题
                var apngTest = Environment.GetEnvironmentVariable("VPET_APNG_TEST");
                if (!string.IsNullOrEmpty(apngTest))
                {
                    if (string.Equals(apngTest, "SYNTH", StringComparison.OrdinalIgnoreCase))
                    {
                        bool ok = VPetAvalonia.App.ApngTestTool.RunSynthetic(out var detail);
                        Console.WriteLine($"[APNGTEST] 合成用例: {detail}");
                        Console.WriteLine(ok ? "[APNGTEST] OK" : "[APNGTEST] FAIL");
                        Environment.ExitCode = ok ? 0 : 4;
                        return;
                    }
                    Environment.ExitCode = VPetAvalonia.App.ApngTestTool.Run(apngTest!,
                        Environment.GetEnvironmentVariable("VPET_APNG_OUT"));
                    return;
                }

                Console.WriteLine("[VPet] 启动中...");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception e)
            {
                // 桌宠在统信UOS 上闪退时, 至少要能在终端看到原因
                Console.WriteLine("[VPet] 启动失败: " + e);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();

            // VPET_SOFTWARE_RENDER=1 时强制 Skia 软件渲染,
            // 用于 GPU 驱动异常(llvmpipe 分配失败等)环境下的诊断/兜底
            if (Environment.GetEnvironmentVariable("VPET_SOFTWARE_RENDER") == "1")
            {
                builder = builder.With(new X11PlatformOptions
                {
                    RenderingMode = new[] { X11RenderingMode.Software },
                });
            }
            return builder;
        }
    }

    public class App : Application
    {
        public override void Initialize()
        {
            // 项目里没有 App.axaml, 必须手动挂上主题,
            // 否则 ContextMenu / MenuItem 等控件没有控件模板, 右键菜单会是空白或干脆不显示
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
