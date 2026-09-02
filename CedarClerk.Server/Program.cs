using System.Globalization;
using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Analytics;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Email;
using CedarClerk.Server.Modules.Agent;
using CedarClerk.Server.Modules.IndieDev;
using CedarClerk.Server.Publishing;
using CedarClerk.Server.Search;
using CedarClerk.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PostHog;
using PostHog.Config;
using Quartz;

const int passwordRequiredLength = 8;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 210 * 1024 * 1024);

#region Agent mode
if (AgentEndpoints.IsAgent(builder.Configuration))
{
    builder.Services.AddSingleton<AgentScanService>();
    builder.Services.AddSingleton<AgentGrants>();

    var agent = builder.Build();
    agent.MapAgentEndpoints();
    agent.Logger.LogInformation("Cedar Clerk agent {Version} — filesystem only, no database", Consts.CurrentVersion);

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
var importTmpDir = Path.Combine(dataDir, "import-tmp");
var dataProtectionKeysDir = Path.Combine(dataDir, "dataprotection-keys");
var thumbnailsDir = Path.Combine(dataDir, "thumbs");
var downloadsDir = Path.Combine(dataDir, "downloads");
// The landing's screenshots (ADR-215). In the data directory rather than in wwwroot,
// because a deploy replaces wwwroot whole and would take the uploads with it.
var landingDir = Path.Combine(dataDir, "landing");

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(mediaDir);
Directory.CreateDirectory(importTmpDir);
Directory.CreateDirectory(dataProtectionKeysDir);
Directory.CreateDirectory(thumbnailsDir);
#endregion

#region Services
// Scoped, matching the context's own lifetime: a longer-lived provider would carry one request's
// tenant into the next, and a shorter-lived one could not outlive the queries that read it.
builder.Services.AddScoped<TenantProvider>();
// The search interceptor rides on the context registration rather than inside the context, so
// tests that construct CedarDbContext directly stay free of the FTS table (see DraftSearchInterceptor).
builder.Services.AddSingleton<DraftSearchInterceptor>();
builder.Services.AddDbContext<CedarDbContext>((sp, dbContextBuilder) => dbContextBuilder
    .UseSqlite($"Data Source={dbPath}")
    .AddInterceptors(sp.GetRequiredService<DraftSearchInterceptor>()));

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

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();

// T-003 / ADR-237 — registered only when both halves are configured, so an install without Google
// credentials simply has no Google scheme and /api/auth/external/google answers 501 rather than
// throwing at startup. Telegram needs nothing here: it is a signed widget payload, not a scheme.
if (builder.Configuration[Consts.ExternalAuth.GoogleClientIdCfg] is { Length: > 0 } googleClientId
    && builder.Configuration[Consts.ExternalAuth.GoogleClientSecretCfg] is { Length: > 0 } googleClientSecret)
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        // The external cookie is what carries the identity between the callback and the screen that
        // finishes the job — an invite code and an account name are asked for after Google answers,
        // never before, because most people who press the button already have an account.
        options.SignInScheme = IdentityConstants.ExternalScheme;
        // Asked for explicitly: without it the callback has a subject and no address, and the whole
        // "does an account already hold this email" question below has nothing to ask about.
        options.Scope.Add("email");
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
    });
}

builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = passwordRequiredLength;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<CedarDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(AuthCookie.Configure);

builder.Services.AddScoped<TenantContext>();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton(new MediaPaths(mediaDir));
builder.Services.AddSingleton<TenantOwnerCache.ForHosts>();
builder.Services.AddSingleton<TenantOwnerCache.ForMedia>();
builder.Services.AddSingleton<TenantOwnerCache.ForDomains>();
builder.Services.AddSingleton<MediaOwnerIndex>();
builder.Services.AddSingleton<MediaVisibilityIndex>();
// Registered unconditionally, unlike the module's endpoints: the door in front of /media is one
// middleware for every host, and a flag that changed which dependencies it takes would be a
// different door rather than a hidden feature.
builder.Services.AddSingleton<CanvasMediaIndex>();
builder.Services.AddSingleton(new ImportTmpPaths(importTmpDir));
builder.Services.AddSingleton<AiJobService>();
builder.Services.AddSingleton(new ThumbnailPaths(thumbnailsDir));
builder.Services.AddSingleton(new LandingPaths(landingDir));
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());
builder.Services.AddHttpClient(); // named clients used by billing (Stripe), translation providers, and email
builder.Services.AddSingleton<ResendEmailProvider>();
builder.Services.AddSingleton<PublishTargetSecrets>();
builder.Services.AddSingleton<PrivateAccess>();
builder.Services.AddSingleton<MediaGrant>();
builder.Services.AddScoped<TelegramPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<TelegramPublishTarget>());
builder.Services.AddScoped<BlueskyPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<BlueskyPublishTarget>());
builder.Services.AddScoped<XPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<XPublishTarget>());
builder.Services.AddScoped<DiscordPublishTarget>();
builder.Services.AddScoped<IPublishTarget>(sp => sp.GetRequiredService<DiscordPublishTarget>());
builder.Services.AddSingleton<PublishJobRunner>();
builder.Services.AddScoped<IDraftSearchIndex, DraftSearchIndex>();
builder.Services.AddHostedService<DraftSearchBackfill>();
builder.Services.AddSingleton<BlogSubscriberNotifier>();
builder.Services.AddPreflightServices();

