using System.Diagnostics;
using System.Text;
using CedarClerk.Cli.Configuration;

namespace CedarClerk.Cli.Execution;

// The real one: starts processes, and reaches the droplet by shelling out to ssh.
//
// ssh rather than a library (ADR-118) — the keys, agent and known_hosts are already configured and
// already work, and every remote command stays copy-pasteable out of --dry-run.
public sealed class ProcessCommandRunner : ICommandRunner
{
    private readonly CliConfig _config;

    public ProcessCommandRunner(CliConfig config) => _config = config;

    // BatchMode makes a key problem fail immediately instead of hanging on a hidden password
    // prompt — the same reasoning, and the same flags, as deploy.ps1.
    private string SshArgs(string command)
    {
        var parts = new List<string>
        {
            "-o", "BatchMode=yes",
            "-o", "ConnectTimeout=20",
            "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=6"
        };
        if (!string.IsNullOrWhiteSpace(_config.IdentityFile))
        {
            parts.Add("-i");
            parts.Add(Quote(_config.IdentityFile));
        }
        parts.Add(_config.Host);
        parts.Add(Quote(command));
        return string.Join(' ', parts);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    public Task<CommandResult> RunRemoteAsync(string command, CancellationToken ct) =>
        RunAsync("ssh", SshArgs(command), null, null, ct);

    public Task<CommandResult> RunLocalAsync(string exe, string args, CancellationToken ct) =>
        RunAsync(exe, args, null, null, ct);

    public Task<CommandResult> RunLocalStreamingAsync(
        string exe, string args, string? workingDirectory, Action<string> onLine, CancellationToken ct) =>
        RunAsync(exe, args, workingDirectory, onLine, ct);

    public Task<CommandResult> RunRemoteStreamingAsync(string command, Action<string> onLine, CancellationToken ct) =>
        RunAsync("ssh", SshArgs(command), null, onLine, ct);

    // One attempt at pushing the tail of a local file into a remote command's stdin. Not scp: scp
    // restarts a dropped transfer from byte zero, and this file has kept dying at 90% (ADR-113).
    //
    // A broken pipe is an ordinary outcome here, not an exception worth propagating — the caller
    // decides what to do by asking the far side how many bytes actually arrived, which is the only
    // answer that cannot be optimistic.
    public async Task<CommandResult> StreamFileToRemoteAsync(
        string localPath, long offset, string remoteCommand, Action<long> onSent, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var info = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = SshArgs(remoteCommand),
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = info };
        var stderr = new StringBuilder();

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new CommandResult(127, "", ex.Message, watch.Elapsed);
        }

        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.Append(e.Data).Append('\n'); };
        process.BeginErrorReadLine();

        var broke = false;
        var sent = offset;

        try
        {
            await using var file = File.OpenRead(localPath);
            file.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[512 * 1024];
            while (true)
            {
                var read = await file.ReadAsync(buffer, ct);
                if (read <= 0) break;

                await process.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), ct);
                sent += read;
                onSent(sent);
            }
            await process.StandardInput.BaseStream.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (Exception)
        {
            // The connection going away mid-write is the normal failure of this path.
            broke = true;
        }
        finally
        {
            try { process.StandardInput.Close(); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
        }

        var code = broke ? Math.Max(1, process.ExitCode) : process.ExitCode;
        return new CommandResult(code, "", stderr.ToString(), watch.Elapsed);
    }

    // UseShellExecute is what gives the new process a console of its own instead of inheriting ours,
    // which is the entire point: two programs reading this terminal's keyboard would fight over it.
    public Task<CommandResult> LaunchDetachedAsync(string exe, string args, string? workingDirectory, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = false
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) info.WorkingDirectory = workingDirectory;

        try
        {
            using var process = Process.Start(info);
            return Task.FromResult(CommandResult.Empty(process is null ? 127 : 0));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CommandResult(127, "", ex.Message, TimeSpan.Zero));
        }
    }

    private static async Task<CommandResult> RunAsync(
        string exe, string args, string? workingDirectory, Action<string>? onLine, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var info = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory)) info.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.Append(e.Data).Append('\n');
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.Append(e.Data).Append('\n');
            // Some tools report progress on stderr; the grid would miss half its ticks otherwise.
            onLine?.Invoke(e.Data);
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // A missing ssh or pwsh is a configuration problem, not a crash — report it as output
            // so the caller can show it in the same place it shows every other failure.
            return new CommandResult(127, "", ex.Message, watch.Elapsed);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString(), watch.Elapsed);
    }
}
