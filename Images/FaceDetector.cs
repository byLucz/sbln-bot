using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace sblngavnav6.Images
{
    internal sealed record Face(SKRect Box, SKPoint[] Points, float Score);

    internal static class FaceDetector
    {
        private const int InputSize = 640;
        private const float ScoreThreshold = 0.6f;
        private const float OverlapThreshold = 0.3f;

        private static readonly int[] Strides = [8, 16, 32];

        private static readonly Lazy<InferenceSession> Session = new(() =>
            new InferenceSession(ImageTools.AssetPath("yunet-2023mar.onnx"),
                new SessionOptions { LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR }));

        private static readonly Lock Gate = new();

        public static IReadOnlyList<Face> Detect(SKBitmap image)
        {
            var scale = Math.Min(InputSize / (float)image.Width, InputSize / (float)image.Height);
            var width = Math.Max(1, (int)(image.Width * scale));
            var height = Math.Max(1, (int)(image.Height * scale));

            using var resized = image.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            var input = new DenseTensor<float>([1, 3, InputSize, InputSize]);
            var tensor = input.Buffer.Span;
            var pixels = resized.GetPixelSpan();
            const int plane = InputSize * InputSize;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = y * resized.RowBytes + x * 4;
                    var index = y * InputSize + x;
                    tensor[index] = pixels[offset + 2];
                    tensor[plane + index] = pixels[offset + 1];
                    tensor[plane * 2 + index] = pixels[offset];
                }
            }

            var outputs = new Dictionary<string, float[]>();

            lock (Gate)
            {
                using var results = Session.Value.Run([NamedOnnxValue.CreateFromTensor("input", input)]);
                foreach (var result in results)
                    outputs[result.Name] = result.AsEnumerable<float>().ToArray();
            }

            var candidates = new List<Face>();

            foreach (var stride in Strides)
            {
                var cls = outputs[$"cls_{stride}"];
                var obj = outputs[$"obj_{stride}"];
                var bbox = outputs[$"bbox_{stride}"];
                var kps = outputs[$"kps_{stride}"];
                var cols = InputSize / stride;

                for (var i = 0; i < cls.Length; i++)
                {
                    var score = MathF.Sqrt(Math.Clamp(cls[i], 0, 1) * Math.Clamp(obj[i], 0, 1));
                    if (score < ScoreThreshold)
                        continue;

                    var row = i / cols;
                    var col = i % cols;

                    var cx = (col + bbox[i * 4]) * stride / scale;
                    var cy = (row + bbox[i * 4 + 1]) * stride / scale;
                    var w = MathF.Exp(bbox[i * 4 + 2]) * stride / scale;
                    var h = MathF.Exp(bbox[i * 4 + 3]) * stride / scale;

                    var points = new SKPoint[5];
                    for (var n = 0; n < 5; n++)
                        points[n] = new SKPoint((kps[i * 10 + n * 2] + col) * stride / scale, (kps[i * 10 + n * 2 + 1] + row) * stride / scale);

                    candidates.Add(new Face(new SKRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2), points, score));
                }
            }

            var faces = new List<Face>();

            foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Score))
            {
                if (faces.All(face => Overlap(face.Box, candidate.Box) < OverlapThreshold))
                    faces.Add(candidate);
            }

            return faces;
        }

        private static float Overlap(SKRect a, SKRect b)
        {
            var intersection = SKRect.Intersect(a, b);
            if (intersection.IsEmpty)
                return 0;

            var shared = intersection.Width * intersection.Height;
            return shared / (a.Width * a.Height + b.Width * b.Height - shared);
        }
    }
}
