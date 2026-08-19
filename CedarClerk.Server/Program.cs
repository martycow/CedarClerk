using System.Globalization;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Email;
using CedarClerk.Server.Modules.Agent;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Publishing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Quartz;

const int passwordRequiredLength = 8;

var builder = WebApplication.CreateBuilder(args);

// Kestrel's default (~28.6MB) is below even the .cedar import cap (50MB) — a bulk Markdown
// import (Notion-shaped .zip, see ADR-026) can run to ~200MB. Raised globally rather than
// per-endpoint since this is a small self-hosted server, not a shared multi-tenant Kestrel
// instance with a reason to keep the default low.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 210 * 1024 * 1024);

#region Agent mode (ADR-117)
// The desktop shell runs this same executable with Cedar:Agent:Enabled, and in that role it is not a
// Cedar Clerk installation at all — it is a reader of one machine's disk. So it exits here, before
// anything below exists: no data directory, no SQLite, no migrations, no Identity, no Quartz, no bot,
// no SPA, no landing page, no /api/*.
//
// An early return rather than a pile of `if` around the rest, and the difference is not tidiness:
// this way agent mode cannot *accidentally* gain a capability that gets added below later. A hosted
// Cedar Clerk and a filesystem agent have almost nothing in common but the assembly they ship in, and
// the code should say so.
if (AgentEndpoints.IsAgent(builder.Configuration))
{
    builder.Services.AddSingleton<AgentScanService>();
    builder.Services.AddSingleton<AgentGrants>();

    var agent = builder.Build();
    agent.MapAgentEndpoints();
    agent.Logger.LogInformation("Cedar Clerk agent {Version} — filesystem only, no database", Consts.CurrentVersion);

    // Loopback only, and not because a firewall might catch the rest: this process answers questions
    // about the contents of somebody's disk, and it has no business being reachable from the network
    // even in principle. The shell passes a free port in Cedar:Urls.
    agent.Run(builder.Configuration[Consts.General.UrlsCfg] ?? Consts.URLs.Localhost);
    return;
}
#endregion

#region Paths
var dataDir = Environment.GetEnvironmentVariable(Consts.DataDirectoryKey);
if (dataDir == null)
{
    dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Environment.SetEnvironmentVariable(Consts.DataDirectoryKey, dataDir);
}

var mediaDir = Path.Combine(dataDir, "media");
var dbPath = Path.Combine(dataDir, Consts.DbFileName);
// ADR-058 — where a zip too large for Cloudflare's edge (~100MB) is scp'd for the local-only
// import bypass. Under dataDir, which is where everything worth keeping lives.
var importTmpDir = Path.Combine(dataDir, "import-tmp");
// T-074 — the keys that decrypt every auth cookie. ASP.NET's default location
// (~/.aspnet/DataProtection-Keys) is outside dataDir, so no backup of the data directory would have
// included them and a host rebuild would have signed everyone out irrecoverably. That is also why
// they had to be carried by hand during the DigitalOcean move (ADR-114).
var dataProtectionKeysDir = Path.Combine(dataDir, "dataprotection-keys");
// T-140 — generated asset thumbnails. Under dataDir, never beside the source: the folder being
// indexed is somebody's game project, usually under version control, and writing into it would be
// both a surprise and a diff.
var thumbnailsDir = Path.Combine(dataDir, "thumbs");
// ADR-116 — the desktop installer and the electron-updater manifest that points at it. Under
// dataDir because a deploy replaces app/ wholesale; the directory itself is created only where it
// is actually served (never in desktop mode), see DownloadEndpoints.
var downloadsDir = Path.Combine(dataDir, "downloads");

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(mediaDir);
Directory.CreateDirectory(importTmpDir);
Directory.CreateDirectory(dataProtectionKeysDir);
Directory.CreateDirectory(thumbnailsDir);
#endregion

