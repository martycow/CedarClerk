using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

const string currentVersion = "0.4.0";
const string dataDirectoryKey = "CEDAR_DATA_DIR";
const string dbFileName = "cedar.db";
const int passwordMinLength = 10;

var builder = WebApplication.CreateBuilder(args);

#region Paths
var dataDir = Environment.GetEnvironmentVariable(dataDirectoryKey);
if (dataDir == null)
{
    dataDir = Path.Combine(builder.Environment.ContentRootPath, "data");
    Environment.SetEnvironmentVariable(dataDirectoryKey, dataDir);
}

var mediaDir = Path.Combine(dataDir, "media");
var dbPath = Path.Combine(dataDir, dbFileName);

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
        options.Password.RequiredLength = passwordMinLength;
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
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBotService>());
#endregion

#region Application
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapDraftEndpoints();
app.MapPostEndpoints();
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
    version = currentVersion,
    timeUtc = DateTime.UtcNow,
    status = "I'm fine, thanks."
}));
#endregion

app.Run("http://localhost:8080");
