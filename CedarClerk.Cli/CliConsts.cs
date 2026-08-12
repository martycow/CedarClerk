namespace CedarClerk.Cli;

// Strings the tool refers to itself by, plus the handful of remote facts every command shares.
//
// BinaryName is here alone rather than typed out per command on purpose: the product name is still
// an open question (Q-17), and a rename must be one edit rather than a sweep.
public static class CliConsts
{
    public const string BinaryName = "cedar";
    public const string DisplayName = "Cedar Clerk";
    public const string Tagline = "operations console";

    public const string ServiceName = "cedarclerk";
    public const string TunnelServiceName = "cloudflared";

    public const string DefaultHost = "martycow@periwinkle.mooexe.dev";
    public const string DefaultRemoteRoot = "/home/martycow/cedarclerk";
    public const string DefaultHealthUrl = "https://cedarclerk.mooexe.dev/api/health";

    // sysstat samples every 10 minutes (ADR-118), so an hour is six points and does not draw.
    public const int DefaultHistoryHours = 24;

    // The journal holds ~1.5 million lines a day. Every read is bounded; there is no "all" mode.
    public const int DefaultLogLines = 80;
    public const int MaxLogLines = 2000;

    public static class Files
    {
        public const string ConfigFileName = "config.json";
        public const string ConfigDirName = BinaryName;
    }
}
