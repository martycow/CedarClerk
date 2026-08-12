namespace CedarClerk.Cli.Configuration;

// What the tool needs to know to reach the machine and the repository. Paths and host names only —
// ADR-118 keeps authentication with the ssh-agent, so there is nothing secret to store and nothing
// secret to leak from this file.
public sealed class CliConfig
{
    public string Host { get; set; } = CliConsts.DefaultHost;

    // Empty means "whatever ssh already resolves" — an agent key, or an IdentityFile in ~/.ssh/config.
    public string IdentityFile { get; set; } = "";

    public string RemoteRoot { get; set; } = CliConsts.DefaultRemoteRoot;
    public string HealthUrl { get; set; } = CliConsts.DefaultHealthUrl;
    public string RepoRoot { get; set; } = "";

    public string RemoteAppDir => $"{RemoteRoot}/app";
    public string RemoteDataDir => $"{RemoteRoot}/data";
    public string ScriptsDir => Path.Combine(RepoRoot, "Scripts");

    public string DeployScript => Path.Combine(ScriptsDir, "deploy.ps1");
    public string BuildScript => Path.Combine(ScriptsDir, "build.ps1");
    public string TestScript => Path.Combine(ScriptsDir, "test.ps1");

    public bool LooksComplete =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(RemoteRoot) &&
        !string.IsNullOrWhiteSpace(RepoRoot) &&
        File.Exists(DeployScript);
}
