using CedarClerk.Core;
using CedarClerk.Server;
using CedarClerk.Server.Bot;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Quartz;

const int passwordRequiredLength = 8;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpClient(); // named clients used by billing (Stripe) and translation providers

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
app.MapBlogEndpoints();
app.MapPostEndpoints();
app.MapAssetEndpoints();
app.MapChannelEndpoints();
app.MapScheduledPostEndpoints();
app.MapBillingEndpoints();
#endregion

// MUST be here, after all endpoints
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
    dbContext.Database.Migrate();
    
    // Enable Write-Ahead Logging for better concurrency
    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
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
