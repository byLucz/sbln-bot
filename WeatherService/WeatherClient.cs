using sblngavnav6.Data;
using sblngavnav6.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.Commands
{
    public sealed class WeatherClient
    {
        private const string LogSource = "WETHR";
        private const string NotFound = "такого города не нашлось";
        private const string Unavailable = "сервис погоды недоступен";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly IHttpClientFactory _factory;

        public WeatherClient(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<WeatherResult> GetAsync(string city, CancellationToken cancellationToken = default)
        {
            var apiKey = Global.Vars.Cfg.weatherApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                await LoggingService.LogWarningAsync(LogSource, "Api:WeatherApiKey не задан").ConfigureAwait(false);
                return WeatherResult.Failed(Unavailable);
            }

            city = city.Trim();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            try
            {
                using var client = _factory.CreateClient();

                var current = await LoadAsync(
                    client,
                    WeatherUrl.Current(city, apiKey),
                    AppJsonContext.Default.WeatherCurrent,
                    city,
                    cts.Token).ConfigureAwait(false);

                if (current.Value is null)
                    return WeatherResult.Failed(current.Missing ? NotFound : Unavailable);

                var forecast = await LoadAsync(
                    client,
                    WeatherUrl.Forecast(city, apiKey),
                    AppJsonContext.Default.WeatherForecast,
                    city,
                    cts.Token).ConfigureAwait(false);

                return new WeatherResult(current.Value, forecast.Value, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await LoggingService.LogWarningAsync(LogSource, $"погода для \"{city}\" не ответила за {Timeout.TotalSeconds:0}с").ConfigureAwait(false);
                return WeatherResult.Failed(Unavailable);
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(LogSource, $"запрос погоды для \"{city}\" упал", ex).ConfigureAwait(false);
                return WeatherResult.Failed(Unavailable);
            }
        }

        private static async Task<(T Value, bool Missing)> LoadAsync<T>(
            HttpClient client,
            string url,
            JsonTypeInfo<T> typeInfo,
            string city,
            CancellationToken cancellationToken) where T : class
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, true);

            if (!response.IsSuccessStatusCode)
            {
                await LoggingService.LogWarningAsync(
                    LogSource,
                    $"погода для \"{city}\": ответ {(int)response.StatusCode} {response.ReasonPhrase}").ConfigureAwait(false);

                return (null, false);
            }

            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);

            if (value is null)
                await LoggingService.LogWarningAsync(LogSource, $"погода для \"{city}\": пустой ответ").ConfigureAwait(false);

            return (value, false);
        }
    }
}
