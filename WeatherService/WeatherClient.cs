using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using sblngavnav6.Data;
using sblngavnav6.Services;

namespace sblngavnav6.Commands
{
    public sealed class WeatherClient
    {
        private const string LogSource = "WETHR";
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly IHttpClientFactory _factory;

        public WeatherClient(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<WeatherResult> GetAsync(string city, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(city))
                return WeatherResult.Failed("не указан город");

            var apiKey = Global.Vars.Cfg.weatherApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
                return WeatherResult.Failed("ключ OpenWeatherMap не настроен");

            city = city.Trim();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            try
            {
                using var client = _factory.CreateClient();

                var (current, error) = await LoadAsync(
                    client,
                    WeatherUrl.Current(city, apiKey),
                    AppJsonContext.Default.WeatherCurrent,
                    cts.Token).ConfigureAwait(false);

                if (current is null)
                    return WeatherResult.Failed(error);

                var (forecast, _) = await LoadAsync(
                    client,
                    WeatherUrl.Forecast(city, apiKey),
                    AppJsonContext.Default.WeatherForecast,
                    cts.Token).ConfigureAwait(false);

                return new WeatherResult(current, forecast, null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return WeatherResult.Failed("сервис погоды не ответил вовремя");
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync(LogSource, $"запрос погоды для \"{city}\" упал", ex).ConfigureAwait(false);
                return WeatherResult.Failed("сервис погоды недоступен");
            }
        }

        private static async Task<(T Value, string Error)> LoadAsync<T>(
            HttpClient client,
            string url,
            JsonTypeInfo<T> typeInfo,
            CancellationToken cancellationToken) where T : class
        {
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (null, Describe(response.StatusCode, response.ReasonPhrase));

            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken).ConfigureAwait(false);

            return value is null ? (null, "сервис погоды вернул пустой ответ") : (value, null);
        }

        private static string Describe(HttpStatusCode code, string reason) => code switch
        {
            HttpStatusCode.NotFound => "такого города не нашлось",
            HttpStatusCode.Unauthorized => "ключ OpenWeatherMap отклонён",
            HttpStatusCode.TooManyRequests => "лимит запросов к погоде исчерпан",
            _ => string.IsNullOrWhiteSpace(reason) ? $"сервис погоды ответил {(int)code}" : $"сервис погоды ответил {(int)code} ({reason})"
        };
    }
}
