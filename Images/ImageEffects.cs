using SkiaSharp;
using static sblngavnav6.Images.ImageTools;

namespace sblngavnav6.Images
{
    internal static class ImageEffects
    {
        public static readonly Lazy<SKImage> Sheff = new(() =>
            SKImage.FromEncodedData(ImageTools.AssetPath("sheff.png")) ?? throw new InvalidOperationException("не найден sheff.png"));

        public static byte[] Demotivator(SKBitmap source, string title, string caption)
        {
            const float pictureWidth = 600;
            const float margin = 60;
            const float gap = 8;

            var pictureHeight = Math.Clamp(source.Height * pictureWidth / source.Width, pictureWidth / 3, pictureWidth * 2);
            var cropWidth = Math.Min(source.Width, source.Height * pictureWidth / pictureHeight);
            var cropHeight = Math.Min(source.Height, source.Width * pictureHeight / pictureWidth);
            var width = (int)(pictureWidth + margin * 2);

            using var titleFont = new SKFont(ImageTools.Serif, 46) { Subpixel = true };
            using var captionFont = new SKFont(ImageTools.Serif, 24) { Subpixel = true };

            var titleLines = string.IsNullOrWhiteSpace(title) ? [] : ImageTools.Wrap(title, titleFont, width - margin);
            var captionLines = string.IsNullOrWhiteSpace(caption) ? [] : ImageTools.Wrap(caption, captionFont, width - margin);

            var textHeight = 24 + titleLines.Count * 54 + (captionLines.Count > 0 ? 10 + captionLines.Count * 32 : 0);
            var height = (int)(margin * 0.6f + pictureHeight + gap * 2 + textHeight + margin * 0.4f);

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);

            var picture = SKRect.Create(margin, margin * 0.6f, pictureWidth, pictureHeight);
            using (var image = SKImage.FromBitmap(source))
                canvas.DrawImage(image, SKRect.Create((source.Width - cropWidth) / 2, (source.Height - cropHeight) / 2, cropWidth, cropHeight), picture, Smooth);

            using (var frame = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
                canvas.DrawRect(SKRect.Inflate(picture, gap, gap), frame);

            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            var y = picture.Bottom + gap + 24 + 44;

            foreach (var line in titleLines)
            {
                canvas.DrawText(line, width / 2f, y, SKTextAlign.Center, titleFont, paint);
                y += 54;
            }

            y += 6;

            foreach (var line in captionLines)
            {
                canvas.DrawText(line, width / 2f, y, SKTextAlign.Center, captionFont, paint);
                y += 32;
            }

            return ImageTools.Jpeg(surface);
        }

        public static readonly SKPoint[] SheffPoints =
        [
            new(23 / 112f, 32 / 112f), new(86 / 112f, 32 / 112f), new(55 / 112f, 62 / 112f),
            new(26 / 112f, 91 / 112f), new(95 / 112f, 91 / 112f)
        ];

        public static readonly SKPoint[] AvatarPoints =
        [
            new(0.35f, 0.42f), new(0.65f, 0.42f), new(0.5f, 0.56f),
            new(0.38f, 0.7f), new(0.62f, 0.7f)
        ];

        public static byte[] Sheffogram(SKBitmap source, SKImage overlay, SKPoint[] template, IReadOnlyList<Face> faces)
        {
            using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height));
            var canvas = surface.Canvas;

            using var sourceImage = SKImage.FromBitmap(source);
            canvas.DrawImage(sourceImage, 0, 0);

            var padX = overlay.Width * 0.15f;
            var padY = overlay.Height * 0.15f;
            var paddedWidth = overlay.Width + padX * 2;
            var paddedHeight = overlay.Height * 1.45f;

            using var padded = Pad(overlay, paddedWidth, paddedHeight, padX, padY);

            var anchors = template.Select(point => new SKPoint(point.X * overlay.Width + padX, point.Y * overlay.Height + padY)).ToArray();
            var overlayTone = Average(overlay, SKRect.Create(overlay.Width, overlay.Height));
            using var mask = FaceMask(padded.Width, padded.Height,
                padX + overlay.Width / 2f, padY - overlay.Height * 0.02f, padY + overlay.Height * 1.12f, overlay.Width * 0.5f, overlay.Width * 0.08f);

