namespace CedarClerk.Cli.Execution;

// Finding the programs the pipelines run.
//
// pwsh first because that is what the project's remaining scripts are written against and what
// Marty's shell is; Windows PowerShell is the fallback so the tool still works without pwsh on PATH.
public static class Shell
{
    private static string? _cachedPowerShell;
    private static string? _cachedNpm;

    public static string PowerShell()
    {
        if (_cachedPowerShell is not null) return _cachedPowerShell;

        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            if (Find(candidate) is null) continue;
            _cachedPowerShell = candidate;
            return candidate;
        }
        // Reporting "pwsh" when nothing was found gives the caller a runnable error message rather
        // than an empty file name.
        _cachedPowerShell = "pwsh";
        return _cachedPowerShell;
    }

    // npm has to be started by its FULL path, and this is not tidiness — it is a bug that cost a
    // green test run (12.08.2026). npm.cmd locates its own JavaScript through %~dp0, and a batch file
    // launched by bare name through CreateProcess gets %0 without a directory, so cmd.exe resolves
    // %~dp0 against the *working* directory instead. The result is
    // "Cannot find module <cwd>\node_modules\npm\bin\npm-cli.js" — which reads like a broken project
    // and is in fact a broken launch. It never happened while npm was being started by PowerShell.
    public static string Npm()
    {
        if (_cachedNpm is not null) return _cachedNpm;
        _cachedNpm = Find("npm") ?? (OperatingSystem.IsWindows() ? "npm.cmd" : "npm");
        return _cachedNpm;
    }

    // -NoProfile keeps a personal profile from printing into output the tool then tries to parse.
    public static string ScriptArgs(string scriptPath, params string[] arguments)
    {
        var parts = new List<string> { "-NoProfile", "-NonInteractive", "-File", $"\"{scriptPath}\"" };
        parts.AddRange(arguments);
        return string.Join(' ', parts);
    }

    public static bool OnPath(string exe) => Find(exe) is not null;

    // The full path to an executable on PATH, or null.
    //
    // The extension order is load-bearing on Windows. Node ships BOTH `npm` (a shell script, for
    // git-bash) and `npm.cmd` in the same directory, and CreateProcess cannot run the first one — so
    // trying the bare name first finds a file that exists and refuses to start, which is worse than
    // finding nothing. A name that already carries an extension is tried as written first instead.
    public static string? Find(string exe)
    {
        var extensions = OperatingSystem.IsWindows()
            ? Path.HasExtension(exe)
                ? new[] { "", ".exe", ".cmd", ".bat" }
                : new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), exe + extension);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not this tool's problem to solve, only to survive.
                }
            }
        }
        return null;
    }
}
