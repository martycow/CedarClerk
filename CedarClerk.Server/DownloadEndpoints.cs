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
    /// Registers the static-file branch and the one convenience route. Call it beside the other
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

        app.UseWhen(ctx => !Tenancy.TenantRouting.IsTenantRequest(ctx), downloads =>
            downloads.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(downloadsDir),
            RequestPath = RequestPath,
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = ctx =>
            {
                // The manifest is the only file whose content changes under a fixed name, so it is
                // the only one that must not be cached — a cached copy at the edge means an update
                // that was published hours ago is still invisible. Installers carry their version
                // in the file name and can be cached as hard as anything hashed.
                ctx.Context.Response.Headers.CacheControl =
                    ctx.File.Name.Equals(Manifest, StringComparison.OrdinalIgnoreCase)
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
    }
}
