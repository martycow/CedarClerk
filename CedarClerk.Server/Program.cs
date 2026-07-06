var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => $"{app.Environment.ApplicationName} is alive 🐮");
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    version = "0.1.0",
    timeUtc = DateTime.UtcNow
}));

app.Run("http://localhost:8080");
