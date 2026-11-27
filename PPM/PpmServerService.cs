using sblngavnav6.Data;
using System.Diagnostics;

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

            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                return (false, $"не удалось запустить docker: {ex.Message}");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);
                await Task.WhenAll(stdoutTask, stderrTask);

                var combined = string.Join("\n",
                    new[] { stdoutTask.Result.Trim(), stderrTask.Result.Trim() }
                        .Where(part => part.Length > 0));

                return (proc.ExitCode == 0, combined);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
                return (false, "docker exec timeout (30s)");
            }
        }
    }
}
