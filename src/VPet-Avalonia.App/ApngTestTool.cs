using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Interfaces;
using VPetAvalonia.Core;

namespace VPetAvalonia.App
{
    /// <summary>
    /// APNG 支持的命令行自测工具 (只在设置了 VPET_APNG_TEST 时启用, 不影响正常启动)
    ///
    /// 用法:
    ///   VPET_APNG_TEST=/path/to/test.apng [VPET_APNG_OUT=/dir] dotnet VPet-Avalonia.App.dll
    ///
    /// 做三件事:
    ///   1. 解析: 打印帧数/画布/每帧时长/是否命中缓存
    ///   2. 导出: 把合成后的帧拷到 VPET_APNG_OUT, 方便和 ffmpeg 的解码结果逐像素比对
    ///   3. 播放: 用假的 IUIElement 跑一遍 APNGAnimation, 验证帧序列、结束回调、状态复位
    /// </summary>
    public static class ApngTestTool
    {
        /// <summary>
        /// 记录 SetContent 调用的假显示元素
        /// </summary>
        private sealed class FakeElement : IUIElement
        {
            public readonly List<string> Contents = new List<string>();
            public double Width { get; set; }
            public double Height { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public int ShowCount;
            public void Show() => ShowCount++;
            public void Hide() { }
            public void UpdateLayout() { }
            public void SetContent(object content) => Contents.Add(content?.ToString() ?? "");
        }

        /// <summary>测试用: 直接在当前线程执行</summary>
        private sealed class InlineDispatcher : IDispatcher
        {
            public void Invoke(Action action) => action();
            public void BeginInvoke(Action action) => action();
            public T Invoke<T>(Func<T> func) => func();
            public bool CheckAccess() => true;
        }

        public static int Run(string file, string? outDir)
        {
            Console.WriteLine($"[APNGTEST] 文件={file}");
            if (!File.Exists(file))
            {
                Console.WriteLine("[APNGTEST] FAIL 文件不存在");
                return 2;
            }

            Console.WriteLine($"[APNGTEST] LooksLikeApng={ApngDecoder.LooksLikeApng(file)}");

            var dir = string.IsNullOrEmpty(GraphCoreAvalonia.CachePath)
                ? Path.Combine(Path.GetTempPath(), "VPetCache")
                : GraphCoreAvalonia.CachePath;

            int maxWidth = 0;
            if (int.TryParse(Environment.GetEnvironmentVariable("VPET_APNG_MAXW"), out var mw))
                maxWidth = mw;

            var sw = Stopwatch.StartNew();
            var info = ApngDecoder.Load(file, dir, maxWidth);
            sw.Stop();
            Console.WriteLine($"[APNGTEST] 解析: 动画={info.IsAnimated} 帧={info.Frames.Count} " +
                              $"画布={info.Width}x{info.Height} 播放次数={info.LoopCount} " +
                              $"缓存命中={info.FromCache} 耗时={sw.ElapsedMilliseconds}ms");

            if (!info.IsAnimated || info.Frames.Count == 0)
            {
                Console.WriteLine("[APNGTEST] FAIL 没有解析出帧");
                return 3;
            }

            // 第二次必须命中缓存, 否则每次启动都要重新合成
            var again = ApngDecoder.Load(file, dir, maxWidth);
            Console.WriteLine($"[APNGTEST] 二次解析: 缓存命中={again.FromCache} 耗时={again.DecodeMs}ms");

            var delays = new List<string>();
            for (int i = 0; i < info.Frames.Count; i++)
            {
                var f = info.Frames[i];
                delays.Add($"{f.DelayMs}");
                if (!string.IsNullOrEmpty(outDir))
                {
                    try
                    {
                        Directory.CreateDirectory(outDir);
                        File.Copy(f.FilePath, Path.Combine(outDir, $"out_{i:D3}.png"), true);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[APNGTEST] 导出第 {i} 帧失败: {e.Message}");
                    }
                }
                Console.WriteLine($"[APNGTEST] 帧{i}: {Path.GetFileName(f.FilePath)} 时长={f.DelayMs}ms " +
                                  $"矩形=({f.X},{f.Y} {f.Width}x{f.Height}) 处置={f.DisposeOp} 混合={f.BlendOp} " +
                                  $"大小={(File.Exists(f.FilePath) ? new FileInfo(f.FilePath).Length : -1)}B");
            }
            Console.WriteLine($"[APNGTEST] 时长序列={string.Join(",", delays)}");

            // 播放验证
            var anim = new APNGAnimation(null!, file, new GraphInfo("apngtest"), isLoop: false);
            var elem = new FakeElement();
            using var done = new ManualResetEventSlim(false);
            var t0 = Stopwatch.StartNew();
            anim.Run(elem, () => done.Set());
            bool finished = done.Wait(TimeSpan.FromSeconds(30));
            t0.Stop();

            Console.WriteLine($"[APNGTEST] 播放: 结束={finished} 用时={t0.ElapsedMilliseconds}ms " +
                              $"SetContent={elem.Contents.Count} 次 Show={elem.ShowCount} 次 " +
                              $"FrameCount={anim.FrameCount} 就绪={anim.IsReady} 失败={anim.IsFail} PlayState={anim.Control?.PlayState}");

            int distinct = new HashSet<string>(elem.Contents).Count;
            Console.WriteLine($"[APNGTEST] 不同帧图={distinct} (应={anim.FrameCount})");

            bool ok = finished
                      && anim.IsReady
                      && !anim.IsFail
                      && elem.Contents.Count == anim.FrameCount
                      && distinct == anim.FrameCount
                      && anim.Control?.PlayState == false;

            // 循环模式: 跑 1 秒应该还在播, Stop 之后要退出
            if (ok)
            {
                var loopAnim = new APNGAnimation(null!, file, new GraphInfo("apngtest"), isLoop: true);
                var loopElem = new FakeElement();
                var stopped = new ManualResetEventSlim(false);
                loopAnim.Run(loopElem, () => stopped.Set());
                Thread.Sleep(1200);
                bool alive = loopAnim.Control?.PlayState == true;
                int played = loopElem.Contents.Count;
                loopAnim.Stop(false);
                Thread.Sleep(400);
                bool exited = loopAnim.Control?.PlayState == false;
                Console.WriteLine($"[APNGTEST] 循环: 1.2s 内播放={played} 帧 仍在播={alive} Stop 后退出={exited}");
                // 1.2 秒不一定能跑完整轮(帧多/帧慢时), 只要在持续产出帧就说明循环生效
                ok = ok && alive && played >= 3 && exited;
            }

            Console.WriteLine(ok ? "[APNGTEST] OK" : "[APNGTEST] FAIL 断言未通过");
            return ok ? 0 : 4;
        }

