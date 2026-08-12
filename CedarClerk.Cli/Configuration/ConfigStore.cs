using System.Text.Json;

namespace CedarClerk.Cli.Configuration;

// Loads and saves the config, and guesses sane values on a first run rather than throwing.
//
// The guess matters: the tool ships inside the repository it operates on, so the repository root is
// discoverable by walking up from the executable. A first run that opens a setup wizard with the
// answers already filled in is a very different experience from one that starts with an exception.
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Directory()
    {
        // %APPDATA%\cedar on Windows, ~/.config/cedar elsewhere. SpecialFolder.ApplicationData maps
        // to both, so this is one call rather than an OS switch.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(root, CliConsts.Files.ConfigDirName);
    }

    public static string Path_ => System.IO.Path.Combine(Directory(), CliConsts.Files.ConfigFileName);

    public static bool Exists() => File.Exists(Path_);

    public static CliConfig Load()
    {
        if (!Exists()) return Guess();
        try
        {
            var config = JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(Path_));
            if (config is null) return Guess();
            // A file written by an older build can be missing a field; fill from the guess rather
            // than refusing to start over a value the user never chose.
            if (string.IsNullOrWhiteSpace(config.RepoRoot)) config.RepoRoot = Guess().RepoRoot;
            return config;
        }
        catch (Exception)
        {
            // A corrupt config must not make the tool unusable — the wizard can rewrite it.
            return Guess();
        }
    }

    public static void Save(CliConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory());
        File.WriteAllText(Path_, JsonSerializer.Serialize(config, Json));
    }

    public static CliConfig Guess() => new() { RepoRoot = FindRepoRoot() ?? "" };

    // Walk up from the running executable, then from the working directory. CedarClerk.sln is the
    // marker because it is the one file guaranteed to sit at the root of this repository.
    public static string? FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "CedarClerk.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
