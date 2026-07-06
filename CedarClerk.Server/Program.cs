using CedarClerk.Server;
using CedarClerk.Server.Bot;
using CedarClerk.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Setting up Database path
var dataDir = Environment.GetEnvironmentVariable("CEDAR_DATA_DIR") ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "cedar.db");

builder.Services.AddDbContext<CedarDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

// Auth
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.Password.RequiredLength = 10;
        o.User.RequireUniqueEmail = true;
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

// Application
var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapDraftEndpoints();
app.MapPostEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CedarDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); // Enable Write-Ahead Logging for better concurrency
}

app.MapGet("/", () => $"{app.Environment.ApplicationName} is alive 🐮\nServer time: {DateTime.UtcNow.ToLongDateString()} {DateTime.UtcNow.ToLongTimeString()} UTC");
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    version = "0.3.0",
    timeUtc = DateTime.UtcNow
}));

app.Run("http://localhost:8080");
