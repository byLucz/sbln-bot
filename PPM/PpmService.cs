using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using sblngavnav6.Common;
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
        private const string LogSource = "PPMAN";

        private readonly PpmServerService _mail;
        private readonly PpmPanels _panels;
        private CancellationTokenSource _sweeperCts;
        private Task _sweeperTask = Task.CompletedTask;
        private bool _disposed;

        public PpmService(PpmServerService mail, PpmPanels panels)
        {
            _mail = mail;
            _panels = panels;
        }

        public async Task<(bool ok, PpmMailbox box, string error, bool ready)> CreateAsync(string ownerId, bool permanent, string wanted = null, string wantedPassword = null)
        {
            if (!Global.Vars.Cfg.ppmEnabled) return (false, null, "PPM отключён в конфигурации", false);

            if (CheckLimits(ownerId, permanent) is { } refusal)
                return (false, null, refusal, false);

            string local;

            if (string.IsNullOrWhiteSpace(wanted))
            {
                local = RandomLocalPart();
            }
            else
            {
                local = wanted.Trim().ToLowerInvariant();

                if (ValidateLocalPart(local) is { } complaint)
                    return (false, null, complaint, false);

                if (DataBase.GetActivePpmMailbox($"{local}@{Global.Vars.Cfg.ppmDomain}") is not null)
                    return (false, null, $"подпись **{local}** уже занята", false);
            }

            string email = $"{local}@{Global.Vars.Cfg.ppmDomain}";
            string password;

            if (string.IsNullOrWhiteSpace(wantedPassword))
            {
                password = RandomPassword();
            }
            else
            {
                password = wantedPassword.Trim();

                if (ValidatePassword(password) is { } weak)
                    return (false, null, weak, false);
            }

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
                await LoggingService.LogErrorAsync(LogSource, $"Не удалось записать ящик {email} в БД, откатываю создание", ex);
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
                await LoggingService.LogWarningAsync(LogSource, $"Ящик {email} создан, но почтовик не принял его за {ReadyTimeout.TotalSeconds:0}с");

            return (true, box, null, ready);
        }

        private static readonly string[] Reserved =
            ["postmaster", "admin", "administrator", "root", "abuse", "noreply", "no-reply", "mailer-daemon", "hostmaster", "webmaster"];

        private static string CheckLimits(string ownerId, bool permanent)
        {
            if (permanent)
            {
                var limit = Global.Vars.Cfg.ppmPermanentMax;
                var used = DataBase.CountPpmPermanent(ownerId);

                return used < limit
                    ? null
                    : $"лимит постоянных ящиков исчерпан: {used} из {limit}, удали лишний";
            }

            var perDay = Global.Vars.Cfg.ppmTempPerDay;
            var (count, oldest) = DataBase.CountPpmTempCreated(ownerId, DateTime.UtcNow.AddDays(-1));

            if (count < perDay)
                return null;

            var wait = oldest.HasValue
                ? oldest.Value.AddDays(1) - DateTime.UtcNow
                : TimeSpan.Zero;

            return wait > TimeSpan.Zero
                ? $"лимит временных ящиков исчерпан: {count} за сутки из {perDay}, следующий через {CommonUtils.Time.FormatAge(wait)}"
                : $"лимит временных ящиков исчерпан: {count} за сутки из {perDay}";
        }

        private static string ValidatePassword(string password)
        {
            if (password.Length is < 8 or > 64)
                return "пароль должен быть от 8 до 64 символов";

            if (password.Any(symbol => char.IsWhiteSpace(symbol) || symbol < ' ' || symbol > '~'))
                return "в пароле можно только видимые ASCII-символы без пробелов";

            return null;
        }

        private static string ValidateLocalPart(string local)
        {
            if (local.Length is < 3 or > 32)
                return "подпись должна быть от 3 до 32 символов";

            if (!local.All(symbol => char.IsAsciiLetterLower(symbol) || char.IsAsciiDigit(symbol) || symbol is '.' or '-' or '_'))
                return "в подписи можно только латиницу, цифры, точку, дефис и подчёркивание";

            if (!char.IsAsciiLetterLower(local[0]) && !char.IsAsciiDigit(local[0]))
                return "подпись должна начинаться с буквы или цифры";

            if (local.Contains(".."))
                return "две точки подряд нельзя";

            if (Reserved.Contains(local))
                return $"подпись **{local}** зарезервирована";

            return null;
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
                    LogSource,
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
                if (ok)
                    return;

                throw new InvalidOperationException(output);
            }
            catch (Exception ex)
            {
                await LoggingService.LogCriticalAsync(LogSource, $"Компенсация не удалась, ящик {email} остался без записи в БД", ex);
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
                    var touched = new HashSet<string>();

                    foreach (var (id, email, ownerId) in expired)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var (ok, output) = await _mail.DelAsync(email, cancellationToken);
                        if (ok)
                        {
                            DataBase.MarkPpmMailboxDeleted(id);
                            touched.Add(ownerId);
                        }
                        else
                        {
                            await LoggingService.LogWarningAsync(LogSource, $"Не удалось удалить {email}: {output}");
                        }
                    }

                    foreach (var ownerId in touched)
                        await _panels.RefreshAsync(ownerId).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    await LoggingService.LogErrorAsync(LogSource, "Ошибка sweeper-цикла", ex);
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
