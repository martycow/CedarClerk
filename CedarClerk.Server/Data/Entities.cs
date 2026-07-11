using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public PlanTiers PlanTier { get; set; } = PlanTiers.Free;

    /// <summary>
    /// Nullable means most accounts sign in with email/password and never link their Telegram account
    /// </summary>
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? TelegramFirstName { get; set; }
    
    /// <summary>
    /// User-defined signature in the end of each post
    /// </summary>
    public string? PostSignature { get; set; }
    
    public string? StripeCustomerId { get; set; }
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    
    /// <summary>
    /// stripe, telegram-stars, paypal
    /// </summary>
    public string Provider { get; set; } = "";
    
    /// <summary>
    /// Stripe session id, Telegram charge id, PayPal order id, etc. Is used to prevent duplicates
    /// </summary>
    public string? ExternalId { get; set; }
    
    public long Amount { get; set; } 
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "Completed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled";
    public string CedarJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }

    public string? BlogSlug { get; set; }
    public bool IsBlogPublished { get; set; }
    public DateTime? BlogPublishedAt { get; set; }

    public string Tags { get; set; } = "";

    // Most recent successful Telegram send for this draft. Is used to cross-link the blog post
    // back to Telegram. Nullable means there was no post in Telegram yet
    public string? LastTelegramChatId { get; set; }
    public int? LastTelegramMessageId { get; set; }
    public string? LastTelegramUsername { get; set; }
}

public class DraftTranslation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public Draft? Draft { get; set; }
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public string CedarJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public long TelegramChatId { get; set; }
    public string? Username { get; set; }
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }
}

public class ChannelStatSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public int MemberCount { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
}

public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string LocalPath { get; set; } = "";
    public string? TelegramFileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string OwnerId { get; set; } = default!;
}

public class Reaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    
    /// <summary>
    /// null means whole-article
    /// </summary>
    public string? AnnotationId { get; set; }
    
    /// <summary>
    /// Like/Dislike
    /// </summary>
    public string Kind { get; set; } = ""; 
    public string VisitorHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    
    // null means whole-article
    public string? AnnotationId { get; set; }
    
    public string? AuthorName { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BotKnownChat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long TelegramChatId { get; set; }
    public string Title { get; set; } = "";
    public string? Username { get; set; }
    
    /// <summary>
    /// Channel, group, supergroup
    /// </summary>
    public string Type { get; set; } = "";
    
    public bool BotCanPost { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

public class BotKnownChatAdmin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BotKnownChatId { get; set; }
    public BotKnownChat? BotKnownChat { get; set; }
    public long TelegramUserId { get; set; }
}

public class ScheduledPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public string ChatId { get; set; } = "";
    public DateTime ScheduledAtUtc { get; set; }
    
    /// <summary>
    /// Pending, Sent, Failed
    /// </summary>
    public string Status { get; set; } = "Pending";
    
    public string? Error { get; set; }
    public int? MessageId { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Format { get; set; } = Consts.Html;
    public string Language { get; set; } = Languages.Primary;
}