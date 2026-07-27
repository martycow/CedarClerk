using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Admin panel access (IF2). A plain flag rather than ASP.NET Identity roles: there is one
    /// admin, and roles would add two tables and a join to express a single boolean. Granted at
    /// startup from Cedar:AdminEmail — the first admin cannot be made through the panel itself.
    /// See docs/admin-panel-scope.md.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Which invite code this account registered through (IF2, step 3). Null for accounts created
    /// before codes existed, or through the config fallback — that attribution genuinely cannot be
    /// recovered, so an admin can set it by hand instead of it reading "unknown" forever.
    /// </summary>
    public Guid? InviteCodeId { get; set; }

    public PlanTiers PlanTier { get; set; } = PlanTiers.Free;

    /// <summary>
    /// When the paid tier lapses (UTC). Null on a paid tier = manual grant, never expires.
    /// Effective tier is always Plans.Effective(PlanTier, PlanExpiresAt, now).
    /// </summary>
    public DateTime? PlanExpiresAt { get; set; }

    /// <summary>
    /// The $1/7-day Pro Plus trial can be used exactly once per account.
    /// </summary>
    public DateTime? TrialUsedAt { get; set; }

    /// <summary>
    /// Anti channel-cycling on Free: set when a Free user deletes a channel; connecting a
    /// DIFFERENT channel is blocked until this passes (same channel may reconnect freely).
    /// </summary>
    public DateTime? FreeChannelCooldownUntil { get; set; }
    public long? LastDeletedTelegramChatId { get; set; }

    /// <summary>
    /// Nullable means most accounts sign in with email/password and never link their Telegram account
    /// </summary>
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string? TelegramFirstName { get; set; }
    public DateTime? TelegramLinkedAt { get; set; }

    // Opt-in DM via the bot when a new comment or "like" reaction lands on this owner's blog
    // posts (see the ADR following ADR-039, docs/DECISIONS.md). Default false — a real opt-in,
    // not opt-out, so linking Telegram alone doesn't start sending unsolicited DMs.
    public bool NotifyOnEngagement { get; set; }

    /// <summary>
    /// Profile picture (IF1) — a /media/... path produced by the normal asset upload, so it goes
    /// through the same type whitelist, storage quota and public serving as post media. Null =
    /// the initial-letter avatar the app has always drawn.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// User-defined signature in the end of each post
    /// </summary>
    public string? PostSignature { get; set; }

    /// <summary>
    /// Text of the cross-links between a post's two homes (I15). Null falls back to the built-in
    /// wording. Profile-level rather than per-post: it is branding that reads the same on every
    /// post, and retyping it at each export would be a chore, not a choice.
    /// BlogLinkText is what the Telegram post says to reach the blog; TelegramLinkText is the
    /// reverse. A custom value is used as-is in both UI languages — it's the author's own words.
    /// </summary>
    public string? BlogLinkText { get; set; }
    public string? TelegramLinkText { get; set; }

    // Pro-only: makes the whole PostSignature text a clickable link — see Phase 8 Step 5,
    // docs/ROADMAP.md. Free-tier posts never read this; they get the fixed attribution instead.
    public string? PostSignatureUrl { get; set; }

    public string? StripeCustomerId { get; set; }

    // Header Slot System (blog-only, see docs/ROADMAP.md Phase 8 Step 4) — fixed profile values
    // shown by the AuthorSignature/Url/MapLocation slot types, distinct from PostSignature above.
    public string? AuthorDisplayName { get; set; }
    public string? ProfileUrl { get; set; }
    public string? ProfileLocation { get; set; }
    public HeaderSlotType? HeaderSlot1Type { get; set; }
    public HeaderSlotType? HeaderSlot2Type { get; set; }
    public HeaderSlotType? HeaderSlot3Type { get; set; }

    // Social profile links — purely informational/reference for now (Settings > Profile), not
    // yet wired into any blog/header display. Kept as individual named columns rather than a
    // JSON blob to match the existing flat-column convention for profile fields (AuthorDisplayName
    // etc. above).
    public string? SocialTwitterUrl { get; set; }
    public string? SocialInstagramUrl { get; set; }
    public string? SocialFacebookUrl { get; set; }
    public string? SocialYoutubeUrl { get; set; }
    public string? SocialGithubUrl { get; set; }

    // Editor redesign (ADR-035, docs/DECISIONS.md) — null always means "use the built-in
    // default", so existing accounts are unaffected until they opt in. JSON blobs rather than
    // flat columns: these are variable-length/growable preference bags, not a fixed field set
    // (contrast the flat SocialXxxUrl columns above, which ARE a fixed set).
    public string? ToolbarLayoutJson { get; set; }
    public string? AppearancePrefsJson { get; set; }
    public string? NewDraftDefaultsJson { get; set; }

    // Interface language (B26, ADR-044) — "ru"/"en". NOT the content language of a post: that
    // one is DraftTranslation.Language / Languages.cs. Null means the user never picked, and the
    // client falls back to the browser's language.
    public string? UiLanguage { get; set; }

    // High-water mark for "I've seen this feedback" (N8) — comments and reactions created after
    // it are highlighted as new. One timestamp rather than a per-comment seen table: the list is
    // ordered by date anyway, so a watermark answers the same question without a row per comment.
    // Null means nothing has been marked seen yet, so everything reads as new.
    public DateTime? FeedbackSeenAt { get; set; }
}