#region Services
builder.Services.AddDbContext<CedarDbContext>(dbContextBuilder => dbContextBuilder.UseSqlite($"Data Source={dbPath}"));

// ADR-115 — every DateTime on the wire carries its Z. SQLite loses DateTimeKind, so values that are
// UTC in fact came back Unspecified and serialized without a suffix, which a browser reads as local
// time. One converter instead of remembering a helper in each new component.
builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
{
    jsonOptions.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
    jsonOptions.SerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
});

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
    .SetApplicationName(Consts.DataProtectionApplicationName);

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = passwordRequiredLength;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<CedarDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.MaxAge = Consts.AuthCookieLifetime;
    // Without this the ticket inside the cookie expires after Identity's default 14 days while the
    // cookie itself lives 30 — the shorter one wins and looks like a random logout (T-062).
    o.ExpireTimeSpan = Consts.AuthCookieLifetime;
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton(new MediaPaths(mediaDir));
builder.Services.AddSingleton(new ImportTmpPaths(importTmpDir));
builder.Services.AddSingleton<AiJobService>();
// T-140 — where uploaded asset previews are kept. This installation no longer *makes* them (ADR-117:
// it cannot see the files), it stores what the desktop agent sent.
builder.Services.AddSingleton(new ThumbnailPaths(thumbnailsDir));
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());
builder.Services.AddHttpClient(); // named clients used by billing (Stripe), translation providers, and email
builder.Services.AddSingleton<ResendEmailProvider>();
// T-084 — encrypts per-tenant social credentials with the DataProtection key ring that already
// lives under CEDAR_DATA_DIR (T-074). Singleton: it holds one derived protector and nothing else.
builder.Services.AddSingleton<PublishTargetSecrets>();
// T-023/T-064 — signs the private-post access cookie and mints a reader's own link token.
builder.Services.AddSingleton<PrivateAccess>();
// T-085 — Telegram is the first IPublishTarget. Scoped, because it writes through the same
// CedarDbContext as whoever called it: the export endpoint and the Quartz job both save their own
// rows in the same unit of work, and a second context would split that in half.
builder.Services.AddScoped<TelegramPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<TelegramPublishTarget>());
// T-089 — Bluesky. Registered unconditionally: it needs no application-level key of ours (each
// tenant brings their own app password), so there is nothing to be configured before it works.
builder.Services.AddScoped<BlueskyPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<BlueskyPublishTarget>());
// T-110 — X. Also unconditional: with no Cedar:X:ClientId configured the connect endpoint answers
// 501 and no target row can exist, so PublishAsync is unreachable rather than broken.
builder.Services.AddScoped<XPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<XPublishTarget>());
// T-090 — singleton because it outlives any one request: an author's browser can close the moment
// after pressing Publish, and the send has to carry on without it.
builder.Services.AddSingleton<PublishJobRunner>();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("PublishDueScheduledPosts");
    q.AddJob<PublishDueScheduledPostsJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));

    // Daily channel members count snapshot
    var statsJobKey = new JobKey("SnapshotChannelStats");
    q.AddJob<SnapshotChannelStatsJob>(opts => opts.WithIdentity(statsJobKey));
    q.AddTrigger(t => t.ForJob(statsJobKey).WithCronSchedule("0 0 4 * * ?"));

    // T-090 — the durable publish queue's backstop.
    var publishJobKey = new JobKey("RunPublishJobs");
    q.AddJob<RunPublishJobsJob>(opts => opts.WithIdentity(publishJobKey));
    q.AddTrigger(t => t.ForJob(publishJobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(15).RepeatForever()));

    // Hourly check if the paid plan is lapsed
    var downgradeJobKey = new JobKey("DowngradeExpiredPlans");
    q.AddJob<DowngradeExpiredPlansJob>(opts => opts.WithIdentity(downgradeJobKey));
    q.AddTrigger(t => t.ForJob(downgradeJobKey).WithSimpleSchedule(s => s.WithIntervalInHours(1).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
#endregion

#region Application
var app = builder.Build();

// Any unhandled exception on any endpoint used to fall through to ASP.NET Core's default
// behavior — a bare 500 with no body at all — which left the frontend's httpErrorMessage()
// with nothing to show but a generic fallback string. Now every failure gets a real `{error}`
// body (and a full server-side log with stack trace) instead of silently going dark. See ADR
// in docs/DECISIONS.md.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is not null)
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException")
            .LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = ex is null ? "Unexpected server error" : $"{ex.GetType().Name}: {ex.Message}" });
}));