// Analytics (T-153, ADR-236). The provider is registered only when it is configured, so a local run
// and a self-hosted install carry no client at all; ProductAnalytics resolves it with GetService and
// turns into a no-op rather than a missing dependency.
if (builder.Configuration.GetValue(Consts.Analytics.EnabledCfg, false)
    && builder.Configuration[Consts.Analytics.ProjectKeyCfg] is { Length: > 0 } postHogKey)
{
    builder.AddPostHog(postHog => postHog.PostConfigure(options =>
    {
        options.ProjectToken = postHogKey;
        options.HostUrl = new Uri(builder.Configuration[Consts.Analytics.HostCfg] ?? Consts.Analytics.DefaultHost);
    }));
}
builder.Services.AddSingleton(sp => new ProductAnalytics(
    sp.GetService<IPostHogClient>(),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<ProductAnalytics>>()));

// The canvas hub (ADR-218), registered with the module it belongs to. The tuning is about the
// Cloudflare Tunnel in front of Kestrel: WebSockets pass through it, but an idle connection is not
// guaranteed to survive, so the keep-alive is well inside any idle window and the client timeout
// is short enough that a dropped tunnel becomes a reconnect rather than a board that has silently
// stopped updating. The receive limit is above the largest legal batch (a hundred items carrying
// four thousand characters each) and far below what a whole board would be.
if (ProjectEndpoints.IsEnabled(builder.Configuration))
{
    builder.Services.AddSignalR(o =>
    {
        o.KeepAliveInterval = TimeSpan.FromSeconds(10);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        o.MaximumReceiveMessageSize = 512 * 1024;
    }).AddJsonProtocol(o =>
    {
        // ADR-115 over the socket: SQLite hands back Unspecified kinds, and a timestamp without a
        // Z is read as local time by the browser.
        o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        o.PayloadSerializerOptions.Converters.Add(new NullableUtcDateTimeConverter());
    });
}

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

    // Wave 1 item 7 — the notify-on-publish queue's backstop.
    var notifyJobKey = new JobKey("SendBlogNotifications");
    q.AddJob<SendBlogNotificationsJob>(opts => opts.WithIdentity(notifyJobKey));
    q.AddTrigger(t => t.ForJob(notifyJobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));

    // Wave 2 item 10 — keeps the queue slots' upcoming occurrences filled from the evergreen pool.
    var fillSlotsJobKey = new JobKey("FillQueueSlots");
    q.AddJob<FillQueueSlotsJob>(opts => opts.WithIdentity(fillSlotsJobKey));
    q.AddTrigger(t => t.ForJob(fillSlotsJobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(30).RepeatForever()));

    // Hourly check if the paid plan is lapsed
    var downgradeJobKey = new JobKey("DowngradeExpiredPlans");
    q.AddJob<DowngradeExpiredPlansJob>(opts => opts.WithIdentity(downgradeJobKey));
    q.AddTrigger(t => t.ForJob(downgradeJobKey).WithSimpleSchedule(s => s.WithIntervalInHours(1).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
#endregion

#region Application
var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is not null)
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException")
            .LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);

    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = ex is null ? "Unexpected server error" : $"{ex.GetType().Name}: {ex.Message}" });
}));

var indexNoCache = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate";
    }
};

app.UseTenantResolution();
app.UseTenantScope();
app.UseBlogOnlyHost();

app.UseLanding();

// The rewrite "/" → index.html belongs to the application's own host: on a subdomain "/" is the
// blog index, which the branch below renders.
app.UseWhen(ctx => !TenantRouting.IsTenantRequest(ctx), appHost => appHost.UseDefaultFiles());
app.UseStaticFiles(indexNoCache);
app.UseTenantMedia(mediaDir);
app.UseLandingMedia(landingDir);

// ADR-116 — updates for the desktop shell. Unconditional since ADR-117: the process that used to
// need this switched off is now an agent, and an agent leaves this file before reaching here.
app.UseDesktopDownloads(downloadsDir);

app.UseAuthentication();
app.UseTenantFromUser();
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

// Every host that renders a blog, not only the legacy one: /api/auth/me and publish-blog hand out
// a subdomain URL for every account, and a host the server advertises has to be a host it serves.
// After the static files above, because the rendered pages ask wwwroot for their fonts and OG image.
app.MapWhen(TenantRouting.IsTenantRequest,
    blogApp => blogApp.Run(BlogEndpoints.HandleRequest));

