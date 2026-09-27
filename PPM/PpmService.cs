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
        public string Folder { get; set; }
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

        public async Task<(bool ok, PpmMailbox box, string error, bool ready)> CreateAsync(string ownerId, bool permanent)
        {
            if (!Global.Vars.Cfg.ppmEnabled) return (false, null, "PPM отключён в конфигурации", false);
            string local = RandomLocalPart();
            string email = $"{local}@{Global.Vars.Cfg.ppmDomain}";
            string password = RandomPassword();

            var (ok, output) = await _mail.AddAsync(email, password);
            if (!ok)
                return (false, null, output, false);

            DateTime? expiresAt = permanent ? null : DateTime.UtcNow.AddMinutes(Global.Vars.Cfg.ppmTtlMinutes);

            int id;
            try
            {
                id = DataBase.InsertPpmMailbox(email, password, ownerId, expiresAt, permanent);
            }
            catch (Exception ex)
            {
                await LoggingService.LogErrorAsync("ppm", $"Не удалось записать ящик {email} в БД, откатываю создание", ex);
                await CompensateAsync(email);
                return (false, null, "Не удалось сохранить ящик, создание отменено", false);
            }

            var box = new PpmMailbox
            {
                Id = id,
                Email = email,
                Password = password,
                OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                IsPermanent = permanent
            };

            var ready = await WaitUntilLiveAsync(box).ConfigureAwait(false);

            if (!ready)
                await LoggingService.LogWarningAsync("ppm", $"Ящик {email} создан, но почтовик не принял его за {ReadyTimeout.TotalSeconds:0}с");

            return (true, box, null, ready);
        }

        private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ReadyProbeDelay = TimeSpan.FromSeconds(3);

        private async Task<bool> WaitUntilLiveAsync(PpmMailbox box, CancellationToken cancellationToken = default)
        {
            var deadline = DateTimeOffset.UtcNow + ReadyTimeout;

            while (true)
            {
                try
                {
                    using var client = await ConnectAsync(box, cancellationToken).ConfigureAwait(false);
                    await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
                    await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    if (DateTimeOffset.UtcNow + ReadyProbeDelay >= deadline)
                        return false;

                    await Task.Delay(ReadyProbeDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task<ImapClient> ConnectAsync(PpmMailbox box, CancellationToken cancellationToken = default)
        {
            var client = new ImapClient();

            if (Global.Vars.Cfg.ppmImapAllowInvalidCert)
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            try
            {
                await client.ConnectAsync(
                    Global.Vars.Cfg.ppmImapHost,
                    Global.Vars.Cfg.ppmImapPort,
                    SecureSocketOptions.SslOnConnect,
                    cancellationToken).ConfigureAwait(false);

                await client.AuthenticateAsync(box.Email, box.Password, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            return client;
        }

        public async Task<(bool ok, string error)> DeleteAsync(PpmMailbox box)
        {
            if (!Global.Vars.Cfg.ppmEnabled) return (false, "PPM отключён в конфигурации");

            var (ok, output) = await _mail.DelAsync(box.Email);
            if (!ok)
                return (false, output);

            try
            {
                DataBase.MarkPpmMailboxDeleted(box.Id);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync(
                    "ppm",
                    $"Ящик {box.Email} удалён на сервере, но запись id={box.Id} осталась активной в БД, нужна ручная сверка",
                    ex);
                return (false, "Ящик удалён, но запись в БД не обновилась, сообщи администратору");
            }

            return (true, null);
        }

        private async Task CompensateAsync(string email)
        {
            try
            {
                var (ok, output) = await _mail.DelAsync(email);
                if (!ok)
                    await LoggingService.LogCriticalAsync("ppm", $"Компенсация не удалась, ящик {email} остался без записи в БД: {output}");
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync("ppm", $"Компенсация не удалась, ящик {email} остался без записи в БД", ex);
            }
        }

        public async Task<List<PpmMessageView>> ReadInboxAsync(PpmMailbox box, int max = 5)
        {
            if (!Global.Vars.Cfg.ppmEnabled) throw new InvalidOperationException("PPM отключён в конфигурации");
            var result = new List<PpmMessageView>();
            using var client = await ConnectAsync(box).ConfigureAwait(false);

            await CollectAsync(client.Inbox, null, result, max).ConfigureAwait(false);

            foreach (var special in new[] { SpecialFolder.Junk, SpecialFolder.All })
            {
                var folder = TryGetSpecial(client, special);

                if (folder is null || folder.FullName == client.Inbox.FullName)
                    continue;

                await CollectAsync(folder, special is SpecialFolder.Junk ? "спам" : folder.Name, result, max)
                    .ConfigureAwait(false);

                if (special is SpecialFolder.Junk)
                    continue;

                break;
            }

            await client.DisconnectAsync(true);

            return result
                .OrderByDescending(message => message.Date)
                .Take(max)
                .ToList();
        }

        private static IMailFolder TryGetSpecial(ImapClient client, SpecialFolder special)
        {
            try { return client.GetFolder(special); }
            catch (Exception) { return null; }
        }

        private static async Task CollectAsync(IMailFolder folder, string label, List<PpmMessageView> result, int max)
        {
            try
            {
                await folder.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            for (int index = folder.Count - 1, taken = 0; index >= 0 && taken < max; index--, taken++)
            {
                var message = await folder.GetMessageAsync(index).ConfigureAwait(false);

                result.Add(new PpmMessageView
                {
                    From = message.From?.ToString() ?? "(неизвестно)",
                    Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(без темы)" : message.Subject,
                    Date = message.Date.LocalDateTime,
                    Body = message.TextBody ?? message.HtmlBody ?? "(пусто)",
                    Folder = label
                });
            }
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
