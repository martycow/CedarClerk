using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CedarClerk.Core;
using CedarClerk.Localization;
using CedarClerk.Server.Bot;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Payments;

namespace CedarClerk.Server;


/// <summary>
///  Stripe: auto-renewing subscriptions
///  Telegram Stars: native 30-day subscriptions
///  PayPal: manual 30-day passes 
/// </summary>
public static class BillingEndpoints
{
    public record CheckoutRequest(string Plan);

    public static void MapBillingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/billing");

        group.MapGet("/status", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, TelegramBotService bot) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();

            var now = DateTime.UtcNow;
            return Results.Ok(new
            {
                planTier = SubscriptionPlanHelper.CheckPlanExpiration(user.PlanTier, user.PlanExpiresAt, now).ToString(),
                planExpiresAt = user.PlanExpiresAt,
                trialUsed = user.TrialUsedAt is not null,
                providers = new
                {
                    stripe = !string.IsNullOrEmpty(cfg[Consts.Stripe.SecretKeyCfg]) && !string.IsNullOrEmpty(cfg[Consts.Stripe.ProPriceIdCfg]),
                    telegramStars = bot.IsRunning,
                    paypal = !string.IsNullOrEmpty(cfg[Consts.PayPal.ClientIdCfg]) && !string.IsNullOrEmpty(cfg[Consts.PayPal.SecretKeyCfg]),
                },
                prices = new
                {
                    proUsd = SubscriptionPlanHelper.PriceUsd(Consts.Plans.Pro),
                    proPlusUsd = SubscriptionPlanHelper.PriceUsd(Consts.Plans.ProPlus),
                    trialUsd = SubscriptionPlanHelper.PriceUsd(Consts.Plans.Trial),
                    proStars = cfg.GetValue(Consts.Telegram.ProStarsPriceCfg, Consts.Telegram.DefaultProStarsPrice),
                    proPlusStars = cfg.GetValue(Consts.Telegram.ProPlusStarsPriceCfg, Consts.Telegram.DefaultProPlusStarsPrice),
                    trialStars = cfg.GetValue(Consts.Telegram.TrialStarsPriceCfg, Consts.Telegram.DefaultTrialStarsPrice),
                },
            });
        }).RequireAuthorization();

        #region Stripe

        group.MapPost("/stripe/checkout", async (CheckoutRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, IHttpClientFactory httpFactory) =>
        {
            var secretKey = cfg[Consts.Stripe.SecretKeyCfg];
            if (!SubscriptionPlanHelper.IsValid(req.Plan))
                return Results.BadRequest(new { error = $"Unknown plan '{req.Plan}'" });
            var priceId = req.Plan switch
            {
                Consts.Plans.Pro => cfg[Consts.Stripe.ProPriceIdCfg],
                Consts.Plans.ProPlus => cfg[Consts.Stripe.ProPlusPriceIdCfg],
                _ => null, // trial uses inline price_data below — no dashboard price required
            };
            if (string.IsNullOrEmpty(secretKey) || (req.Plan != Consts.Plans.Trial && string.IsNullOrEmpty(priceId)))
                return Results.Json(new { error = "Stripe is not configured for this plan — see docs/integrations-setup.md" }, statusCode: StatusCodes.Status501NotImplemented);

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (req.Plan == Consts.Plans.Trial && user.TrialUsedAt is not null)
                return Results.BadRequest(new { error = "Trial has already been used on this account" });

            var mainHost = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
            
            var isSubscription = req.Plan != Consts.Plans.Trial;
            var form = new Dictionary<string, string>
            {
                ["mode"] = isSubscription ? "subscription" : "payment",
                ["line_items[0][quantity]"] = "1",
                ["success_url"] = $"{mainHost}/?billing=success",
                ["cancel_url"] = $"{mainHost}/?billing=cancelled",
                ["client_reference_id"] = user.Id,
                ["metadata[plan]"] = req.Plan,
            };
            if (isSubscription)
            {
                form["line_items[0][price]"] = priceId!;
            }
            else
            {
                // Trial is a one-time $1 payment
                var unitsAmount = SubscriptionPlanHelper.PriceUsd(Consts.Plans.Trial) * 100;
                
                form["line_items[0][price_data][currency]"] = "usd";
                form["line_items[0][price_data][unit_amount]"] = (unitsAmount).ToString();
                form["line_items[0][price_data][product_data][name]"] = "Cedar Clerk Pro Plus ( 7-day trial)";
            }
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
            return Results.Ok(new { url = doc.RootElement.GetProperty("url").GetString() });
        }).RequireAuthorization();
        
        group.MapPost("/stripe/webhook", async (HttpRequest request, CedarDbContext db, IConfiguration cfg, ILogger<Payment> logger) =>
        {
            var webhookSecret = cfg[Consts.Stripe.WebhookSecretCfg];
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
                    var plan = obj.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("plan", out var p) ? p.GetString() : null;
                    var sessionId = obj.TryGetProperty("id", out var sid) ? sid.GetString() : null;
                    if (userId is null || plan is null) break;

                    if (sessionId is not null && await db.Payments.AnyAsync(x => x.ExternalId == sessionId))
                        break; // webhook retry — already processed

                    var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
                    if (user is null)
                    {
                        logger.LogWarning("Stripe checkout completed for unknown user {UserId}", userId);
                        break;
                    }

                    var error = SubscriptionPlan.ApplyPurchase(user, plan, DateTime.UtcNow);
                    if (error is not null)
                    {
                        logger.LogWarning("Stripe checkout for user {UserId} not applied: {Error}", userId, error);
                        break;
                    }

                    if (obj.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.String)
                        user.StripeCustomerId = customer.GetString();

                    db.Payments.Add(new Payment
                    {
                        OwnerId = user.Id,
                        Provider = "stripe",
                        Plan = plan,
                        ExternalId = sessionId,
                        Amount = obj.TryGetProperty("amount_total", out var amt) && amt.ValueKind == JsonValueKind.Number ? amt.GetInt64() : 0,
                        Currency = obj.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "" : "",
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Stripe checkout completed — user {UserId} on plan {Plan}", userId, plan);
                    break;
                }
                case "invoice.paid":
                {
                    var billingReason = obj.TryGetProperty("billing_reason", out var br) ? br.GetString() : null;
                    if (billingReason != "subscription_cycle") break;

                    var customerId = obj.TryGetProperty("customer", out var cust) && cust.ValueKind == JsonValueKind.String ? cust.GetString() : null;
                    if (customerId is null) break;

                    var invoiceId = obj.TryGetProperty("id", out var iid) ? iid.GetString() : null;
                    if (invoiceId is not null && await db.Payments.AnyAsync(x => x.ExternalId == invoiceId))
                        break;

                    var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
                    if (user is null || user.PlanTier is not (PlanTiers.Pro or PlanTiers.ProPlus)) break;

                    var plan = user.PlanTier == PlanTiers.Pro ? Consts.Plans.Pro : Consts.Plans.ProPlus;
                    SubscriptionPlan.ApplyPurchase(user, plan, DateTime.UtcNow);
                    db.Payments.Add(new Payment
                    {
                        OwnerId = user.Id,
                        Provider = "stripe",
                        Plan = plan,
                        ExternalId = invoiceId,
                        Amount = obj.TryGetProperty("amount_paid", out var paid) && paid.ValueKind == JsonValueKind.Number ? paid.GetInt64() : 0,
                        Currency = obj.TryGetProperty("currency", out var cur2) ? cur2.GetString() ?? "" : "",
                    });
                    await db.SaveChangesAsync();
                    logger.LogInformation("Stripe renewal — user {UserId} extended on plan {Plan}", user.Id, plan);
                    break;
                }
                case "customer.subscription.deleted":
                {
                    var customerId = obj.TryGetProperty("customer", out var cust) ? cust.GetString() : null;
                    if (customerId is null) break;

                    var user = await db.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == customerId);
                    if (user is not null && user.PlanTier is PlanTiers.Pro or PlanTiers.ProPlus)
                    {
                        user.PlanExpiresAt ??= DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        logger.LogInformation("Stripe subscription cancelled — user {UserId} will lapse at {ExpiresAt}", user.Id, user.PlanExpiresAt);
                    }
                    break;
                }
            }

            return Results.Ok();
        });

        #endregion

        #region Telegram Stars

        group.MapPost("/telegram-stars/invoice", async (CheckoutRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, TelegramBotService bot) =>
        {
            if (!SubscriptionPlanHelper.IsValid(req.Plan))
                return Results.BadRequest(new { error = $"Unknown plan '{req.Plan}'" });
            
            if (!bot.IsRunning)
                return Results.Json(new { error = ErrorMessages.BotNotRunning }, statusCode: StatusCodes.Status503ServiceUnavailable);

            var user = await users.GetUserAsync(principal);
            if (user is null)
                return Results.Unauthorized();
            
            if (user.TelegramUserId is null)
                return Results.BadRequest(new { error = ErrorMessages.LinkYouTelegram });
            
            if (req.Plan == Consts.Plans.Trial && user.TrialUsedAt is not null)
                return Results.BadRequest(new { error = "Trial has already been used on this account" });

            var (title, stars) = req.Plan switch
            {
                Consts.Plans.Pro => ("Cedar Clerk Pro", cfg.GetValue(Consts.Telegram.ProStarsPriceCfg, Consts.Telegram.DefaultProStarsPrice)),
                Consts.Plans.ProPlus => ("Cedar Clerk Pro Plus", cfg.GetValue(Consts.Telegram.ProPlusStarsPriceCfg, Consts.Telegram.DefaultProPlusStarsPrice)),
                _ => ("Cedar Clerk Pro Plus (7-day trial)", cfg.GetValue(Consts.Telegram.TrialStarsPriceCfg, Consts.Telegram.DefaultTrialStarsPrice)),
            };

            var description = req.Plan == Consts.Plans.Trial
                ? "7 days of Cedar Clerk Pro Plus — one-time trial."
                : "30-day auto-renewing subscription paid in Telegram Stars.";

            int? period = req.Plan == Consts.Plans.Trial ? null : 2592000;
            
            var invoiceLink = await bot.Client.CreateInvoiceLink(
                title: title,
                description: description,
                payload: $"{req.Plan}:{user.Id}",
                currency: "XTR",
                prices: [new LabeledPrice(title, stars)],
                subscriptionPeriod: period);

            await bot.Client.SendMessage(user.TelegramUserId.Value, $"{title} — {stars} ⭐\nTap to pay: {invoiceLink}");

            return Results.Ok(new { sent = true });
        }).RequireAuthorization();

        #endregion

        #region PayPal

        group.MapPost("/paypal/checkout", async (CheckoutRequest req, ClaimsPrincipal principal, UserManager<ApplicationUser> users, IConfiguration cfg, IHttpClientFactory httpFactory) =>
        {
            if (!SubscriptionPlanHelper.IsValid(req.Plan))
                return Results.BadRequest(new { error = $"Unknown plan '{req.Plan}'" });
            
            var clientId = cfg[Consts.PayPal.ClientIdCfg];
            var secret = cfg[Consts.PayPal.SecretKeyCfg];
            
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
                return Results.Json(new { error = ErrorMessages.PaypalNotConfigured }, statusCode: StatusCodes.Status501NotImplemented);

            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (req.Plan == Consts.Plans.Trial && user.TrialUsedAt is not null)
                return Results.BadRequest(new { error = "Trial has already been used on this account" });

            var http = httpFactory.CreateClient("billing");
            var payPalBase = PayPalBaseUrl(cfg);
            var accessToken = await GetPayPalTokenAsync(http, payPalBase, clientId, secret);
            if (accessToken is null)
                return Results.Json(new { error = "PayPal auth failed — check ClientId/Secret (and Cedar:PayPal:Mode: live vs sandbox)" }, statusCode: StatusCodes.Status502BadGateway);

            var mainUrl = cfg[Consts.General.MainHostCfg] ?? Consts.URLs.MainHost;
            var orderBody = JsonSerializer.Serialize(new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        custom_id = $"{req.Plan}:{user.Id}",
                        description = req.Plan == Consts.Plans.Trial ? 
                            "Cedar Clerk Pro Plus (7-day trial)" : 
                            $"Cedar Clerk {(req.Plan == Consts.Plans.Pro ? "Pro" : "Pro Plus")} (30 days)",
                        amount = new { currency_code = "USD", value = $"{SubscriptionPlanHelper.PriceUsd(req.Plan)}.00" },
                    },
                },
                application_context = new
                {
                    brand_name = "Cedar Clerk",
                    user_action = "PAY_NOW",
                    return_url = $"{mainUrl}/api/billing/paypal/capture",
                    cancel_url = $"{mainUrl}/?billing=cancelled",
                },
            });

            using var orderReq = new HttpRequestMessage(HttpMethod.Post, $"{payPalBase}/v2/checkout/orders");
            orderReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            orderReq.Content = new StringContent(orderBody, Encoding.UTF8, "application/json");
            
            var orderResp = await http.SendAsync(orderReq);
            var orderJson = await orderResp.Content.ReadAsStringAsync();
            if (!orderResp.IsSuccessStatusCode)
                return Results.Json(new { error = $"PayPal API error ({(int)orderResp.StatusCode}) — check server logs" }, statusCode: StatusCodes.Status502BadGateway);

            using var orderDoc = JsonDocument.Parse(orderJson);
            var approveUrl = orderDoc.RootElement.GetProperty("links").EnumerateArray()
                .FirstOrDefault(l => l.GetProperty("rel").GetString() == "approve")
                .TryGetProperty("href", out var href) ? href.GetString() : null;
            
            if (approveUrl is null)
                return Results.Json(new { error = "PayPal did not return an approval link" }, statusCode: StatusCodes.Status502BadGateway);

            return Results.Ok(new { url = approveUrl });
        }).RequireAuthorization();
        
        group.MapGet("/paypal/capture", async (string token, CedarDbContext db, IConfiguration cfg, IHttpClientFactory httpFactory, ILogger<Payment> logger) =>
        {
            var clientId = cfg[Consts.PayPal.ClientIdCfg];
            var secret = cfg[Consts.PayPal.SecretKeyCfg];
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
                return Results.Redirect("/?billing=error");

            var http = httpFactory.CreateClient("billing");
            var payPalBase = PayPalBaseUrl(cfg);
            var accessToken = await GetPayPalTokenAsync(http, payPalBase, clientId, secret);
            if (accessToken is null) 
                return Results.Redirect("/?billing=error");

            using var captureReq = new HttpRequestMessage(HttpMethod.Post, $"{payPalBase}/v2/checkout/orders/{Uri.EscapeDataString(token)}/capture");
            captureReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            captureReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var captureResp = await http.SendAsync(captureReq);
            var captureJson = await captureResp.Content.ReadAsStringAsync();
            if (!captureResp.IsSuccessStatusCode)
            {
                logger.LogWarning("PayPal capture failed for order {OrderId}: {Body}", token, captureJson.Length > 300 ? captureJson[..300] : captureJson);
                return Results.Redirect("/?billing=error");
            }

            using var doc = JsonDocument.Parse(captureJson);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "COMPLETED")
                return Results.Redirect("/?billing=error");

            var unit = root.GetProperty("purchase_units")[0];
            var capture = unit.GetProperty("payments").GetProperty("captures")[0];
            var captureId = capture.GetProperty("id").GetString();
            var customId = capture.TryGetProperty("custom_id", out var cid) ? cid.GetString() : null;
            var parts = customId?.Split(':', 2);
            if (parts is not { Length: 2 })
            {
                logger.LogWarning("PayPal capture {CaptureId} without parsable custom_id", captureId);
                return Results.Redirect("/?billing=error");
            }
            var (plan, userId) = (parts[0], parts[1]);

            if (captureId is not null && await db.Payments.AnyAsync(p => p.ExternalId == captureId))
                return Results.Redirect("/?billing=success"); // refresh / double-visit

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.Redirect("/?billing=error");

            var error = SubscriptionPlan.ApplyPurchase(user, plan, DateTime.UtcNow);
            if (error is not null)
            {
                logger.LogWarning("PayPal purchase not applied for user {UserId}: {Error}", userId, error);
                return Results.Redirect("/?billing=error");
            }

            var amountValue = capture.TryGetProperty("amount", out var amount) && amount.TryGetProperty("value", out var v) ? v.GetString() : null;
            db.Payments.Add(new Payment
            {
                OwnerId = user.Id,
                Provider = "paypal",
                Plan = plan,
                ExternalId = captureId,
                Amount = decimal.TryParse(amountValue, out var dollars) ? (long)(dollars * 100) : 0,
                Currency = "usd",
            });
            await db.SaveChangesAsync();
            logger.LogInformation("PayPal capture {CaptureId} — user {UserId} on plan {Plan}", captureId, userId, plan);
            return Results.Redirect("/?billing=success");
        });

        #endregion
    }

    private static string PayPalBaseUrl(IConfiguration cfg) =>
        string.Equals(cfg[Consts.PayPal.ModeCfg], "sandbox", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.sandbox.paypal.com"
            : "https://api-m.paypal.com";

    private static async Task<string?> GetPayPalTokenAsync(HttpClient http, string payPalBase, string clientId, string secret)
    {
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{payPalBase}/v1/oauth2/token");

        var auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"));

        tokenReq.Headers.TryAddWithoutValidation("Authorization", auth);
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

        var resp = await http.SendAsync(tokenReq);
        if (!resp.IsSuccessStatusCode) 
            return null;
        
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}