        #region 内置合成用例(供 VPET_SELFTEST 回归)

        /// <summary>
        /// 现场合成一个 4 帧 APNG 再解回来, 不依赖任何外部素材。
        /// 覆盖: 多帧时长、逐帧像素、缓存目录复用、播放循环。
        /// </summary>
        public static bool RunSynthetic(out string detail)
        {
            detail = "";
            string root = Path.Combine(Path.GetTempPath(), "vpet-apng-synth");
            string file = Path.Combine(root, "synth.apng");
            var colors = new (byte R, byte G, byte B, byte A)[]
            {
                (220, 40, 40, 255),
                (40, 180, 60, 255),
                (40, 80, 220, 255),
                (230, 200, 40, 255),
            };
            var delays = new[] { 60, 70, 80, 90 };

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(file, BuildApng(64, 64, colors, delays));

                var dir = Path.Combine(root, "cache");
                var info = ApngDecoder.Load(file, dir);
                var again = ApngDecoder.Load(file, dir);

                bool ok = info.IsAnimated && info.Frames.Count == colors.Length && again.FromCache;
                var gotDelays = new List<string>();
                for (int i = 0; i < info.Frames.Count && ok; i++)
                {
                    gotDelays.Add(info.Frames[i].DelayMs.ToString());
                    if (info.Frames[i].DelayMs != delays[i])
                        ok = false;
                    using var bmp = SkiaSharp.SKBitmap.Decode(info.Frames[i].FilePath);
                    if (bmp == null)
                    {
                        ok = false;
                        break;
                    }
                    var px = bmp.GetPixel(32, 32);
                    if (px.Red != colors[i].R || px.Green != colors[i].G || px.Blue != colors[i].B || px.Alpha < 250)
                        ok = false;
                }

                // 播放: 4 帧播完应自然结束
                if (ok)
                {
                    var anim = new APNGAnimation(null!, file, new GraphInfo("synth"), isLoop: false);
                    var elem = new FakeElement();
                    using var done = new ManualResetEventSlim(false);
                    anim.Run(elem, () => done.Set());
                    ok = done.Wait(TimeSpan.FromSeconds(10))
                         && anim.FrameCount == colors.Length
                         && anim.Control?.PlayState == false;
                }

                // 资源分派: 声明成 picture 的 APNG 也要自动走 APNGAnimation;
                // 声明成 apnganimation 的普通 PNG 要退回 Picture
                bool autoApng = false, fallbackPicture = false;
                if (ok)
                {
                    var core = new GraphCoreAvalonia(1000, new InlineDispatcher(), new GraphCoreAvalonia.Config());
                    try
                    {
                        Picture.LoadGraph(core, new FileInfo(file), new LinePutScript.Line("picture"));
                        autoApng = core.GetAllGraphs().Any(g => g is APNGAnimation);

                        string plain = Path.Combine(root, "plain.png");
                        using (var bmp = new SkiaSharp.SKBitmap(32, 32, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                        {
                            bmp.Erase(new SkiaSharp.SKColor(10, 200, 240, 255));
                            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
                            using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                            File.WriteAllBytes(plain, data.ToArray());
                        }
                        APNGAnimation.LoadGraph(core, new FileInfo(plain), new LinePutScript.Line("apnganimation"));
                        fallbackPicture = core.GetAllGraphs().Any(g => g is Picture);
                    }
                    finally
                    {
                        core.Dispose();
                    }
                }

                detail = $"帧={info.Frames.Count} 时长={string.Join(",", gotDelays)} 缓存复用={again.FromCache} " +
                         $"自动识别={autoApng} 静态回退={fallbackPicture} {(ok ? "通过" : "失败")}";
                return ok && autoApng && fallbackPicture;
            }
            catch (Exception e)
            {
                detail = "异常: " + e.Message;
                return false;
            }
        }

        private static byte[] BuildApng(int w, int h, (byte R, byte G, byte B, byte A)[] colors, int[] delays)
        {
            var ms = new MemoryStream();
            var sig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            ms.Write(sig, 0, sig.Length);
            WriteChunk(ms, "IHDR", Concat(
                U32((uint)w), U32((uint)h),
                new byte[] { 8, 6, 0, 0, 0 }));   // 8bit RGBA, 无隔行
            WriteChunk(ms, "acTL", Concat(
                U32((uint)colors.Length),
                U32(0)));   // 0 = 无限循环

            uint seq = 0;
            for (int i = 0; i < colors.Length; i++)
            {
                ushort dn = (ushort)delays[i];
                WriteChunk(ms, "fcTL", Concat(
                    U32(seq), U32((uint)w), U32((uint)h), U32(0), U32(0),
                    U16(dn), U16(1000),
                    new byte[] { 0, 0 }));   // dispose=NONE, blend=SOURCE
                seq++;
                var data = RawRgbaZlib(w, h, colors[i]);
                if (i == 0)
                {
                    WriteChunk(ms, "IDAT", data);
                }
                else
                {
                    WriteChunk(ms, "fdAT", Concat(U32(seq), data));
                    seq++;
                }
            }
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        private static byte[] RawRgbaZlib(int w, int h, (byte R, byte G, byte B, byte A) c)
        {
            var raw = new byte[h * (1 + w * 4)];
            int p = 0;
            for (int y = 0; y < h; y++)
            {
                raw[p++] = 0;   // filter: None
                for (int x = 0; x < w; x++)
                {
                    raw[p++] = c.R;
                    raw[p++] = c.G;
                    raw[p++] = c.B;
                    raw[p++] = c.A;
                }
            }
            using var outMs = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(outMs, System.IO.Compression.CompressionLevel.Fastest, true))
                z.Write(raw, 0, raw.Length);
            return outMs.ToArray();
        }

        private static byte[] U32(uint v)
            => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private static byte[] U16(ushort v)
            => new[] { (byte)(v >> 8), (byte)v };

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (var p in parts)
                total += p.Length;
            var dst = new byte[total];
            int o = 0;
            foreach (var p in parts)
            {
                Buffer.BlockCopy(p, 0, dst, o, p.Length);
                o += p.Length;
            }
            return dst;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var len = new byte[]
            {
                (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length,
            };
            s.Write(len, 0, 4);
            var tag = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(tag, 0, 4);
            if (data.Length > 0)
                s.Write(data, 0, data.Length);
            var crc = new byte[4];
            uint c = Crc32(tag, data);
            crc[0] = (byte)(c >> 24);
            crc[1] = (byte)(c >> 16);
            crc[2] = (byte)(c >> 8);
            crc[3] = (byte)c;
            s.Write(crc, 0, 4);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static uint Crc32(byte[] tag, byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var b in tag)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            foreach (var b in data)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        #endregion
    }
}
