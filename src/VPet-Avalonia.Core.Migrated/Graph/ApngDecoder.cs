using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace VPetAvalonia.Core
{
    /// <summary>
    /// APNG 的一帧: 磁盘上的成品图 + 该帧停留时长
    /// </summary>
    public sealed class ApngFrame
    {
        /// <summary>合成后写到磁盘的 PNG 路径</summary>
        public string FilePath = "";

        /// <summary>停留时长(毫秒)</summary>
        public int DelayMs = 100;

        /// <summary>在画布上的位置与尺寸(fcTL 里的值)</summary>
        public int X, Y, Width, Height;

        /// <summary>帧处置方式 0=NONE 1=BACKGROUND 2=PREVIOUS</summary>
        public byte DisposeOp;

        /// <summary>混合方式 0=SOURCE 1=OVER</summary>
        public byte BlendOp;
    }

    /// <summary>
    /// 解析结果
    /// </summary>
    public sealed class ApngInfo
    {
        public List<ApngFrame> Frames = new List<ApngFrame>();

        /// <summary>画布宽高(可能已按上限缩放)</summary>
        public int Width, Height;

        /// <summary>原始画布宽高</summary>
        public int SourceWidth, SourceHeight;

        /// <summary>acTL 的播放次数, 0 = 无限</summary>
        public int LoopCount;

        /// <summary>是否真的含有动画帧(没有 acTL 就是普通 PNG)</summary>
        public bool IsAnimated;

        /// <summary>帧图是否来自缓存目录(没有重新解码)</summary>
        public bool FromCache;

        /// <summary>解码耗时(毫秒), 用于诊断</summary>
        public long DecodeMs;
    }

    /// <summary>
    /// APNG 解码器 (纯 SkiaSharp, 不依赖 WPF)
    ///
    /// 做三件事:
    /// 1. 按 PNG 块结构拆出 acTL / fcTL / fdAT, 还原每帧的矩形、时长、处置/混合方式
    /// 2. 按规范把每帧合成到画布上(含 dispose_op 的 BACKGROUND/PREVIOUS 与 blend_op 的 SOURCE/OVER)
    /// 3. 把合成结果逐帧写成 PNG, 交给现有的显示层(PetImageElement)按路径播放
    ///
    /// 为什么不直接在内存里传位图: IUIElement 的抽象只认"路径/位图对象",
    /// 而位图对象是 Avalonia 类型, 属于 App 层; 写成文件可以让 Core 层保持无 UI 依赖,
    /// 同时白拿显示层的 LRU 位图缓存和"后台线程解码"这两件事。
    ///
    /// 合成是一次性的顺序过程(后一帧依赖前一帧的画布状态), 所以整段解码放在缓存目录里做一次,
    /// 之后每次启动直接读现成的帧图 — ARM 上重新合成几十帧要好几秒。
    /// </summary>
    public static class ApngDecoder
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private const string CacheVersion = "v1";

        /// <summary>缓存超过这个时间没被访问就清掉(天)</summary>
        private const int CacheKeepDays = 30;

        private sealed class RawFrame
        {
            public int Width, Height, X, Y;
            public int DelayMs;
            public byte DisposeOp, BlendOp;
            public readonly List<byte[]> Data = new List<byte[]>();
        }

        private sealed class ParsedPng
        {
            public byte[] Ihdr = Array.Empty<byte>();
            public int Width, Height;
            public readonly List<(string Type, byte[] Data)> Shared = new List<(string, byte[])>();
            public readonly List<RawFrame> Frames = new List<RawFrame>();
            public int LoopCount;
            public bool HasAnimation;
            /// <summary>IDAT 出现在第一个 fcTL 之前 = 这张是"默认图", 不属于动画</summary>
            public bool HasDefaultImage;
        }

        /// <summary>
        /// 判断文件是不是 APNG(有 acTL 且在 IDAT 之前)。
        /// 只读块头, 不解码数据 — 普通 PNG 通常读到第二个块就能返回, 621 个动画全扫一遍也很快。
        /// </summary>
        public static bool LooksLikeApng(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var sig = new byte[8];
                if (fs.Read(sig, 0, 8) != 8)
                    return false;
                for (int i = 0; i < 8; i++)
                {
                    if (sig[i] != Signature[i])
                        return false;
                }

                var head = new byte[8];
                while (fs.Position + 8 <= fs.Length)
                {
                    if (fs.Read(head, 0, 8) != 8)
                        return false;
                    uint len = ReadU32(head, 0);
                    string type = Encoding.ASCII.GetString(head, 4, 4);
                    if (type == "acTL")
                        return true;
                    if (type == "IDAT" || type == "IEND")
                        return false;   // 到了图像数据还没有 acTL, 就是普通 PNG
                    // 长度 + 4 字节 CRC + 越界保护
                    if (fs.Position + len + 4 > fs.Length)
                        return false;
                    fs.Seek(len + 4, SeekOrigin.Current);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解码 APNG 到缓存目录。maxWidth &lt;= 0 表示不缩放。
        /// 普通 PNG 会返回 IsAnimated=false 且 Frames 为空, 由调用方回退到静态图。
        /// </summary>
        public static ApngInfo Load(string path, string cacheDir, int maxWidth = 0)
        {
            var info = new ApngInfo();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var parsed = Parse(File.ReadAllBytes(path));
            if (!parsed.HasAnimation || parsed.Frames.Count == 0)
            {
                info.DecodeMs = sw.ElapsedMilliseconds;
                return info;
            }

            info.IsAnimated = true;
            info.LoopCount = parsed.LoopCount;
            info.SourceWidth = parsed.Width;
            info.SourceHeight = parsed.Height;

            // 超宽图缩放: 桌宠贴图按 500 逻辑像素显示, 大画布除了吃内存没意义
            double scale = 1.0;
            if (maxWidth > 0 && parsed.Width > maxWidth)
                scale = maxWidth / (double)parsed.Width;
            info.Width = Math.Max(1, (int)Math.Round(parsed.Width * scale));
            info.Height = Math.Max(1, (int)Math.Round(parsed.Height * scale));

            string dir = BuildCacheDir(path, cacheDir, parsed, info.Width);
            try
            {
                if (TryLoadFromCache(dir, parsed, scale, out var cached))
                {
                    info.Frames = cached;
                    info.FromCache = true;
                    info.DecodeMs = sw.ElapsedMilliseconds;
                    return info;
                }
            }
            catch
            {
                // 缓存读坏了就重建
            }

            var frames = Composite(path, parsed, info, dir);
            info.Frames = frames;
            info.DecodeMs = sw.ElapsedMilliseconds;
            return info;
        }

        #region 解析

        private static ParsedPng Parse(byte[] data)
        {
            if (data.Length < 8)
                throw new InvalidDataException("文件太小, 不是 PNG");
            for (int i = 0; i < 8; i++)
            {
                if (data[i] != Signature[i])
                    throw new InvalidDataException("PNG 签名不对");
            }

            var p = new ParsedPng();
            RawFrame? current = null;
            bool imageDataStarted = false;
            int pos = 8;

            while (pos + 8 <= data.Length)
            {
                uint len = ReadU32(data, pos);
                if (len > int.MaxValue || pos + 12 + (int)len > data.Length)
                    break;
                string type = Encoding.ASCII.GetString(data, pos + 4, 4);
                int dataStart = pos + 8;
                int length = (int)len;
                pos = dataStart + length + 4;   // 跳过 CRC

                switch (type)
                {
                    case "IHDR":
                        p.Ihdr = Slice(data, dataStart, length);
                        p.Width = (int)ReadU32(p.Ihdr, 0);
                        p.Height = (int)ReadU32(p.Ihdr, 4);
                        if (p.Width <= 0 || p.Height <= 0)
                            throw new InvalidDataException("IHDR 尺寸非法");
                        break;

                    case "acTL":
                        if (length >= 8)
                        {
                            p.HasAnimation = true;
                            p.LoopCount = (int)ReadU32(data, dataStart + 4);
                        }
                        break;

                    case "fcTL":
                        if (length < 26)
                            throw new InvalidDataException("fcTL 长度不足");
                        current = new RawFrame
                        {
                            Width = (int)ReadU32(data, dataStart + 4),
                            Height = (int)ReadU32(data, dataStart + 8),
                            X = (int)ReadU32(data, dataStart + 12),
                            Y = (int)ReadU32(data, dataStart + 16),
                            DisposeOp = data[dataStart + 24],
                            BlendOp = data[dataStart + 25],
                        };
                        current.DelayMs = DelayToMs(ReadU16(data, dataStart + 20), ReadU16(data, dataStart + 22));
                        if (current.Width > 0 && current.Height > 0)
                            p.Frames.Add(current);
                        else
                            current = null;
                        break;

                    case "IDAT":
                        if (current == null)
                        {
                            // fcTL 之前的 IDAT = 默认图, 不参与动画
                            p.HasDefaultImage = true;
                        }
                        else
                        {
                            current.Data.Add(Slice(data, dataStart, length));
                        }
                        imageDataStarted = true;
                        break;

                    case "fdAT":
                        if (current == null)
                            throw new InvalidDataException("fdAT 出现在 fcTL 之前");
                        if (length > 4)
                            current.Data.Add(Slice(data, dataStart + 4, length - 4));
                        imageDataStarted = true;
                        break;

                    case "IEND":
                        pos = data.Length;
                        break;

                    default:
                        // 帧编译时需要带上颜色表/透明表等附属块, 只在图像数据之前收集
                        if (!imageDataStarted && IsSharedChunk(type))
                            p.Shared.Add((type, Slice(data, dataStart, length)));
                        break;
                }
            }

            if (p.Ihdr.Length != 13)
                throw new InvalidDataException("缺少 IHDR");
            return p;
        }

        /// <summary>拼"单帧 PNG"时需要用到的附属块(颜色表/透明表/色彩空间)</summary>
        private static bool IsSharedChunk(string type)
        {
            switch (type)
            {
                case "PLTE":
                case "tRNS":
                case "gAMA":
                case "cHRM":
                case "sRGB":
                case "iCCP":
                case "sBIT":
                case "bKGD":
                    return true;
                default:
                    return false;
            }
        }

        private static int DelayToMs(ushort num, ushort den)
        {
            uint n = num == 0 ? 1u : num;      // 规范: 0 视为 1
            uint d = den == 0 ? 100u : den;    // 规范: 0 视为 100
            var ms = (int)Math.Round(n * 1000d / d);
            return ms <= 0 ? 100 : ms;
        }

        #endregion

        #region 合成

        private static List<ApngFrame> Composite(string srcPath, ParsedPng parsed, ApngInfo info, string dir)
        {
            var frames = new List<ApngFrame>(parsed.Frames.Count);
            using var canvasBitmap = new SKBitmap(parsed.Width, parsed.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            canvasBitmap.Erase(SKColors.Transparent);

            string tmp = dir + ".tmp" + Environment.ProcessId;
            try
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);

                for (int i = 0; i < parsed.Frames.Count; i++)
                {
                    var raw = parsed.Frames[i];
                    if (raw.Data.Count == 0)
                        continue;   // 没有图像数据的空帧跳过, 避免整段动画坏掉

                    // PREVIOUS: 先把这一帧要覆盖的区域拍个快照
                    SKBitmap? backup = null;
                    if (raw.DisposeOp == 2)
                    {
                        backup = new SKBitmap(raw.Width, raw.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using var bc = new SKCanvas(backup);
                        bc.DrawBitmap(canvasBitmap,
                            new SKRect(raw.X, raw.Y, raw.X + raw.Width, raw.Y + raw.Height),
                            new SKRect(0, 0, raw.Width, raw.Height));
                    }

                    using (var patch = DecodePatch(parsed, raw))
                    using (var canvas = new SKCanvas(canvasBitmap))
                    using (var paint = new SKPaint())
                    {
                        // SOURCE = 直接替换这块像素(含透明), OVER = 正常 alpha 叠加
                        paint.BlendMode = raw.BlendOp == 0 ? SKBlendMode.Src : SKBlendMode.SrcOver;
                        canvas.DrawBitmap(patch, raw.X, raw.Y, paint);
                    }

                    // 按目标宽度缩放并编码
                    string framePath = System.IO.Path.Combine(tmp,
                        $"f{i:D5}_{raw.DelayMs}.png");
                    if (info.Width == parsed.Width && info.Height == parsed.Height)
                    {
                        SaveBitmap(canvasBitmap, framePath);
                    }
                    else
                    {
                        using var scaled = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using (var sc = new SKCanvas(scaled))
                        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High })
                        {
                            sc.DrawBitmap(canvasBitmap, new SKRect(0, 0, info.Width, info.Height), paint);
                        }
                        SaveBitmap(scaled, framePath);
                    }

                    double k = info.Width / (double)parsed.Width;
                    frames.Add(new ApngFrame
                    {
                        FilePath = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(framePath)),
                        DelayMs = raw.DelayMs,
                        X = (int)Math.Round(raw.X * k),
                        Y = (int)Math.Round(raw.Y * k),
                        Width = (int)Math.Round(raw.Width * k),
                        Height = (int)Math.Round(raw.Height * k),
                        DisposeOp = raw.DisposeOp,
                        BlendOp = raw.BlendOp,
                    });

                    // 输出完再按 dispose_op 修补画布, 供下一帧使用
                    switch (raw.DisposeOp)
                    {
                        case 1:   // BACKGROUND: 这块清成透明
                            using (var canvas = new SKCanvas(canvasBitmap))
                            using (var paint = new SKPaint { BlendMode = SKBlendMode.Src, Color = SKColors.Transparent })
                            {
                                canvas.DrawRect(new SKRect(raw.X, raw.Y, raw.X + raw.Width, raw.Y + raw.Height), paint);
                            }
                            break;
                        case 2:   // PREVIOUS: 恢复到这一帧画上去之前
                            if (backup != null)
                            {
                                using var canvas = new SKCanvas(canvasBitmap);
                                using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                                canvas.DrawBitmap(backup, raw.X, raw.Y, paint);
                            }
                            break;
                    }
                    backup?.Dispose();
                }

                WriteManifest(tmp, srcPath, parsed, info);
                Publish(tmp, dir);
            }
            catch
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                throw;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }

            SweepOldCaches(System.IO.Path.GetDirectoryName(dir));
            return frames;
        }

        private static SKBitmap DecodePatch(ParsedPng parsed, RawFrame frame)
        {
            using var ms = new MemoryStream();
            ms.Write(Signature, 0, Signature.Length);

            var ihdr = (byte[])parsed.Ihdr.Clone();
            WriteU32(ihdr, 0, (uint)frame.Width);
            WriteU32(ihdr, 4, (uint)frame.Height);
            WriteChunk(ms, "IHDR", ihdr);

            foreach (var (type, data) in parsed.Shared)
                WriteChunk(ms, type, data);

            // fdAT 里存的就是"以帧矩形为单位"的完整 PNG 图像数据, 直接当 IDAT 用
            foreach (var d in frame.Data)
                WriteChunk(ms, "IDAT", d);

            WriteChunk(ms, "IEND", Array.Empty<byte>());

            var bmp = SKBitmap.Decode(ms.ToArray());
            if (bmp == null)
                throw new InvalidDataException($"第 {parsed.Frames.IndexOf(frame)} 帧解码失败");
            return bmp;
        }

        private static void SaveBitmap(SKBitmap bitmap, string path)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            data.SaveTo(fs);
        }

        #endregion

        #region 缓存

        private static string BuildCacheDir(string srcPath, string cacheRoot, ParsedPng parsed, int width)
        {
            long stamp = 0;
            try { stamp = File.GetLastWriteTimeUtc(srcPath).Ticks; } catch { }
            string key = $"{CacheVersion}|{srcPath}|{stamp}|{parsed.Frames.Count}|{width}";
            using var sha = SHA1.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(16);
            for (int i = 0; i < 8; i++)
                sb.Append(hash[i].ToString("x2"));
            return System.IO.Path.Combine(cacheRoot, "apng_" + sb);
        }

        private static void WriteManifest(string dir, string srcPath, ParsedPng parsed, ApngInfo info)
        {
            var text = string.Join('\n', new[]
            {
                CacheVersion,
                srcPath,
                parsed.Frames.Count.ToString(),
                info.Width.ToString(),
                info.Height.ToString(),
                parsed.LoopCount.ToString(),
            });
            File.WriteAllText(System.IO.Path.Combine(dir, "manifest.txt"), text);
        }

        private static bool TryLoadFromCache(string dir, ParsedPng parsed, double scale, out List<ApngFrame> frames)
        {
            frames = new List<ApngFrame>();
            var manifest = System.IO.Path.Combine(dir, "manifest.txt");
            if (!Directory.Exists(dir) || !File.Exists(manifest))
                return false;

            var lines = File.ReadAllLines(manifest);
            if (lines.Length < 5 || lines[0] != CacheVersion)
                return false;
            if (!int.TryParse(lines[2], out var count) || count != parsed.Frames.Count)
                return false;

            for (int i = 0; i < parsed.Frames.Count; i++)
            {
                var raw = parsed.Frames[i];
                if (raw.Data.Count == 0)
                    continue;
                var path = System.IO.Path.Combine(dir, $"f{i:D5}_{raw.DelayMs}.png");
                if (!File.Exists(path))
                {
                    frames.Clear();
                    return false;
                }
                double k = scale;
                frames.Add(new ApngFrame
                {
                    FilePath = path,
                    DelayMs = raw.DelayMs,
                    X = (int)Math.Round(raw.X * k),
                    Y = (int)Math.Round(raw.Y * k),
                    Width = (int)Math.Round(raw.Width * k),
                    Height = (int)Math.Round(raw.Height * k),
                    DisposeOp = raw.DisposeOp,
                    BlendOp = raw.BlendOp,
                });
            }

            // 刷新访问时间, 让清扫器知道这套还在用
            try { Directory.SetLastAccessTimeUtc(dir, DateTime.UtcNow); } catch { }
            return frames.Count > 0;
        }

        private static void Publish(string tmpDir, string finalDir)
        {
            try
            {
                if (Directory.Exists(finalDir))
                {
                    // 另一个进程可能刚好建好了, 直接用它
                    if (File.Exists(System.IO.Path.Combine(finalDir, "manifest.txt")))
                    {
                        try { Directory.Delete(tmpDir, true); } catch { }
                        return;
                    }
                    Directory.Delete(finalDir, true);
                }
                Directory.Move(tmpDir, finalDir);
            }
            catch
            {
                // 并发时 move 失败不算错: 帧文件已经写在 finalDir 吗? 没有就保留 tmp 让本次能播
                if (!File.Exists(System.IO.Path.Combine(finalDir, "manifest.txt")))
                    throw;
            }
        }

        private static void SweepOldCaches(string? root)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                    return;
                var limit = DateTime.UtcNow.AddDays(-CacheKeepDays);
                foreach (var dir in Directory.EnumerateDirectories(root, "apng_*"))
                {
                    try
                    {
                        if (Directory.GetLastAccessTimeUtc(dir) < limit)
                            Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        #endregion

        #region PNG 块工具

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] type, byte[] data, int offset, int count)
        {
            uint c = 0xFFFFFFFFu;
            foreach (var b in type)
                c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            for (int i = 0; i < count; i++)
                c = CrcTable[(c ^ data[offset + i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            var lenBytes = new byte[4];
            WriteU32(lenBytes, 0, (uint)data.Length);
            stream.Write(lenBytes, 0, 4);
            stream.Write(typeBytes, 0, 4);
            if (data.Length > 0)
                stream.Write(data, 0, data.Length);
            var crcBytes = new byte[4];
            WriteU32(crcBytes, 0, Crc32(typeBytes, data, 0, data.Length));
            stream.Write(crcBytes, 0, 4);
        }

        private static byte[] Slice(byte[] src, int offset, int count)
        {
            var dst = new byte[count];
            Buffer.BlockCopy(src, offset, dst, 0, count);
            return dst;
        }

        private static uint ReadU32(byte[] data, int offset)
            => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
             | ((uint)data[offset + 2] << 8) | data[offset + 3];

        private static ushort ReadU16(byte[] data, int offset)
            => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        #endregion
    }
}
