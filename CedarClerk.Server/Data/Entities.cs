using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server.Data;

public class ApplicationUser : IdentityUser
{
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

public class ScheduledPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public string ChatId { get; set; } = "";
    public DateTime ScheduledAtUtc { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
    public string? Error { get; set; }
    public int? MessageId { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Format { get; set; } = "Html"; // Html, Markdown
}