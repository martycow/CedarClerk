using System.Text;
using System.Text.Json;
using CedarClerk.Core;

namespace CedarClerk.Server.Email;

// Only one email provider exists today (unlike ITranslationProvider/IAiEditProvider, which
// genuinely have several interchangeable implementations already) — a plain concrete class, no
// interface abstraction, per the ADR following ADR-040, docs/DECISIONS.md. Resend's HTTP API is
// used directly (POST JSON, no SMTP), the same "not configured -> log and skip, never throw"
// shape as TelegramBotService.IsRunning.
public class ResendEmailProvider(IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<ResendEmailProvider> logger)
{
    public bool IsConfigured => !string.IsNullOrEmpty(cfg[Consts.Email.ResendApiKeyCfg]);

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Email provider not configured — skipping send to {Email}", toEmail);
            return false;
        }

        var apiKey = cfg[Consts.Email.ResendApiKeyCfg];
        var from = cfg[Consts.Email.FromAddressCfg] ?? "Cedar Clerk <onboarding@resend.dev>";

        try
        {
            var http = httpFactory.CreateClient("email");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { from, to = new[] { toEmail }, subject, html = htmlBody }),
                Encoding.UTF8, "application/json");

            var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Resend API returned {Status} sending to {Email}: {Body}",
                    (int)response.StatusCode, toEmail, body.Length <= 300 ? body : body[..300]);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Email send failed for {Email}", toEmail);
            return false;
        }
    }
}
