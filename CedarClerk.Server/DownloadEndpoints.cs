using CedarClerk.Localization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace CedarClerk.Server;

/// <summary>
/// Where the desktop shell gets its updates from (ADR-116).
///
/// **There is no update protocol here to speak of** — `electron-updater`'s generic provider is
/// three files in one folder: `latest.yml` (version + sha512), the installer it names, and a
/// `.blockmap` that lets a client download only the changed chunks. Serving them is ordinary
/// static-file middleware; everything clever lives in the desktop publish step, which puts them
/// there (CedarClerk.Cli/Pipelines/DeployPipeline.cs since ADR-119).
///
/// **The folder is under <c>CEDAR_DATA_DIR</c>, not beside the app**: a deploy replaces `app/`
/// wholesale, so an installer stored there would vanish on the next ordinary release and take
/// `latest.yml` with it, leaving every installed copy pointed at a file that no longer exists.
///
/// **Unauthenticated on purpose.** An installed copy checks for updates before anyone signs in,
/// and the installer itself is what a new machine downloads. Both are public artifacts.
/// </summary>
public static class DownloadEndpoints
{
    private const string RequestPath = "/downloads";
    private const string Manifest = "latest.yml";

    /// <summary>
    /// Registers the static-file branch and the convenience routes. Call it beside the other
    /// <c>UseStaticFiles</c> calls — never in desktop mode, where this server *is* the thing being
    /// updated and the folder would only ever be empty.
    /// </summary>
    public static void UseDesktopDownloads(this WebApplication app, string downloadsDir)
    {
        Directory.CreateDirectory(downloadsDir);
        app.UseDesktopDownloadFiles(downloadsDir);
        app.MapDesktopDownloadRoute(downloadsDir);
    }

    /// <summary>
    /// The files themselves. Skipped on a tenant subdomain: that host serves one account's blog,
    /// and a download mirror is not part of one (T-290). The convenience route below needs no such
    /// guard — <c>UseBlogOnlyHost</c> has already dropped the endpoint by then.
    /// </summary>
    public static void UseDesktopDownloadFiles(this IApplicationBuilder app, string downloadsDir)
    {
        // Neither extension is in the default map, and the static-file middleware answers 404 for a
        // type it cannot name — which would look exactly like "no update was published".
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".yml"] = "text/yaml";
        contentTypes.Mappings[".blockmap"] = "application/octet-stream";
        contentTypes.Mappings[".dmg"] = "application/x-apple-diskimage";
        contentTypes.Mappings[".AppImage"] = "application/octet-stream";

        app.UseWhen(ctx => !Tenancy.TenantRouting.IsTenantRequest(ctx), downloads =>
            downloads.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(downloadsDir),
            RequestPath = RequestPath,
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = ctx =>
            {
                // Each platform manifest changes under a fixed name. A cached copy at the edge means an update
                // that was published hours ago is still invisible. Installers carry their version
                // in the file name and can be cached as hard as anything hashed.
                ctx.Context.Response.Headers.CacheControl =
                    ctx.File.Name.StartsWith("latest", StringComparison.OrdinalIgnoreCase)
                    && ctx.File.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                        ? "no-cache, must-revalidate"
                        : "public, max-age=604800";
            }
        }));

    }

    /// <summary>
    /// A link Marty can hand out that does not change with every release. It reads the version out
    /// of the manifest rather than out of Consts, because the installer is shipped by
    /// `deploy -Desktop` and the server by every deploy: the two versions legitimately differ, and
    /// guessing from the running server's version would 404 exactly when they do.
    /// </summary>
    public static void MapDesktopDownloadRoute(this WebApplication app, string downloadsDir)
    {
        app.MapGet($"{RequestPath}/latest", () =>
        {
            var manifestPath = Path.Combine(downloadsDir, Manifest);
            if (!File.Exists(manifestPath))
                return Results.NotFound(new { error = ErrorMessages.NoDesktopBuildPublished });

            var file = File.ReadLines(manifestPath)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
                ?["path:".Length..].Trim().Trim('\'', '"');

            return string.IsNullOrEmpty(file)
                ? Results.NotFound(new { error = ErrorMessages.NoDesktopBuildPublished })
                : Results.Redirect($"{RequestPath}/{file}");
        });

        app.MapGet($"{RequestPath}/platforms", (HttpContext ctx) =>
        {
            // The list changes under a fixed URL, like the manifests it is read from.
            ctx.Response.Headers.CacheControl = "no-cache, must-revalidate";
            return Results.Ok(new
            {
                platforms = DesktopInstallers.Published(downloadsDir).Select(i => new
                {
                    platform = i.Platform,
                    version = i.Version,
                    url = $"{RequestPath}/latest/{i.Platform}",
                }),
            });
        });

        app.MapGet($"{RequestPath}/latest/{{platform}}", (string platform) =>
            DesktopInstallers.Find(downloadsDir, platform) is { } installer
                ? Results.Redirect($"{RequestPath}/{Uri.EscapeDataString(installer.File)}")
                : Results.NotFound(new { error = ErrorMessages.NoDesktopBuildPublished }));
    }
}

public sealed record DesktopInstaller(string Platform, string Version, string File);

/// <summary>
/// Which installers are published, read from the same `electron-updater` manifests the installed
/// copies update from — one per platform, so a platform is offered exactly when its manifest and
/// the file it names are both in the folder.
/// </summary>
public static class DesktopInstallers
{
    // A manifest's `path:` is what the updater wants, which on macOS is the ZIP; a person
    // downloading by hand wants the DMG the same manifest lists under `files:`.
    private static readonly (string Platform, string Manifest, string[] Extensions)[] Platforms =
    [
        ("windows", "latest.yml", [".exe"]),
        ("mac", "latest-mac.yml", [".dmg", ".zip"]),
        ("linux", "latest-linux.yml", [".AppImage"]),
    ];

    public static IReadOnlyList<DesktopInstaller> Published(string downloadsDir) =>
        Platforms.Select(p => Read(downloadsDir, p.Platform, p.Manifest, p.Extensions))
            .OfType<DesktopInstaller>().ToList();

    public static DesktopInstaller? Find(string downloadsDir, string platform) =>
        Published(downloadsDir).FirstOrDefault(i => i.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));

    private static DesktopInstaller? Read(string downloadsDir, string platform, string manifest, string[] extensions)
    {
        var manifestPath = Path.Combine(downloadsDir, manifest);
        if (!File.Exists(manifestPath)) return null;

        var lines = File.ReadLines(manifestPath).Select(line => line.Trim()).ToList();
        var version = Value(lines, "version:").FirstOrDefault();
        var candidates = Value(lines, "- url:").Concat(Value(lines, "path:"))
            // A manifest is uploaded by hand; a name that is not a bare file name must not become a path.
            .Where(name => name.Length > 0 && Path.GetFileName(name) == name && File.Exists(Path.Combine(downloadsDir, name)))
            .ToList();

        var file = extensions
            .Select(ext => candidates.FirstOrDefault(name => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(name => name is not null);

        return string.IsNullOrEmpty(version) || file is null ? null : new DesktopInstaller(platform, version, file);
    }

    private static IEnumerable<string> Value(IEnumerable<string> lines, string key) =>
        lines.Where(line => line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            .Select(line => line[key.Length..].Trim().Trim('\'', '"'));
}
