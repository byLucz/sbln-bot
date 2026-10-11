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

            var fill = Average(overlay, SKRect.Create(overlay.Width * 0.3f, overlay.Height * 0.02f, overlay.Width * 0.4f, overlay.Height * 0.16f));

            using var padded = Pad(overlay, overlay.Width + padX * 2, overlay.Height + padY * 2, padX, padY, fill);

            var anchors = template.Select(point => new SKPoint(point.X * overlay.Width + padX, point.Y * overlay.Height + padY)).ToArray();
            var overlayTone = Average(overlay, SKRect.Create(overlay.Width, overlay.Height));

            foreach (var face in faces)
            {
                var skinTone = Average(sourceImage, SKRect.Intersect(face.Box, SKRect.Create(source.Width, source.Height)));
                var fit = Fit(anchors, face.Points);
                var box = face.Box;

                using var tone = SKColorFilter.CreateColorMatrix(
                [
                    Blend(skinTone.Red, overlayTone.Red), 0, 0, 0, 0,
                    0, Blend(skinTone.Green, overlayTone.Green), 0, 0, 0,
                    0, 0, Blend(skinTone.Blue, overlayTone.Blue), 0, 0,
                    0, 0, 0, 1, 0
                ]);

                canvas.SaveLayer();

                canvas.Save();
                canvas.RotateDegrees(MathF.Atan2(fit.SkewY, fit.ScaleX) * 180 / MathF.PI, box.MidX, box.MidY);

                using (var shape = FaceShape(box.MidX, box.Top - box.Height * 0.04f, box.Bottom + box.Height * 0.02f, box.Width * 0.54f))
                using (var mask = new SKPaint { Color = SKColors.Black, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, box.Width * 0.05f) })
                    canvas.DrawPath(shape, mask);

                canvas.Restore();

                using (var skin = new SKPaint { Color = fill, BlendMode = SKBlendMode.SrcIn, ColorFilter = tone })
                    canvas.DrawPaint(skin);

                canvas.Save();
                canvas.Concat(fit);

                using (var paint = new SKPaint { BlendMode = SKBlendMode.SrcATop, ColorFilter = tone })
                    canvas.DrawImage(padded, 0, 0, Smooth, paint);

                canvas.Restore();
                canvas.Restore();
            }

            return ImageTools.Jpeg(surface);
        }

        private static SKPath FaceShape(float centerX, float top, float bottom, float radius)
        {
            var widest = top + (bottom - top) * 0.42f;
            var path = new SKPath();

            path.MoveTo(centerX, top);
            path.CubicTo(centerX + radius * 0.56f, top, centerX + radius, widest - (widest - top) * 0.55f, centerX + radius, widest);
            path.CubicTo(centerX + radius, widest + (bottom - widest) * 0.55f, centerX + radius * 0.45f, bottom, centerX, bottom);
            path.CubicTo(centerX - radius * 0.45f, bottom, centerX - radius, widest + (bottom - widest) * 0.55f, centerX - radius, widest);
            path.CubicTo(centerX - radius, widest - (widest - top) * 0.55f, centerX - radius * 0.56f, top, centerX, top);
            path.Close();

            return path;
        }

        private static SKImage Pad(SKImage image, float width, float height, float offsetX, float offsetY, SKColor background)
        {
            using var surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(width), (int)Math.Ceiling(height)));
            var canvas = surface.Canvas;
            var area = SKRect.Create(offsetX, offsetY, image.Width, image.Height);

            canvas.Clear(background);
            canvas.SaveLayer();
            canvas.DrawImage(image, area, Smooth);

            using (var edge = new SKPaint
            {
                BlendMode = SKBlendMode.DstIn,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(area.MidX, area.MidY), Math.Min(area.Width, area.Height) / 2,
                    [SKColors.Black, SKColors.Black, SKColors.Transparent], [0, 0.84f, 1], SKShaderTileMode.Clamp)
            })
                canvas.DrawPaint(edge);

            canvas.Restore();
            return surface.Snapshot();
        }

        private static SKMatrix Fit(SKPoint[] from, SKPoint[] to)
        {
            double fromX = from.Average(point => point.X), fromY = from.Average(point => point.Y);
            double toX = to.Average(point => point.X), toY = to.Average(point => point.Y);
            double dot = 0, cross = 0, norm = 0;

            for (var i = 0; i < from.Length; i++)
            {
                double px = from[i].X - fromX, py = from[i].Y - fromY;
                double qx = to[i].X - toX, qy = to[i].Y - toY;
                dot += px * qx + py * qy;
                cross += px * qy - py * qx;
                norm += px * px + py * py;
            }

            var a = dot / norm;
            var b = cross / norm;

            return new SKMatrix(
                (float)a, (float)-b, (float)(toX - a * fromX + b * fromY),
                (float)b, (float)a, (float)(toY - b * fromX - a * fromY),
                0, 0, 1);
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
