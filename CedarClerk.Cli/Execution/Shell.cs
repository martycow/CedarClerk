namespace CedarClerk.Cli.Execution;

// Finding a PowerShell to run the existing scripts with.
//
// pwsh first because that is what the project's scripts are written against and what Marty's shell
// is; Windows PowerShell is the fallback so the tool still works on a machine without pwsh on PATH.
public static class Shell
{
    private static string? _cached;

    public static string PowerShell()
    {
        if (_cached is not null) return _cached;

        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            if (!OnPath(candidate)) continue;
            _cached = candidate;
            return candidate;
        }
        // Reporting "pwsh" when nothing was found gives the caller a runnable error message rather
        // than an empty file name.
        _cached = "pwsh";
        return _cached;
    }

    // -NoProfile keeps a personal profile from printing into output the tool then tries to parse.
    public static string ScriptArgs(string scriptPath, params string[] arguments)
    {
        var parts = new List<string> { "-NoProfile", "-NonInteractive", "-File", $"\"{scriptPath}\"" };
        parts.AddRange(arguments);
        return string.Join(' ', parts);
    }

    public static bool OnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat" }
            : new[] { "" };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory.Trim('"'), exe + extension))) return true;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry is not this tool's problem to solve, only to survive.
                }
            }
        }
        return false;
    }
}
