using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Net.Http.Headers;

namespace sblngavnav6.Audio8
{
    internal readonly record struct Audio8VoteAudio(string Path, TimeSpan AnnounceDuration, TimeSpan CountdownDuration);

    internal sealed class Audio8TtsComposer
    {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const int CountdownSeconds = 10;
        private const float BackgroundGain = 0.4f;
        private const int MinTtsBytes = 1000;

        private static readonly WaveFormat TargetFormat = new(SampleRate, 16, Channels);
        private static readonly TimeSpan StaleAudioLifetime = TimeSpan.FromHours(2);

        public async Task<Audio8VoteAudio> BuildAsync(
            IReadOnlyList<string> items,
            string winner,
            HttpClient http,
            CancellationToken cancellationToken = default)
        {
            var audioDir = ResolveAudioDirectory();
            Directory.CreateDirectory(audioDir);
            CleanupStale(audioDir);

            var path = Path.Combine(audioDir, $"vote_{Guid.NewGuid():N}.wav");
            var background = new BackgroundLoop(LoadBackground());
            var announceDuration = TimeSpan.Zero;

            using (var writer = new WaveFileWriter(path, TargetFormat))
            {
                announceDuration += await SpeakAsync(writer, "Такие варики").ConfigureAwait(false);

                for (var i = 0; i < items.Count; i++)
                    announceDuration += await SpeakAsync(writer, $"Вариант {i + 1}. {items[i]}").ConfigureAwait(false);

                announceDuration += await SpeakAsync(writer, "Голосование началось").ConfigureAwait(false);

                WritePcm(background.Take(CountdownSeconds * SampleRate * Channels), writer);

                var winnerSamples = await FetchTtsSamplesAsync(http, $"Я выбираааю: {winner}", cancellationToken).ConfigureAwait(false);
                WritePcm(winnerSamples, writer);
            }

            return new Audio8VoteAudio(path, announceDuration, TimeSpan.FromSeconds(CountdownSeconds));

            async Task<TimeSpan> SpeakAsync(WaveFileWriter writer, string text)
            {
                var samples = await FetchTtsSamplesAsync(http, text, cancellationToken).ConfigureAwait(false);
                WritePcm(background.Mix(samples, BackgroundGain), writer);
                return TimeSpan.FromSeconds((double)samples.Length / Channels / SampleRate);
            }
        }

        private static string ResolveAudioDirectory() =>
            Environment.GetEnvironmentVariable("SBLN_AUDIO_DIR") ?? Path.Combine(AppContext.BaseDirectory, "audio");

        private static void CleanupStale(string audioDir)
        {
            foreach (var file in Directory.GetFiles(audioDir, "vote_*.wav"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - StaleAudioLifetime)
                        File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        private static float[] LoadBackground()
        {
            var path = FindCountdown(Path.Combine(AppContext.BaseDirectory, "audio"));
            if (path is null)
                return [];

            using var stream = File.OpenRead(path);
            return ReadAll(DecodeMp3(stream).Provider);
        }

        private static string FindCountdown(string audioDir)
        {
            var direct = Path.Combine(audioDir, "countdown.mp3");
            if (File.Exists(direct))
                return direct;

            var directory = Directory.GetParent(audioDir);
            while (directory != null)
            {
                foreach (var folder in new[] { "audio", "Audio" })
                {
                    var candidate = Path.Combine(directory.FullName, folder, "countdown.mp3");
                    if (File.Exists(candidate))
                        return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static async Task<float[]> FetchTtsSamplesAsync(HttpClient http, string text, CancellationToken cancellationToken)
        {
            var url = "https://api.flowery.pw/v1/tts" +
                      "?voice=Aleksandr&translate=false&silence=0&audio_format=mp3&playback_rate=100" +
                      $"&text={Uri.EscapeDataString(text)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse("sbln-bot/6.0"));

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new HttpRequestException($"FloweryTTS {(int)response.StatusCode}: {body}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length < MinTtsBytes)
                throw new InvalidDataException($"TTS слишком мало байт ({bytes.Length})");

            using var stream = new MemoryStream(bytes);
            return ReadAll(DecodeMp3(stream).Provider);
        }

        private static (ISampleProvider Provider, TimeSpan Duration) DecodeMp3(Stream mp3)
        {
            using var mpeg = new NLayer.MpegFile(mp3);

            var sourceRate = mpeg.SampleRate;
            var sourceChannels = mpeg.Channels;
            var samples = new List<float>(sourceRate * sourceChannels * 6);
            var buffer = new float[4096];

            int read;
            while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
                samples.AddRange(buffer.AsSpan(0, read));

            var duration = TimeSpan.FromSeconds((double)samples.Count / sourceChannels / sourceRate);

            var raw = new byte[samples.Count * 4];
            Buffer.BlockCopy(samples.ToArray(), 0, raw, 0, raw.Length);

            var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(sourceRate, sourceChannels);
            ISampleProvider provider = new RawSourceWaveStream(new MemoryStream(raw), sourceFormat).ToSampleProvider();

            if (sourceRate != SampleRate)
                provider = new WdlResamplingSampleProvider(provider, SampleRate);

            if (sourceChannels == 1)
                provider = new MonoToStereoSampleProvider(provider);

            return (provider, duration);
        }

        private static float[] ReadAll(ISampleProvider provider)
        {
            var samples = new List<float>();
            var buffer = new float[4096];

            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
                samples.AddRange(buffer.AsSpan(0, read));

            return samples.ToArray();
        }

        private static void WritePcm(float[] samples, WaveFileWriter writer)
        {
            var pcm = new byte[samples.Length * 2];

            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)Math.Clamp((int)(samples[i] * short.MaxValue), short.MinValue, short.MaxValue);
                pcm[i * 2] = (byte)value;
                pcm[i * 2 + 1] = (byte)(value >> 8);
            }

            writer.Write(pcm, 0, pcm.Length);
        }

        private sealed class BackgroundLoop
        {
            private readonly float[] _samples;
            private int _offset;

            public BackgroundLoop(float[] samples) => _samples = samples ?? [];

            public float[] Mix(float[] samples, float gain)
            {
                if (_samples.Length == 0)
                    return samples;

                var mixed = new float[samples.Length];
                for (var i = 0; i < samples.Length; i++)
                    mixed[i] = Math.Clamp(samples[i] + _samples[(_offset + i) % _samples.Length] * gain, -1f, 1f);

                Advance(samples.Length);
                return mixed;
            }

            public float[] Take(int length)
            {
                var slice = new float[length];

                if (_samples.Length > 0)
                {
                    for (var i = 0; i < length; i++)
                        slice[i] = _samples[(_offset + i) % _samples.Length];

                    Advance(length);
                }

                return slice;
            }

            private void Advance(int count)
            {
                if (_samples.Length > 0)
                    _offset = (_offset + count) % _samples.Length;
            }
        }
    }
}