// index.html must never be cached without revalidation. It carries the hashed names of every JS/CSS
// bundle, so a stale copy pins the browser to a build that no longer exists on disk — and because
// Cloudflare independently caches the hashed assets for 4h, the old bundle is still being served
// from the edge after the deploy deleted it, which makes a whole deployed release invisible to
// anyone who had visited before. (Found 31.07.2026: a freshly deployed route kept resolving to the
// SPA's catch-all in Marty's browser while a private window loaded it fine. The origin was sending
// no Cache-Control and no ETag on index.html at all — only Last-Modified, which lets a browser
// apply heuristic freshness and skip asking us entirely.)
//
// The hashed assets themselves are deliberately left alone: their names change with their content,
// so caching them hard is correct and is the reason index.html is the only file that must not be.
var indexNoCache = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
};

// T-009 — the landing answers "/" for visitors who are not signed in; a signed-in request falls
// straight through to the SPA below, which is why this is middleware and not a mapped endpoint.
// The blog host is excluded by name: it has its own "/" — its index — and the first version of
// this middleware quietly replaced it with a marketing page.
//
// **Unconditional since ADR-117.** The desktop used to switch this off with `Cedar:Desktop`, because
// greeting the author with a pricing page was the first thing Marty saw on the first real launch
// (10.08.2026). The shell now opens `/projects` instead of `/`, so the landing is never on its path —
// the problem is solved by which address the window asks for rather than by a flag, and one fewer
// flag is one fewer thing that can be set wrong on a public server.
app.UseLanding(builder.Configuration[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost);

app.UseDefaultFiles();
app.UseStaticFiles(indexNoCache);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaDir),
    RequestPath = "/media"
});

// ADR-116 — updates for the desktop shell. Unconditional since ADR-117: the process that used to
// need this switched off is now an agent, and an agent leaves this file before reaching here.
app.UseDesktopDownloads(downloadsDir);

app.UseAuthentication();
app.UseAuthorization();

// T-050 — every ErrorMessages member reads CultureInfo.CurrentUICulture, which .NET already flows
// across await boundaries. Setting it once here is what lets ~every existing `ErrorMessages.X`
// call site answer in the reader's language without passing one around. After UseAuthentication,
// because the signed-in account's own preference outranks the browser's header.
app.Use(async (ctx, next) =>
{
    var lang = ctx.User.Identity?.IsAuthenticated == true
        ? await LanguagePreference.OfUserAsync(ctx)
        : null;
    lang ??= LanguagePreference.FromAcceptLanguage(ctx.Request.Headers.AcceptLanguage.ToString());
    if (lang is not null)
        CultureInfo.CurrentUICulture = new CultureInfo(lang);
    await next();
});