app.MapAuthEndpoints();
app.MapExternalAuthEndpoints();
app.MapWaitlistEndpoint();
app.MapFeedbackEndpoints();
app.MapPresetEndpoints();
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
app.MapChannelInviteLinkEndpoints();
app.MapScheduledPostEndpoints();
app.MapQueueSlotEndpoints();
app.MapTrackedLinkEndpoints();
app.MapPreflightEndpoints();
app.MapStatsInsightsEndpoints();
app.MapBillingEndpoints();
app.MapAdminEndpoints();
app.MapAiJobEndpoints();
app.MapSearchEndpoints();
app.MapExportTextEndpoints();
app.MapDraftPreviewEndpoints();
app.MapPreviewEndpoints();

// Wave 1 item 8 — the public preview page, on the app host and deliberately anonymous: the token
// is the whole credential, and the handler answers a wrong one with a plain 404.
app.MapGet("/preview/{token}", (HttpContext ctx) => BlogEndpoints.HandleDraftPreviewAsync(ctx));

// Wave 2 item 16 — the tracked-link redirect, on the app host and anonymous like the preview page.
// An endpoint route, so it wins over the SPA fallback below; an unknown code is a plain 404.
app.MapGet("/l/{code}", (HttpContext ctx) => TrackedLinkEndpoints.HandleRedirectAsync(ctx));

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
    app.MapCanvasEndpoints();
    app.MapProjectMemberEndpoints();
    // T-358 — teams grant access to projects and mean nothing without them, so they live behind the
    // same flag as the projects they reach.
    app.MapTeamEndpoints();
    app.MapDialogueEndpoints();
    app.MapHub<CanvasHub>("/hubs/canvas");
}
#endregion

// MUST be here, after all endpoints. Takes the same options as the static-file middleware above:
// this is the branch every deep link goes through (/drafts, /dev/styleguide, ...), so leaving it on
// the defaults would fix caching only for someone who happened to arrive at "/".
app.MapFallbackToFile("index.html", indexNoCache);

using (var scope = app.Services.CreatePlatformScope())
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
    openRegistration = app.Configuration.IsOn(Consts.General.OpenRegistrationCfg),
    // The provider's project key is public by design — it ships inside the page script — so it
    // rides the call the frontend already makes at startup rather than being built into the bundle,
    // where a self-hosted install could not turn it off. Absent when analytics is not configured,
    // which is how the SPA decides there is nothing to ask consent for.
    analytics = app.Configuration.GetValue(Consts.Analytics.EnabledCfg, false)
                && app.Configuration[Consts.Analytics.ProjectKeyCfg] is { Length: > 0 } key
        ? new { key, host = app.Configuration[Consts.Analytics.HostCfg] ?? Consts.Analytics.DefaultHost }
        : null,
    // T-003 — which sign-in buttons the doors may draw. Read here rather than from a settings call,
    // because /login and /register run before there is a session to make one with. The Telegram
    // widget needs the bot's username to render at all, and it is public by nature.
    externalAuth = new
    {
        google = !string.IsNullOrEmpty(app.Configuration[Consts.ExternalAuth.GoogleClientIdCfg])
                 && !string.IsNullOrEmpty(app.Configuration[Consts.ExternalAuth.GoogleClientSecretCfg]),
        telegramBot = app.Services.GetRequiredService<TelegramBotService>() is { IsRunning: true } bot
            ? bot.Me.Username
            : null,
    },
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

namespace CedarClerk.Server.Tenancy
{
    /// <summary>
    /// Which of the two sites a request belongs to. The blog branch, the landing page and the
    /// static-file segment all ask this question, and asking it in three shapes is how a registered
    /// subdomain came to answer with a cacheable marketing page while the API advertised it as a blog.
    /// </summary>
    public static class TenantRouting
    {
        private const string AppShell = "/index.html";

        public static bool IsTenantRequest(HttpContext ctx) =>
            ctx.RequestServices.GetService<TenantContext>() is { IsTenantRequest: true };

        /// <summary>
        /// A tenant subdomain is one account's public blog and nothing else. Routing has already
        /// picked the SPA fallback or an /api/* handler by this point; dropping that pick is what
        /// turns the application's endpoints on a subdomain from a 401 into the 404 they are, and
        /// leaves every path that is not a static asset to the blog branch. wwwroot keeps answering,
        /// because the blog's own pages ask it for fonts and the OG image — all but the one file
        /// that would boot an application whose API does not live on this host.
        /// </summary>
        public static void UseBlogOnlyHost(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
        {
            if (!IsTenantRequest(ctx))
            {
                await next();
                return;
            }

            ctx.SetEndpoint(null);
            if (string.Equals(ctx.Request.Path.Value, AppShell, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });
    }
}
