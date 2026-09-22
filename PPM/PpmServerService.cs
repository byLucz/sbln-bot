using System.Diagnostics;
using System.Text;
using sblngavnav6.Data;

namespace sblngavnav6.PPM
{
    public sealed class PpmServerService
    {
        public Task<(bool ok, string output)> AddAsync(string email, string password, CancellationToken cancellationToken = default)
            => RunSetupAsync(cancellationToken, "email", "add", email, password);

        public Task<(bool ok, string output)> DelAsync(string email, CancellationToken cancellationToken = default)
            => RunSetupAsync(cancellationToken, "email", "del", "-y", email);

        private async Task<(bool ok, string output)> RunSetupAsync(CancellationToken cancellationToken, params string[] setupArgs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(Global.Vars.Cfg.ppmContainer);
            psi.ArgumentList.Add("setup");
            foreach (var a in setupArgs)
                psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                return (false, $"не удалось запустить docker: {ex.Message}");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
                return (false, "docker exec timeout (30s)");
            }

            return (proc.ExitCode == 0, sb.ToString().Trim());
        }
    }
}
