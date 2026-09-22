using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using sblngavnav6.Data;
using sblngavnav6.Services;
using static sblngavnav6.Data.DataRoots;

namespace sblngavnav6.PPM
{
    public class PpmMessageView
    {
        public string From { get; set; }
        public string Subject { get; set; }
        public DateTime Date { get; set; }
        public string Body { get; set; }
    }

    public sealed class PpmService : IAsyncDisposable
    {
        private readonly PpmServerService _mail;
        private CancellationTokenSource _sweeperCts;
        private Task _sweeperTask = Task.CompletedTask;
        private bool _disposed;

        public PpmService(PpmServerService mail)
        {
            _mail = mail;
        }

        public async Task<(bool ok, PpmMailbox box, string error)> CreateAsync(string ownerId, bool permanent)
        {
            if (!Global.Vars.Cfg.ppmEnabled) return (false, null, "PPM отключён в конфигурации");
            string local = RandomLocalPart();
            string email = $"{local}@{Global.Vars.Cfg.ppmDomain}";
            string password = RandomPassword();

            var (ok, output) = await _mail.AddAsync(email, password);
            if (!ok)
                return (false, null, output);

            DateTime? expiresAt = permanent ? null : DateTime.UtcNow.AddMinutes(Global.Vars.Cfg.ppmTtlMinutes);
            int id = DataBase.InsertPpmMailbox(email, password, ownerId, expiresAt, permanent);

            return (true, new PpmMailbox
            {
                Id = id,
                Email = email,
                Password = password,
                OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                IsPermanent = permanent
            }, null);
        }

        public async Task<(bool ok, string error)> DeleteAsync(PpmMailbox box)
        {
            if (!Global.Vars.Cfg.ppmEnabled) return (false, "PPM отключён в конфигурации");
            var (ok, output) = await _mail.DelAsync(box.Email);
            if (!ok)
                return (false, output);
            DataBase.MarkPpmMailboxDeleted(box.Id);
            return (true, null);
        }

        public async Task<List<PpmMessageView>> ReadInboxAsync(PpmMailbox box, int max = 5)
        {
            if (!Global.Vars.Cfg.ppmEnabled) throw new InvalidOperationException("PPM отключён в конфигурации");
            var result = new List<PpmMessageView>();
            using var client = new ImapClient();

            if (Global.Vars.Cfg.ppmImapAllowInvalidCert)
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            await client.ConnectAsync(Global.Vars.Cfg.ppmImapHost, Global.Vars.Cfg.ppmImapPort, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(box.Email, box.Password);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly);

            int count = inbox.Count;
            for (int i = count - 1; i >= 0 && result.Count < max; i--)
            {
                var msg = await inbox.GetMessageAsync(i);
                result.Add(new PpmMessageView
                {
                    From = msg.From?.ToString() ?? "(неизвестно)",
                    Subject = string.IsNullOrWhiteSpace(msg.Subject) ? "(без темы)" : msg.Subject,
                    Date = msg.Date.LocalDateTime,
                    Body = msg.TextBody ?? msg.HtmlBody ?? "(пусто)"
                });
            }

            await client.DisconnectAsync(true);
            return result;
        }

        public Task StartSweeperAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Global.Vars.Cfg.ppmEnabled || _sweeperCts != null) return Task.CompletedTask;
            _sweeperCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _sweeperTask = Task.Run(() => SweepLoopAsync(_sweeperCts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            if (_disposed || _sweeperCts == null) return;
            await _sweeperCts.CancelAsync();
            try { await _sweeperTask; }
            catch (OperationCanceledException) when (_sweeperCts.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            try { await StopAsync(); }
            finally
            {
                _disposed = true;
                _sweeperCts?.Dispose();
            }
        }

        private async Task SweepLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var expired = DataBase.GetExpiredPpmMailboxes();
                    foreach (var (id, email) in expired)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (ok, output) = await _mail.DelAsync(email, cancellationToken);
                        if (ok)
                        {
                            DataBase.MarkPpmMailboxDeleted(id);
                            await LoggingService.LogInformationAsync("ppm", $"Удалён протухший ящик {email}");
                        }
                        else
                        {
                            await LoggingService.LogWarningAsync("ppm", $"Не удалось удалить {email}: {output}");
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    await LoggingService.LogErrorAsync("ppm", "Ошибка sweeper-цикла", ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private static string RandomLocalPart()
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sb = new StringBuilder(10);
            for (int i = 0; i < 10; i++)
                sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
            return sb.ToString();
        }

        private static string RandomPassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%*";
            var sb = new StringBuilder(24);
            for (int i = 0; i < 24; i++)
                sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
            return sb.ToString();
        }
    }
}
