using SkiaSharp;
using static sblngavnav6.Images.ImageTools;

namespace sblngavnav6.Images
{
    internal sealed class CoinScene
    {
        public const int Width = 360;
        public const int Height = 360;
        public const int Fps = 20;

        private const int FlightFrames = 34;
        private const int SettleFrames = 12;
        private const int HoldFrames = 26;
        private const float Radius = 78f;
        private const float Thickness = 9f;
        private const float GroundY = 250f;
        private const float Lift = 140f;

        private readonly SKImage _heads;
        private readonly SKImage _tails;
        private readonly float _finalAngle;
        private readonly Random _random = new();

        public CoinScene(SKImage heads, SKImage tails, bool headsWins)
        {
            _heads = heads;
            _tails = tails;
            _finalAngle = MathF.PI * (headsWins ? 10 : 11);
        }

        public IEnumerable<SKBitmap> Render()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);

            for (var frame = 0; frame < FlightFrames + SettleFrames + HoldFrames; frame++)
            {
                float angle, height;

                if (frame < FlightFrames)
                {
                    var t = frame / (float)FlightFrames;
                    angle = _finalAngle * (1 - MathF.Pow(1 - t, 1.6f)) - 0.35f * (1 - t);
                    height = Lift * 4 * t * (1 - t);
                }
                else if (frame < FlightFrames + SettleFrames)
                {
                    var t = (frame - FlightFrames) / (float)SettleFrames;
                    var decay = MathF.Exp(-t * 4);
                    angle = _finalAngle + MathF.Sin(t * MathF.PI * 3) * 0.45f * decay;
                    height = MathF.Abs(MathF.Sin(t * MathF.PI * 2)) * 22 * decay;
                }
                else
                {
                    angle = _finalAngle;
                    height = 0;
                }

                Draw(canvas, angle, height, frame);
                canvas.Flush();

                yield return bitmap;
            }
        }

        private void Draw(SKCanvas canvas, float angle, float height, int frame)
        {
            using (var background = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(Width / 2f, GroundY), Width * 0.8f,
                    [new SKColor(30, 92, 58), new SKColor(8, 28, 18)], SKShaderTileMode.Clamp)
            })
                canvas.DrawRect(0, 0, Width, Height, background);

            var lift = height / Lift;
            var radius = Radius * (1 + lift * 0.2f);
            var center = new SKPoint(Width / 2f, GroundY - height - Thickness);
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            var squash = Math.Max(0.04f, MathF.Abs(cos));

            using (var shadow = new SKPaint
            {
                Color = new SKColor(0, 0, 0, (byte)(120 - 70 * lift)),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4 + 10 * lift)
            })
            {
                var spread = Radius * (0.95f - 0.35f * lift);
                canvas.DrawOval(new SKRect(Width / 2f - spread, GroundY + 4 - spread * 0.18f, Width / 2f + spread, GroundY + 4 + spread * 0.18f), shadow);
            }

            var settled = frame >= FlightFrames + SettleFrames;

            if (settled)
            {
                var pulse = 0.5f + 0.5f * MathF.Sin((frame - FlightFrames - SettleFrames) * 0.45f);
                using var glow = new SKPaint
                {
                    Color = new SKColor(255, 214, 80, (byte)(110 + 80 * pulse)),
                    IsAntialias = true,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14 + 6 * pulse)
                };
                canvas.DrawCircle(center, radius + 10, glow);
            }

            var rimOffset = MathF.Abs(sin) * Thickness;

            using (var rim = new SKPaint { Color = new SKColor(150, 104, 24), IsAntialias = true })
            using (var groove = new SKPaint { Color = new SKColor(112, 74, 12), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
            {
                var steps = (int)MathF.Ceiling(MathF.Abs(rimOffset));
                for (var i = steps; i >= 0; i--)
                {
                    var offset = steps == 0 ? 0 : rimOffset * i / steps;
                    var oval = new SKRect(center.X - radius, center.Y + offset - radius * squash, center.X + radius, center.Y + offset + radius * squash);
                    canvas.DrawOval(oval, rim);
                    if (i % 3 == 0)
                        canvas.DrawOval(oval, groove);
                }
            }

            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.Scale(1, squash);

            using (var face = new SKPaint { Color = new SKColor(232, 182, 58), IsAntialias = true })
                canvas.DrawCircle(0, 0, radius, face);

            var inner = radius * 0.84f;
            canvas.DrawImage(cos >= 0 ? _heads : _tails, new SKRect(-inner, -inner, inner, inner), Smooth);

            using (var edge = new SKPaint { Color = new SKColor(255, 226, 120), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
                canvas.DrawCircle(0, 0, inner + 2, edge);

            using (var shine = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(-radius, -radius), new SKPoint(radius, radius),
                    [new SKColor(255, 255, 255, (byte)(30 + 90 * MathF.Abs(sin))), new SKColor(255, 255, 255, 0), new SKColor(0, 0, 0, (byte)(40 + 120 * (1 - squash)))],
                    [0, 0.45f, 1],
                    SKShaderTileMode.Clamp)
            })
                canvas.DrawCircle(0, 0, radius, shine);

            canvas.Restore();

            if (!settled)
                return;

            using var sparkle = new SKPaint { Color = new SKColor(255, 240, 170), IsAntialias = true };

            for (var i = 0; i < 6; i++)
            {
                var spin = (float)(_random.NextDouble() * MathF.PI * 2);
                var distance = radius + 18 + (float)_random.NextDouble() * 40;
                var size = 2 + (float)_random.NextDouble() * 4;
                var point = new SKPoint(center.X + MathF.Cos(spin) * distance, center.Y + MathF.Sin(spin) * distance);

                canvas.DrawRect(point.X - size / 4, point.Y - size, size / 2, size * 2, sparkle);
                canvas.DrawRect(point.X - size, point.Y - size / 4, size * 2, size / 2, sparkle);
            }
        }
    }
}
