using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Email;
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

#region Paths
var dataDir = Environment.GetEnvironmentVariable(Consts.DataDirectoryKey);
if (dataDir == null)
{
    dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Environment.SetEnvironmentVariable(Consts.DataDirectoryKey, dataDir);
}

var mediaDir = Path.Combine(dataDir, "media");
var dbPath = Path.Combine(dataDir, Consts.DbFileName);

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(mediaDir);
#endregion

#region Services
builder.Services.AddDbContext<CedarDbContext>(dbContextBuilder => dbContextBuilder.UseSqlite($"Data Source={dbPath}"));

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
    o.Cookie.MaxAge = TimeSpan.FromDays(30);
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});

builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton(new MediaPaths(mediaDir));
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());
builder.Services.AddHttpClient(); // named clients used by billing (Stripe), translation providers, and email
builder.Services.AddSingleton<ResendEmailProvider>();

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("PublishDueScheduledPosts");
    q.AddJob<PublishDueScheduledPostsJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInMinutes(1).RepeatForever()));

    // Daily channel members count snapshot
    var statsJobKey = new JobKey("SnapshotChannelStats");
    q.AddJob<SnapshotChannelStatsJob>(opts => opts.WithIdentity(statsJobKey));
    q.AddTrigger(t => t.ForJob(statsJobKey).WithCronSchedule("0 0 4 * * ?"));

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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaDir),
    RequestPath = "/media"
});

app.UseAuthentication();
app.UseAuthorization();

var blogHost = builder.Configuration[Consts.General.BlogHostCfg] ?? Consts.URLs.BlogHost;
app.MapWhen(ctx => string.Equals(ctx.Request.Host.Host, blogHost, StringComparison.OrdinalIgnoreCase),
    blogApp => blogApp.Run(BlogEndpoints.HandleRequest));

app.MapAuthEndpoints();
app.MapDraftEndpoints();
app.MapFolderEndpoints();
app.MapFormPresetEndpoints();
app.MapBlogEndpoints();
app.MapPostEndpoints();
app.MapAssetEndpoints();
app.MapChannelEndpoints();
app.MapScheduledPostEndpoints();
app.MapBillingEndpoints();
app.MapAdminEndpoints();
#endregion

// MUST be here, after all endpoints
app.MapFallbackToFile("index.html");

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
}

#region Health (Heartbeat)
app.MapGet("/api/health", () => Results.Ok(new
{
    name = app.Environment.ApplicationName,
    env = app.Environment.EnvironmentName,
    version = Consts.CurrentVersion,
    timeUtc = DateTime.UtcNow,
    status = "I'm fine, thanks."
}));
#endregion

app.Run(Consts.URLs.Localhost);
