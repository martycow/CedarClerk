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
    public string RemoteNewDir => $"{RemoteRoot}/app.new";
    public string RemotePrevDir => $"{RemoteRoot}/app.prev";
    public string RemoteStagingDir => $"{RemoteRoot}/staging";
    public string RemoteDataDir => $"{RemoteRoot}/data";

    // ADR-116: under data/ because app/ is replaced wholesale on every deploy, and an installer left
    // there would vanish on the next ordinary release together with the manifest naming it.
    public string RemoteDownloadsDir => $"{RemoteDataDir}/downloads";

    public string ScriptsDir => Path.Combine(RepoRoot, "Scripts");

    // The smoke harness stays a script (ADR-119 decision 3): it owns a server process, an isolated
    // data directory and a browser, which is what a shell script is actually for.
    public string E2eScript => Path.Combine(ScriptsDir, "e2e.ps1");

    // The local layout the build and deploy pipelines walk. Written down once, because "which
    // directory does the Angular output land in" is answered in four places otherwise.
    public string Solution => Path.Combine(RepoRoot, "CedarClerk.sln");
    public string WebDir => Path.Combine(RepoRoot, "cedarclerk-web");
    public string BrowserDist => Path.Combine(WebDir, "dist", "cedarclerk-web", "browser");
    public string PublishDir => Path.Combine(RepoRoot, "publish");
    public string ServerProject => Path.Combine(RepoRoot, "CedarClerk.Server");
    public string DesktopDir => Path.Combine(RepoRoot, "CedarClerk.Desktop");
    public string DesktopServerDir => Path.Combine(DesktopDir, "server");
    public string DesktopDistDir => Path.Combine(DesktopDir, "dist");

    // The cache the release tarball is packed into. Outside the repository on purpose: it survives a
    // clean checkout, which is what lets an interrupted upload resume after a reboot.
    public string PackCacheDir => Path.Combine(Path.GetTempPath(), "cedarclerk-deploy");

    // The public site, derived from the health URL so that pointing the tool at a different
    // deployment moves every link at once rather than three out of four.
    public string PublicBaseUrl =>
        HealthUrl.EndsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            ? HealthUrl[..^"/api/health".Length]
            : HealthUrl;

    public string DownloadUrl => $"{PublicBaseUrl}/downloads";

    // The solution file rather than a script (ADR-119): the pipelines are the tool now, so what a
    // configured repository has to contain is the repository, not a particular .ps1.
    public bool LooksComplete =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(RemoteRoot) &&
        !string.IsNullOrWhiteSpace(RepoRoot) &&
        File.Exists(Solution);
}