// Real invite codes (IF2, step 3). Registration used to check one shared string from config
// (Cedar:InviteCode), which is kept as a fallback so a database problem can't lock registration
// out entirely — see docs/admin-panel-scope.md.
//
// Codes are DEACTIVATED, never deleted: ApplicationUser.InviteCodeId points here, and deleting a
// row would silently erase the attribution of everyone who joined through it.
public class InviteCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The string typed at registration. Compared case-insensitively.</summary>
    public string Code { get; set; } = "";
    /// <summary>What this code is for ("Twitter launch", "for Sasha") — admin's own note.</summary>
    public string Label { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Null = unlimited. Uses are counted even after the cap, for the record.</summary>
    public int? MaxUses { get; set; }
    public int Uses { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Every state-changing action taken from the admin panel (IF2, step 2). Written from the start
// rather than added later: an audit log that begins halfway through is missing exactly the
// changes someone would go looking for.
//
// Actor and target emails are DENORMALIZED on purpose. A log that stops making sense once a row
// it points at changes or goes away is not a log — it has to read correctly years later without
// depending on joins that may no longer resolve.
public class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = default!;
    public string ActorEmail { get; set; } = "";
    /// <summary>Short machine-readable verb: plan, lock, unlock, reset-trial, grant-admin…</summary>
    public string Action { get; set; } = "";
    public string? TargetUserId { get; set; }
    public string? TargetEmail { get; set; }
    /// <summary>Human-readable "from X to Y" detail; never parsed, only displayed.</summary>
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
    /// pro, proplus, trial — see CedarClerk.Core.Plans
    /// </summary>
    public string Plan { get; set; } = "";
    
    /// <summary>
    /// Stripe session id, Telegram charge id, PayPal order id, etc. Is used to prevent duplicates
    /// </summary>
    public string? ExternalId { get; set; }
    
    public long Amount { get; set; } 
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "Completed";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-user, per-UTC-day counter of AI calls (auto-translate etc.) enforcing PlanQuotas.AiDailyLimit.
/// </summary>
public class AiUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public DateTime Day { get; set; } // UTC date (midnight)
    public int Count { get; set; }
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

    // Raw hit count on the blog post page, shared across RU/EN language versions (see ADR-023).
    // Not visitor-deduped, unlike Reaction — a plain running total.
    public int ViewCount { get; set; }

    public string Tags { get; set; } = "";

    // Most recent successful Telegram send for this draft. Is used to cross-link the blog post
    // back to Telegram. Nullable means there was no post in Telegram yet
    public string? LastTelegramChatId { get; set; }
    public int? LastTelegramMessageId { get; set; }
    public string? LastTelegramUsername { get; set; }

    // /drafts screen (ADR-035, docs/DECISIONS.md) — the only new *content* flag added for the
    // editor redesign; everything else there is a user preference, not draft state.
    public bool IsArchived { get; set; }

    // At most one folder per draft (see the ADR following ADR-038, docs/DECISIONS.md) — unlike
    // Tags, deliberately a plain scalar with no nav property/FK constraint, matching this
    // codebase's "no strict FK-only model" convention (docs/ARCHITECTURE.md). Null = unfiled.
    public Guid? FolderId { get; set; }

    // Gates the published blog page behind PostInvite tokens (see the ADR following ADR-040,
    // docs/DECISIONS.md) — only meaningful when IsBlogPublished is also true.
    public bool IsPrivate { get; set; }

    // Registration form shown to uninvited visitors of a private post (B3). Null = no form
    // configured, so an uninvited visitor still gets the original indistinguishable-from-404
    // response. A JSON blob rather than columns because the question list is variable-shape —
    // same reasoning as ApplicationUser's preference blobs; the server only length-checks it.
    public string? RegistrationFormJson { get; set; }
    // FI4.1 — the same form in the post's other languages: a JSON object keyed by language code,
    // each value a form blob shaped exactly like RegistrationFormJson above. Kept beside the
    // primary field rather than folding it into a map, so every existing post keeps working and
    // the common single-language case stays a single column read.
    public string? RegistrationFormTranslationsJson { get; set; }

    // Watermark text tiled over the rendered blog post (I7). Only applied to private posts —
    // the point is discouraging redistribution of something handed out per-invite. Null/empty =
    // no watermark. Plain text, never markup: it is HTML-escaped at render like any author text.
    public string? WatermarkText { get; set; }
}

// One row per invited email per private Draft. Token grants access (via a long-lived cookie
// once presented) until the row is deleted — deleting revokes immediately. No nav
// property/FK constraint, matching Draft.FolderId's convention above.
public class PostInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public string Email { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// One row per registration-form submission on a private post (B3). Kept separate from
// PostInvite rather than folded into it: the field sets barely overlap, and the owner's
// invite list would otherwise have to render half-empty rows of a different kind.
// AnswersJson holds the custom questionnaire answers keyed by question id.
public class PostRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public string? Name { get; set; }
    public string? Nickname { get; set; }
    public string? Email { get; set; }
    public string? SocialLink { get; set; }
    public string? AnswersJson { get; set; }
    // Same IP-derived hash used for reaction dedup — here it only backs the per-post
    // submission throttle, so a public form can't be used to flood the owner's list.
    public string VisitorHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Per-owner, per-draft baseline the /drafts activity delta is measured against (B23). Written
// by DraftEndpoints' list query, one row per (OwnerId, DraftId), overwritten in place — this is
// not a stats history, and it can't be used to backfill one.
//
// Two pairs of counters, because "since the previous session" and "since the last page load"
// are different things: Baseline* is what the shown delta is measured against, Last* is the
// counters as of the most recent load. When a load comes in more than
// Consts.DraftActivity.SessionGap after the previous one, the session is considered new and
// Baseline* takes on Last* — i.e. the counters as they stood when the owner last had the screen
// open. Inside a session only Last*/SeenAt move, so an F5 doesn't wipe the numbers.
public class DraftStatSeen
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid DraftId { get; set; }
    public int BaselineViewCount { get; set; }
    public int BaselineReactionCount { get; set; }
    public int LastViewCount { get; set; }
    public int LastReactionCount { get; set; }
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
}

// A named, reusable registration-form definition (N12). Holds the exact same client-authored
// blob shape as Draft.RegistrationFormJson — applying a preset copies the blob onto the draft,
// it does not link to it, so editing a preset later never silently rewrites a published post's
// form. Per owner, like Folder.
public class FormPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Name { get; set; } = "";
    public string FormJson { get; set; } = "";
    // FI4.1 — a preset is written in one language; a post published in several attaches one per
    // language. Translating the *questions* automatically was rejected: a form's wording is the
    // owner's voice talking to their reader, and a machine translation of it is not.
    public string Language { get; set; } = Languages.Primary;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// A real, named, user-managed entity (create/rename/delete) — unlike Tags, which stay a flat
// unmanaged string. See the ADR following ADR-038, docs/DECISIONS.md.
public class Folder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

    // Snapshot of the RU draft's CedarJson at the moment this translation was last synced
    // (manual save or auto-translate) — lets the editor diff "what changed in RU since" at the
    // block level instead of just a stale/not-stale boolean. Null for translations that predate
    // this column (existing rows) or were never resynced since — falls back to the boolean
    // staleness indicator in that case. See ADR in docs/DECISIONS.md.
    public string? SourceSnapshotJson { get; set; }
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

    // Aggregated across every draft ever published to this channel (see ChannelPost) —
    // an approximation, not a true per-channel split: a draft sent to multiple channels
    // contributes its full totals to each. See ADR-025, docs/DECISIONS.md.
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }

    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
}

