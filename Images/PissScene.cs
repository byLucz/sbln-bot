using SkiaSharp;

namespace sblngavnav6.Images
{
    internal sealed class PissScene
    {
        public const int Width = 480;
        public const int Height = 320;
        public const int Fps = 20;

        private const int Frames = 70;
        private const int StreamStart = 10;
        private const int StreamEnd = 54;
        private const float Gravity = 900f;
        private const float FloorY = 292f;
        private const float AttackerRadius = 62f;
        private const float TargetRadius = 50f;

        private static readonly SKPoint AttackerCenter = new(140, 128);
        private static readonly SKPoint TargetCenter = new(362, 240);

        private readonly SKImage _attacker;
        private readonly SKImage _target;
        private readonly Random _random = new();
        private readonly List<Drop> _drops = [];

        private float _wetness;
        private float _puddle;
        private float _shake;

        public PissScene(SKImage attacker, SKImage target)
        {
            _attacker = attacker;
            _target = target;
        }

        public IEnumerable<SKBitmap> Render()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);

            for (var frame = 0; frame < Frames; frame++)
            {
                var time = frame / (float)Fps;

                if (frame is >= StreamStart and < StreamEnd)
                    Spawn(time);

                Step(1f / Fps);
                Draw(canvas, time, frame);
                canvas.Flush();

                yield return bitmap;
            }
        }

        private SKPoint Origin(float time) =>
            new(AttackerCenter.X + 30, AttackerCenter.Y + AttackerRadius + 6 + MathF.Sin(time * 7) * 3);

        private void Spawn(float time)
        {
            var origin = Origin(time);

            for (var i = 0; i < 7; i++)
            {
                var flight = 0.42f + (float)_random.NextDouble() * 0.12f;
                var aimX = TargetCenter.X + ((float)_random.NextDouble() - 0.5f) * TargetRadius;
                var aimY = TargetCenter.Y + ((float)_random.NextDouble() - 0.5f) * TargetRadius * 0.6f;
                var velocity = new SKPoint((aimX - origin.X) / flight, (aimY - origin.Y - 0.5f * Gravity * flight * flight) / flight);
                var lead = (float)_random.NextDouble() / Fps;

                _drops.Add(new Drop(
                    new SKPoint(origin.X + velocity.X * lead, origin.Y + velocity.Y * lead),
                    new SKPoint(velocity.X, velocity.Y + Gravity * lead),
                    2.5f + (float)_random.NextDouble() * 2f,
                    false));
            }
        }

        private void Step(float dt)
        {
            _shake = Math.Max(0, _shake - dt * 4);

            for (var i = _drops.Count - 1; i >= 0; i--)
            {
                var drop = _drops[i];
                var velocity = new SKPoint(drop.Velocity.X, drop.Velocity.Y + Gravity * dt);
                var position = new SKPoint(drop.Position.X + velocity.X * dt, drop.Position.Y + velocity.Y * dt);

                if (!drop.Splash && SKPoint.Distance(position, TargetCenter) < TargetRadius)
                {
                    _drops.RemoveAt(i);
                    _wetness = Math.Min(1f, _wetness + 0.006f);
                    _shake = 1f;

                    for (var s = 0; s < 2; s++)
                    {
                        var angle = (float)(_random.NextDouble() * Math.PI) + MathF.PI;
                        var speed = 80 + (float)_random.NextDouble() * 140;
                        _drops.Add(new Drop(position, new SKPoint(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed), 1.5f, true));
                    }

                    continue;
                }

                if (position.Y >= FloorY)
                {
                    _drops.RemoveAt(i);
                    _puddle = Math.Min(1f, _puddle + 0.004f);
                    continue;
                }

                _drops[i] = drop with { Position = position, Velocity = velocity };
            }
        }

        private void Draw(SKCanvas canvas, float time, int frame)
        {
            using (var sky = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(0, Height),
                    [new SKColor(38, 24, 66), new SKColor(14, 10, 24)], SKShaderTileMode.Clamp)
            })
                canvas.DrawRect(0, 0, Width, Height, sky);

            using (var floor = new SKPaint { Color = new SKColor(255, 255, 255, 18), IsAntialias = true })
                canvas.DrawOval(new SKRect(20, FloorY - 6, Width - 20, FloorY + 40), floor);

            using (var puddle = new SKPaint { Color = new SKColor(242, 206, 40, (byte)(70 + 90 * _puddle)), IsAntialias = true })
            {
                var width = 40 + 110 * _puddle;
                canvas.DrawOval(new SKRect(TargetCenter.X - width, FloorY - 8, TargetCenter.X + width, FloorY + 10 + 10 * _puddle), puddle);
            }

            DrawShadow(canvas, AttackerCenter.X, AttackerRadius);
            DrawShadow(canvas, TargetCenter.X, TargetRadius);

            var bob = MathF.Sin(time * 7) * 4;
            var turn = 0.82f + 0.18f * MathF.Cos(time * 2.4f);
            DrawAvatar(canvas, _attacker, new SKPoint(AttackerCenter.X, AttackerCenter.Y + bob), AttackerRadius, turn, MathF.Sin(time * 2.4f) * 0.12f);

            var jitter = _shake * 3;
            var target = new SKPoint(
                TargetCenter.X + ((float)_random.NextDouble() - 0.5f) * jitter,
                TargetCenter.Y + ((float)_random.NextDouble() - 0.5f) * jitter);
            DrawAvatar(canvas, _target, target, TargetRadius, 1f, -0.18f);

            using (var tint = new SKPaint { Color = new SKColor(250, 214, 30, (byte)(150 * _wetness)), IsAntialias = true, BlendMode = SKBlendMode.SrcOver })
                canvas.DrawCircle(target, TargetRadius, tint);

            using var dropPaint = new SKPaint { Color = new SKColor(255, 221, 51, 225), IsAntialias = true };
            using var glow = new SKPaint { Color = new SKColor(255, 230, 90, 70), IsAntialias = true };

            foreach (var drop in _drops)
            {
                canvas.DrawCircle(drop.Position, drop.Radius * 2.2f, glow);
                canvas.DrawCircle(drop.Position, drop.Radius, dropPaint);
            }

            if (frame >= StreamEnd)
            {
                using var drip = new SKPaint { Color = new SKColor(255, 221, 51, 200), IsAntialias = true };
                var length = Math.Min(1f, (frame - StreamEnd) / 10f) * 18;
                for (var i = -2; i <= 2; i++)
                    canvas.DrawRoundRect(target.X + i * 14 - 2, target.Y + TargetRadius - 6, 4, 6 + length * (1 - Math.Abs(i) * 0.2f), 2, 2, drip);
            }
        }

        private static void DrawShadow(SKCanvas canvas, float x, float radius)
        {
            using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 90), IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) };
            canvas.DrawOval(new SKRect(x - radius * 0.9f, FloorY - 6, x + radius * 0.9f, FloorY + 8), shadow);
        }

        private static void DrawAvatar(SKCanvas canvas, SKImage image, SKPoint center, float radius, float turn, float tilt)
        {
            canvas.Save();
            canvas.Translate(center.X, center.Y);
            canvas.RotateRadians(tilt);
            canvas.Scale(turn, 1);

            var rect = new SKRect(-radius, -radius, radius, radius);
            canvas.DrawImage(image, rect, ImageTools.Smooth);

            using var shade = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(-radius, 0), new SKPoint(radius, 0),
                    [new SKColor(255, 255, 255, 40), new SKColor(0, 0, 0, (byte)(110 * (1 - turn) + 30))],
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawCircle(0, 0, radius, shade);

            using var ring = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
            canvas.DrawCircle(0, 0, radius, ring);

            canvas.Restore();
        }

        private readonly record struct Drop(SKPoint Position, SKPoint Velocity, float Radius, bool Splash);
    }
}
