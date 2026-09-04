using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using sblngavnav5X.Data;
using sblngavnav5X.Services;
using static sblngavnav5X.Data.DataRoots;

namespace sblngavnav5X.PPM
{
    public class PpmMessageView
    {
        public string From { get; set; }
        public string Subject { get; set; }
        public DateTime Date { get; set; }
        public string Body { get; set; }
    }

    // Оркестратор PechkinPostManager: генерация кредов, IMAP-чтение, фоновый sweeper.
    public sealed class PpmService
    {
        private readonly PpmServerService _mail;

        public PpmService(PpmServerService mail)
        {
            _mail = mail;
        }

        // Создаёт ящик (random local-part), вызывает docker exec, пишет в БД.
        // Возвращает (ok, mailbox|null, error).
        public async Task<(bool ok, PpmMailbox box, string error)> CreateAsync(string ownerId, bool permanent)
        {
            string local = RandomLocalPart();
            string email = $"{local}@{Utils.ppmDomain}";
            string password = RandomPassword();

            var (ok, output) = await _mail.AddAsync(email, password);
            if (!ok)
                return (false, null, output);

            DateTime? expiresAt = permanent ? null : DateTime.UtcNow.AddMinutes(Utils.ppmTtlMinutes);
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

        // Ручное удаление: docker exec del + пометка в БД.
        public async Task<(bool ok, string error)> DeleteAsync(PpmMailbox box)
        {
            var (ok, output) = await _mail.DelAsync(box.Email);
            if (!ok)
                return (false, output);
            DataBase.MarkPpmMailboxDeleted(box.Id);
            return (true, null);
        }

        public async Task<List<PpmMessageView>> ReadInboxAsync(PpmMailbox box, int max = 5)
        {
            var result = new List<PpmMessageView>();
            using var client = new ImapClient();

            if (Utils.ppmImapAllowInvalidCert)
                client.ServerCertificateValidationCallback = (_, _, _, _) => true;

            await client.ConnectAsync(Utils.ppmImapHost, Utils.ppmImapPort, SecureSocketOptions.SslOnConnect);
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

        // Фоновый цикл: удаляет протухшие ящики. Переживает рестарт (состояние в БД).
        public Task StartSweeperAsync()
        {
            _ = Task.Run(SweepLoopAsync);
            return Task.CompletedTask;
        }

        private async Task SweepLoopAsync()
        {
            while (true)
            {
                try
                {
                    var expired = DataBase.GetExpiredPpmMailboxes();
                    foreach (var (id, email) in expired)
                    {
                        var (ok, output) = await _mail.DelAsync(email);
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
                catch (Exception ex)
                {
                    await LoggingService.LogErrorAsync("ppm", "Ошибка sweeper-цикла", ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(30));
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