// Daily per-owner blog totals (views/likes/comments across ALL of that owner's blog-published
// drafts) — the channel-agnostic counterpart to ChannelStatSnapshot, since blog views aren't
// intrinsically tied to any one Telegram channel. No MemberCount equivalent (a blog has no
// "subscriber" concept). Written by the same SnapshotChannelStatsJob. See ADR in docs/DECISIONS.md.
public class BlogStatSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
}

// Append-only log of every successful send, written by PostEndpoints.PublishAsync. Lets the
// stats snapshot job know which drafts (and therefore which views/likes/comments) belong to
// which channel — Draft only tracks its single *most recent* Telegram send otherwise.
public class ChannelPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid DraftId { get; set; }
    public int TelegramMessageId { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
}

public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string LocalPath { get; set; } = "";
    public string? TelegramFileId { get; set; }

    // Filename (bare, same MediaPaths.Dir as LocalPath) of a resized/recompressed JPEG derivative
    // generated at upload time when the original exceeds Consts.FileSizes.TelegramSafeImageBytes —
    // Telegram rejects large photos fetched by URL (see ADR in docs/DECISIONS.md). Null means the
    // original is already small enough to send to Telegram as-is. Blog/.cedar export always use
    // LocalPath (the untouched original); only PostEndpoints.PublishAsync substitutes this one in.
    public string? TelegramLocalPath { get; set; }

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

    // One level of nesting only (Phase 8 Step 7) — a reply's ParentCommentId always points at a
    // top-level comment, never at another reply; the UI never offers a reply-to-reply action.
    public Guid? ParentCommentId { get; set; }
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
    public string Format { get; set; } = Consts.ContentTypes.Markdown;
    public string Language { get; set; } = Languages.Primary;
}