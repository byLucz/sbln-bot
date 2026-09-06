using System.Diagnostics;
using System.Text;
using sblngavnav5X.Data;

namespace sblngavnav5X.PPM
{
    public sealed class PpmServerService
    {
        public Task<(bool ok, string output)> AddAsync(string email, string password)
            => RunSetupAsync("email", "add", email, password);

        public Task<(bool ok, string output)> DelAsync(string email)
            => RunSetupAsync("email", "del", "-y", email);

        private async Task<(bool ok, string output)> RunSetupAsync(params string[] setupArgs)
        {
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return (false, "docker exec timeout (30s)");
            }

            return (proc.ExitCode == 0, sb.ToString().Trim());
        }
    }
}