            foreach (var face in faces)
            {
                var skinTone = Average(sourceImage, SKRect.Intersect(face.Box, SKRect.Create(source.Width, source.Height)));

                using var tone = new SKPaint
                {
                    ColorFilter = SKColorFilter.CreateColorMatrix(
                    [
                        Blend(skinTone.Red, overlayTone.Red), 0, 0, 0, 0,
                        0, Blend(skinTone.Green, overlayTone.Green), 0, 0, 0,
                        0, 0, Blend(skinTone.Blue, overlayTone.Blue), 0, 0,
                        0, 0, 0, 1, 0
                    ])
                };

                using var feather = new SKPaint { BlendMode = SKBlendMode.DstIn };

                canvas.Save();
                canvas.Concat(Fit(anchors, face.Points));
                canvas.SaveLayer();
                canvas.DrawImage(padded, 0, 0, Smooth, tone);
                canvas.DrawImage(mask, 0, 0, feather);
                canvas.Restore();
                canvas.Restore();
            }

            return ImageTools.Jpeg(surface);
        }

        private static SKImage FaceMask(int width, int height, float centerX, float top, float bottom, float radius, float softness)
        {
            var widest = top + (bottom - top) * 0.42f;
            using var path = new SKPath();

            path.MoveTo(centerX, top);
            path.CubicTo(centerX + radius * 0.56f, top, centerX + radius, widest - (widest - top) * 0.55f, centerX + radius, widest);
            path.CubicTo(centerX + radius, widest + (bottom - widest) * 0.55f, centerX + radius * 0.45f, bottom, centerX, bottom);
            path.CubicTo(centerX - radius * 0.45f, bottom, centerX - radius, widest + (bottom - widest) * 0.55f, centerX - radius, widest);
            path.CubicTo(centerX - radius, widest - (widest - top) * 0.55f, centerX - radius * 0.56f, top, centerX, top);
            path.Close();

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, softness) };
            surface.Canvas.DrawPath(path, paint);

            return surface.Snapshot();
        }

        private static SKImage Pad(SKImage image, float width, float height, float offsetX, float offsetY)
        {
            using var surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(width), (int)Math.Ceiling(height)));
            var canvas = surface.Canvas;
            var area = SKRect.Create(offsetX, offsetY, image.Width, image.Height);
            var soften = Math.Min(image.Width, image.Height) * 0.05f;

            canvas.Clear(Average(image, SKRect.Create(image.Width, image.Height * 0.7f)));
            canvas.SaveLayer();
            canvas.DrawImage(image, area, Smooth);

            using (var edge = new SKPaint { Color = SKColors.Black, BlendMode = SKBlendMode.DstIn, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, soften) })
                canvas.DrawRect(SKRect.Inflate(area, -soften * 1.5f, -soften * 1.5f), edge);

            canvas.Restore();
            return surface.Snapshot();
        }

        private static SKMatrix Fit(SKPoint[] from, SKPoint[] to)
        {
            double suu = 0, suv = 0, svv = 0, su = 0, sv = 0;
            double sxu = 0, sxv = 0, sx = 0, syu = 0, syv = 0, sy = 0;
            var n = from.Length;

            for (var i = 0; i < n; i++)
            {
                double u = from[i].X, v = from[i].Y, x = to[i].X, y = to[i].Y;
                suu += u * u; suv += u * v; svv += v * v; su += u; sv += v;
                sxu += x * u; sxv += x * v; sx += x;
                syu += y * u; syv += y * v; sy += y;
            }

            var (a, b, c) = Solve(suu, suv, su, suv, svv, sv, su, sv, n, sxu, sxv, sx);
            var (d, e, f) = Solve(suu, suv, su, suv, svv, sv, su, sv, n, syu, syv, sy);

            return new SKMatrix((float)a, (float)b, (float)c, (float)d, (float)e, (float)f, 0, 0, 1);
        }

        private static (double, double, double) Solve(
            double m00, double m01, double m02,
            double m10, double m11, double m12,
            double m20, double m21, double m22,
            double r0, double r1, double r2)
        {
            var det = m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);

            return (
                (r0 * (m11 * m22 - m12 * m21) - m01 * (r1 * m22 - m12 * r2) + m02 * (r1 * m21 - m11 * r2)) / det,
                (m00 * (r1 * m22 - m12 * r2) - r0 * (m10 * m22 - m12 * m20) + m02 * (m10 * r2 - r1 * m20)) / det,
                (m00 * (m11 * r2 - r1 * m21) - m01 * (m10 * r2 - r1 * m20) + r0 * (m10 * m21 - m11 * m20)) / det);
        }

        private static SKColor Average(SKImage image, SKRect area)
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            surface.Canvas.DrawImage(image, area, SKRect.Create(1, 1), Smooth);

            using var pixel = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            surface.ReadPixels(pixel.Info, pixel.GetPixels(), pixel.RowBytes, 0, 0);
            return pixel.GetPixel(0, 0);
        }

        private static float Blend(byte skin, byte overlay) =>
            1 + (Math.Clamp((skin + 1f) / (overlay + 1f), 0.5f, 1.5f) - 1) * 0.5f;
    }
}