var blogHost = builder.Configuration[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
app.MapWhen(ctx => string.Equals(ctx.Request.Host.Host, blogHost, StringComparison.OrdinalIgnoreCase),
    blogApp => blogApp.Run(BlogEndpoints.HandleRequest));

app.MapAuthEndpoints();
app.MapDraftEndpoints();
app.MapFolderEndpoints();
app.MapSeriesEndpoints();
app.MapFormPresetEndpoints();
app.MapGlossaryEndpoints();
app.MapBlogEndpoints();
app.MapPostEndpoints();
app.MapPublishEndpoints();
app.MapAssetEndpoints();
app.MapChannelEndpoints();
app.MapScheduledPostEndpoints();
app.MapBillingEndpoints();
app.MapAdminEndpoints();
app.MapAiJobEndpoints();

// Indie-gamedev module (Phase 13, ADR-101). A module is endpoints and screens behind a flag — its
// entities live in the same context either way, so turning this off hides the feature without
// touching the schema. `/api/me` reports the same flag so the client hides its menu entries too.
if (ProjectEndpoints.IsEnabled(app.Configuration))
{
    app.MapIndieDevEndpoints();
    app.MapAssetIndexEndpoints();
    app.MapTaskEndpoints();
    app.MapSprintEndpoints();
    app.MapBuildEndpoints();
}
#endregion

// MUST be here, after all endpoints. Takes the same options as the static-file middleware above:
// this is the branch every deep link goes through (/drafts, /dev/styleguide, ...), so leaving it on
// the defaults would fix caching only for someone who happened to arrive at "/".
app.MapFallbackToFile("index.html", indexNoCache);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
    dbContext.Database.Migrate();

    // Enable Write-Ahead Logging for better concurrency
    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    // Admin bootstrap (IF2). Grants only — it never revokes, so removing the setting doesn't
    // silently lock the panel, and an admin promoted later by other means isn't undone on the
    // next restart. Runs after Migrate() because it writes to a column a migration may just
    // have added. A missing/unknown email is a no-op: this must never block startup.
    var adminEmail = app.Configuration[Consts.General.AdminEmailCfg];
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var normalized = adminEmail.Trim().ToUpperInvariant();
        var admin = dbContext.Users.FirstOrDefault(u => u.NormalizedEmail == normalized);
        if (admin is { IsAdmin: false })
        {
            admin.IsAdmin = true;
            dbContext.SaveChanges();
            app.Logger.LogInformation("Admin rights granted to {Email}", adminEmail);
        }
    }

    // T-085 — gives every channel connected before PublishTargets existed its projected row.
    // Idempotent and cheap (one query, and nothing to write on every later start), so it stays as
    // a startup step rather than a one-shot SQL data migration that can only be run once and
    // cannot be tested. Never blocks startup: publishing also ensures the row on its own path.
    try
    {
        var backfilled = await TelegramTargetProjection.BackfillAsync(dbContext);
        if (backfilled > 0)
            app.Logger.LogInformation("Projected {Count} Telegram channel(s) into PublishTargets", backfilled);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Telegram publish-target backfill failed");
    }
}

#region Health (Heartbeat)
app.MapGet("/api/health", () => Results.Ok(new
{
    name = app.Environment.ApplicationName,
    env = app.Environment.EnvironmentName,
    version = Consts.CurrentVersion,
    // Whether this installation asks for an invite code. Reported here because the register page
    // needs it *before* anyone is signed in, and /api/health is the one endpoint a stranger may
    // already call. It says what this server requires — not who is asking.
    openRegistration = app.Configuration.IsOn(Consts.General.OpenRegistrationCfg),
    timeUtc = DateTime.UtcNow,
    status = "I'm fine, thanks."
}));
#endregion

// ADR-104 — the listening address is configuration, not a literal. `app.Run(url)` silently
// overrides ASPNETCORE_URLS, so the hardcoded localhost:8080 made the port unmovable: the desktop
// shell has to take a free port from the OS, and two of these on one machine cannot both be 8080.
//
// The default is unchanged, so `dotnet run` and production (whose port is fixed by the Cloudflare
// tunnel config) behave exactly as before.
var explicitUrl = app.Configuration[Consts.General.UrlsCfg];
if (!string.IsNullOrWhiteSpace(explicitUrl))
    app.Run(explicitUrl);
else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    app.Run();  // no argument: let Kestrel read ASPNETCORE_URLS itself, which may list several
else
    app.Run(Consts.URLs.Localhost);
