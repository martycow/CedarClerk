using System.Security.Claims;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Payments;

namespace CedarClerk.Server;

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/billing");

        group.MapGet("/status", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, TelegramBotService bot) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var starsAmount = cfg.GetValue<int>(Consts.StarsAmountCfg);
            return Results.Ok(new
            {
                planTier = user.PlanTier.ToString(),
                providers = new
                {
                    stripe = !string.IsNullOrEmpty(cfg[Consts.StripeSecretKeyCfg]) && !string.IsNullOrEmpty(cfg[Consts.StripePriceIdCfg]),
                    telegramStars = bot.IsRunning && starsAmount > 0,
                    paypal = false, // stub — see docs/integrations-setup.md
                },
                starsAmount,
            });
        }).RequireAuthorization();
        
        group.MapPost("/stripe/checkout", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, IHttpClientFactory httpFactory) =>
        {
            var secretKey = cfg[Consts.StripeSecretKeyCfg];
            var priceId = cfg[Consts.StripePriceIdCfg];
            if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(priceId))
                return Results.Json(new { error = "Stripe is not configured — see docs/integrations-setup.md" }, statusCode: StatusCodes.Status501NotImplemented);

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var baseUrl = cfg[Consts.PublicBaseUrlCfg] ?? Consts.Url;
            var form = new Dictionary<string, string>
            {
                ["mode"] = "subscription",
                ["line_items[0][price]"] = priceId,
                ["line_items[0][quantity]"] = "1",
                ["success_url"] = $"{baseUrl}/?billing=success",
                ["cancel_url"] = $"{baseUrl}/?billing=cancelled",
                ["client_reference_id"] = user.Id,
            };
            if (!string.IsNullOrEmpty(user.Email))
                form["customer_email"] = user.Email;

            var http = httpFactory.CreateClient("billing");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {secretKey}");
            request.Content = new FormUrlEncodedContent(form);

            var response = await http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return Results.Json(new { error = $"Stripe API error ({(int)response.StatusCode}) — check server logs" }, statusCode: StatusCodes.Status502BadGateway);

            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("url").GetString();
            return Results.Ok(new { url });
        }).RequireAuthorization();
        
        group.MapPost("/stripe/webhook", async (HttpRequest request, CedarDbContext db, IConfiguration cfg, ILogger<Payment> logger) =>
        {
            var webhookSecret = cfg[Consts.StripeWebhookSecretCfg];
            if (string.IsNullOrEmpty(webhookSecret))
                return Results.StatusCode(StatusCodes.Status501NotImplemented);

            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync();
            var signature = request.Headers["Stripe-Signature"].FirstOrDefault();

            if (!StripeWebhookVerifier.Verify(payload, signature, webhookSecret, DateTimeOffset.UtcNow))
            {
                logger.LogWarning("Rejected Stripe webhook with invalid signature");
                return Results.BadRequest();
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var eventType = root.GetProperty("type").GetString();
            var obj = root.GetProperty("data").GetProperty("object");

            switch (eventType)
            {
                case "checkout.session.completed":
                {
                    var userId = obj.TryGetProperty("client_reference_id", out var refId) ? refId.GetString() : null;
                    if (userId is null) break;

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                    if (user is null)
                    {
                        logger.LogWarning("Stripe checkout completed for unknown user {UserId}", userId);
                        break;
                    }

                    user.PlanTier = PlanTiers.Pro;
                    if (obj.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.String)
                        user.StripeCustomerId = customer.GetString();

                    db.Payments.Add(new Payment
                    {
                        OwnerId = user.Id,
                        Provider = "stripe",
                        ExternalId = obj.TryGetProperty("id", out var sid) ? sid.GetString() : null,
                        Amount = obj.TryGetProperty("amount_total", out var amt) && amt.ValueKind == JsonValueKind.Number ? amt.GetInt64() : 0,
                        Currency = obj.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "" : "",
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Stripe checkout completed — user {UserId} upgraded to Pro", userId);
                    break;
                }
                case "customer.subscription.deleted":
                {
                    var customerId = obj.TryGetProperty("customer", out var cust) ? cust.GetString() : null;
                    if (customerId is null) break;

                    var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
                    if (user is not null)
                    {
                        user.PlanTier = PlanTiers.Free;
                        await db.SaveChangesAsync();
                        logger.LogInformation("Stripe subscription cancelled — user {UserId} downgraded to Free", user.Id);
                    }
                    break;
                }
            }

            return Results.Ok();
        });
        
        group.MapPost("/telegram-stars/invoice", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, TelegramBotService bot) =>
        {
            var starsAmount = cfg.GetValue<int>(Consts.StarsAmountCfg);
            if (starsAmount <= 0)
                return Results.Json(new { error = ErrorMessages.TelegramBillingNotConfigured }, statusCode: StatusCodes.Status501NotImplemented);
            
            if (!bot.IsRunning)
                return Results.Json(new { error = ErrorMessages.BotNotRunning }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var user = await users.GetUserAsync(principal);
            if (user is null) 
                return Results.Unauthorized();
            
            if (user.TelegramUserId is null)
                return Results.BadRequest(new { error = ErrorMessages.LinkYouTelegram });

            await bot.Client.SendInvoice(
                chatId: user.TelegramUserId.Value,
                title: "Cedar Clerk Pro",
                description: "Upgrade to Cedar Clerk Pro and support the project!",
                payload: user.Id,
                currency: "XTR",
                prices: [new LabeledPrice("Cedar Clerk Pro", starsAmount)]);

            return Results.Ok(new { sent = true });
        }).RequireAuthorization();

        group.MapPost("/paypal/checkout", (ClaimsPrincipal principal) =>
            Results.Json(new { error = ErrorMessages.PaypalNotConfigured }, statusCode: StatusCodes.Status501NotImplemented))
            .RequireAuthorization();
    }
}
