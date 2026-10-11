using System.Diagnostics;
using Discord;
using SkiaSharp;

namespace sblngavnav6.Images
{
    internal static class ImageTools
    {
        public const int MaxSourceBytes = 8 * 1024 * 1024;
        public const int MaxSide = 1600;
        public const long MaxPixels = 40_000_000;

        public static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

        private static readonly Lazy<SKTypeface> SerifTypeface = new(() =>
            new[] { "DejaVu Serif", "Liberation Serif", "Times New Roman" }
                .Select(family => SKTypeface.FromFamilyName(family))
                .FirstOrDefault(typeface => typeface is not null && typeface.FamilyName != SKTypeface.Default.FamilyName)
            ?? SKTypeface.Default);

        public static readonly SemaphoreSlim RenderGate = new(2, 2);

        public static SKTypeface Serif => SerifTypeface.Value;

        public static bool IsImage(IAttachment attachment) =>
            attachment.ContentType is { } type ? type.StartsWith("image/", StringComparison.OrdinalIgnoreCase) : attachment.Width is not null;

        public static string AssetPath(string name) => Path.Combine(AppContext.BaseDirectory, "images", name);

        public static SKBitmap Decode(byte[] bytes)
        {
            if (bytes is not { Length: > 0 })
                return null;

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);

            if (codec is null || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
                return null;

            var size = codec.GetScaledDimensions(Math.Min(1f, MaxSide / (float)Math.Max(codec.Info.Width, codec.Info.Height)));
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var decoded = new SKBitmap(info);

            if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            {
                decoded.Dispose();
                return null;
            }

            var scale = Math.Min(1f, MaxSide / (float)Math.Max(size.Width, size.Height));
            var origin = codec.EncodedOrigin;

            if (scale >= 1f && origin == SKEncodedOrigin.TopLeft)
                return decoded;

            using (decoded)
            {
                var width = Math.Max(1, (int)(size.Width * scale));
                var height = Math.Max(1, (int)(size.Height * scale));
                var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
                var result = new SKBitmap(swap ? height : width, swap ? width : height, SKColorType.Bgra8888, SKAlphaType.Premul);

                using var canvas = new SKCanvas(result);
                canvas.SetMatrix(origin switch
                {
                    SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
                    SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
                    SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
                    SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
                    SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
                    SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
                    SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
                    _ => SKMatrix.Identity
                });

                using var image = SKImage.FromBitmap(decoded);
                canvas.DrawImage(image, SKRect.Create(width, height), Smooth);
                return result;
            }
        }

        public static byte[] Png(SKBitmap bitmap)
        {
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        public static byte[] Jpeg(SKSurface surface, int quality = 90)
        {
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data.ToArray();
        }

        public static List<string> Wrap(string text, SKFont font, float maxWidth)
        {
            var lines = new List<string>();
            var line = "";

            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (line.Length > 0 && font.MeasureText(candidate) > maxWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }

            if (line.Length > 0)
                lines.Add(line);

            return lines;
        }

        public static async Task<string> RecognizeTextAsync(byte[] image, string languages, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tesseract",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in new[] { "stdin", "stdout", "-l", languages })
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("tesseract не запустился");

            var reading = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);

            await using (var input = process.StandardInput.BaseStream)
                await input.WriteAsync(image, cancellationToken).ConfigureAwait(false);

            var text = await reading.ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"tesseract завершился с кодом {process.ExitCode}: {error.Trim()}");

            return text;
        }

        public static SKImage Circle(byte[] bytes, int diameter)
        {
            using var source = Decode(bytes);
            using var surface = SKSurface.Create(new SKImageInfo(diameter, diameter, SKColorType.Bgra8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using var clip = new SKPath();
            clip.AddCircle(diameter / 2f, diameter / 2f, diameter / 2f);
            canvas.ClipPath(clip, antialias: true);

            if (source is null)
            {
                using var fill = new SKPaint { Color = new SKColor(88, 101, 242) };
                canvas.DrawRect(0, 0, diameter, diameter, fill);
            }
            else
            {
                var side = Math.Min(source.Width, source.Height);
                var crop = SKRectI.Create((source.Width - side) / 2, (source.Height - side) / 2, side, side);
                using var image = SKImage.FromBitmap(source);
                canvas.DrawImage(image, crop, SKRect.Create(diameter, diameter), Smooth);
            }

            return surface.Snapshot();
        }

        public static async Task<byte[]> EncodeGifAsync(int width, int height, int fps, IEnumerable<SKBitmap> frames, CancellationToken cancellationToken = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{width}x{height}", "-r", fps.ToString(), "-i", "-",
                "-filter_complex", "[0:v]split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3",
                "-loop", "0", "-f", "gif", "-"
            })
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg не запустился");
            using var output = new MemoryStream();

            var reading = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);

            var buffer = new byte[width * height * 4];

            await using (var input = process.StandardInput.BaseStream)
            {
                foreach (var frame in frames)
                {
                    frame.GetPixelSpan().CopyTo(buffer);
                    await input.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }

            await reading.ConfigureAwait(false);
            var error = await errors.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg завершился с кодом {process.ExitCode}: {error.Trim()}");

            return output.ToArray();
        }
    }
}
