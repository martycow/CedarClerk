using CedarClerk.Core;
using CedarClerk.Localization;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The tenant name this account's public blog answers at — <c>marty.cedarclerk.app</c>. Stored
    /// lowercase; <see cref="Usernames"/> holds the rules both this and the Host resolver obey.
    ///
    /// Deliberately not called <c>Username</c>: Identity already owns <c>UserName</c> (which holds
    /// the email here), and EF's migration differ matched the two case-insensitively and generated a
    /// column rename that would have emptied every account's login identity.
    /// Null means the account has no subdomain, which is every account that predates this column.
    /// </summary>
    public string? TenantUsername { get; set; }

    /// <summary>
    /// Admin panel access (IF2). A plain flag rather than ASP.NET Identity roles: there is one
    /// admin, and roles would add two tables and a join to express a single boolean. Granted at
    /// startup from Cedar:AdminEmail — the first admin cannot be made through the panel itself.
    /// See ADR-122 in docs/DECISIONS.md.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Which invite code this account registered through (IF2, step 3). Null for accounts created
    /// before codes existed, or through the config fallback — that attribution genuinely cannot be
    /// recovered, so an admin can set it by hand instead of it reading "unknown" forever.
    /// </summary>
    public Guid? InviteCodeId { get; set; }

    /// <summary>
    /// ADR-108 — this account's id on the installation that authenticated it. **Vestigial: nothing
    /// reads or writes it since ADR-117 retired the desktop's local database**, and the
    /// <c>UpstreamAuth</c> code that filled it is deleted.
    ///
    /// The column stays anyway, and deliberately. Dropping one in SQLite rebuilds the whole table, and
    /// this is <c>AspNetUsers</c> — which Identity's security-stamp validation touches on **every
    /// authorized request**. That is exactly the shape of the `no such column` incident recorded in
    /// `.claude/rules/ef-migrations.md`. An unused nullable column costs nothing; a table rebuild on a
    /// live database for tidiness is a risk with no return.
    /// </summary>
    public string? RemoteUserId { get; set; }

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

    /// FI5 — a signature is read at the bottom of whichever language's post it is, so like the
    /// cross-link labels below it belongs to the language too. Same split as those: the primary
    /// wording stays in PostSignature above, the rest is a JSON object keyed by language code
    /// (LocalizedTextMap). Gated by the same Pro check as PostSignature itself.
    public string? PostSignatureTranslationsJson { get; set; }

    /// <summary>
    /// Text of the cross-links between a post's two homes (I15). Null falls back to the built-in
    /// wording. Profile-level rather than per-post: it is branding that reads the same on every
    /// post, and retyping it at each export would be a chore, not a choice.
    /// BlogLinkText is what the Telegram post says to reach the blog; TelegramLinkText is the
    /// reverse. A custom value is used as-is in both UI languages — it's the author's own words.
    /// </summary>
    public string? BlogLinkText { get; set; }
    public string? TelegramLinkText { get; set; }
    /// A cross-link is read by whoever is reading that language's version of the post, so the
    /// label belongs to the language too. The primary-language wording stays in the two fields
    /// above; these hold the rest as a JSON object keyed by language code (LocalizedTextMap).
    public string? BlogLinkTextTranslationsJson { get; set; }
    public string? TelegramLinkTextTranslationsJson { get; set; }

    // Pro-only: makes the whole PostSignature text a clickable link — see Phase 8 Step 5,
    // docs/tasks/ROADMAP.md. Free-tier posts never read this; they get the fixed attribution instead.
    public string? PostSignatureUrl { get; set; }

    public string? StripeCustomerId { get; set; }

    // Header Slot System (blog-only, see docs/tasks/ROADMAP.md Phase 8 Step 4) — fixed profile values
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
    public string? SocialTelegramUrl { get; set; }
    public string? SocialThreadsUrl { get; set; }
    public string? SocialBlueskyUrl { get; set; }
    public string? SocialRedditUrl { get; set; }
    // The two an indie developer publishes a build on, which is what makes them belong on a fixed
    // list beside the five social networks rather than in a free-form row (Phase 13).
    public string? SocialSteamUrl { get; set; }
    public string? SocialItchUrl { get; set; }

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
// out entirely — see ADR-122 in docs/DECISIONS.md.
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
/// ADR-092 — one movement of the prepaid credit wallet: +Delta on a pack purchase, -Delta on a
/// paid publish. The balance is SUM(Delta) per owner — a ledger, not a mutable counter, so every
/// balance has an audit trail and a charge can be made idempotent by (Reason, Ref).
/// </summary>
public class CreditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public int Delta { get; set; }

    /// <summary>purchase, x-post, admin-grant — see CedarClerk.Core.CreditReasons</summary>
    public string Reason { get; set; } = "";

    /// <summary>
    /// What this movement is anchored to: the payment's ExternalId for a purchase, the PublishJob
    /// id for a charge. (Reason, Ref) is unique, which is what makes a retried charge a no-op.
    /// </summary>
    public string? Ref { get; set; }

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
    // The document stored directly on Draft is the canonical version. Existing rows default to
    // Russian, but a new draft may choose any supported content language (ADR-064).
    public string PrimaryLanguage { get; set; } = Languages.Russian;
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
    // codebase's "no strict FK-only model" convention (docs/tech/ARCHITECTURE.md). Null = unfiled.
    public Guid? FolderId { get; set; }

    // ADR-125 — series membership, same plain-scalar convention as FolderId. The pair is set and
    // cleared together: attach assigns SeriesOrder = max+1 among the members, detach nulls both,
    // so a stale zero can never make an unattached post claim first place in someone's series.
    public Guid? SeriesId { get; set; }
    public int? SeriesOrder { get; set; }

    // ADR-128 — the document tree (ГДД structure). Plain scalar, no FK, same convention as
    // FolderId; null = root. Deleting a node lifts its children to the grandparent — documents
    // outlive structure. Depth is capped at 10 and cycles are refused by the endpoint.
    public Guid? ParentDraftId { get; set; }
    public int SiblingOrder { get; set; }

    // ADR-102 — what kind of document this is (CedarClerk.Core.DocumentTypes). The default is what
    // makes this a column instead of a migration: every row written before types existed IS a post,
    // so there is nothing to backfill. A Draft was never "a post" in the code — it is a TipTap
    // document with autosave, revisions, translations, tags and a folder, and a design doc or a
    // script needs all of that verbatim.
    public string DocumentType { get; set; } = DocumentTypes.Post;

    // ADR-102 — which Project this document belongs to. Plain scalar, no nav property and no FK,
    // exactly like FolderId above and for the same recorded reason. Null = not in a project, which
    // is what every draft that predates the module is.
    public Guid? ProjectId { get; set; }

    // Gates the published blog page behind PostInvite tokens (see the ADR following ADR-040,
    // docs/DECISIONS.md) — only meaningful when IsBlogPublished is also true.
    public bool IsPrivate { get; set; }

    // NF1 (docs/DECISIONS.md, ADR following ADR-055): a post template. Named and edited exactly
    // like any other Draft (same autosave, same export), just filtered into its own /drafts tab
    // instead of the main list and never itself published — the "cheapest honest shape" per
    // Marty's own framing in docs/tasks/BACKLOG.md, a flag rather than a parallel entity.
    public bool IsTemplate { get; set; }

    // Registration form shown to uninvited visitors of a private post (B3). Null = no form
    // configured, so an uninvited visitor still gets the original indistinguishable-from-404
    // response. A JSON blob rather than columns because the question list is variable-shape —
    // same reasoning as ApplicationUser's preference blobs; the server only length-checks it.
    // Idea #4 - the name in the drafts list and the headline the reader sees are two different
    // things: "devlog 14 (final final)" is a useful name and a bad title. Null means they are the
    // same, which is what every draft written before this field existed means.
    //
    // Only the primary language needs it: a translation already carries its own DraftTranslation
    // .Title, which has always been the article title for that language.
    public string? ArticleTitle { get; set; }

    // Semi-public: a private post that still appears in the blog index and its tag filters, with
    // a lock on the card. Clicking it lands on the registration gate exactly as before — this
    // changes what is *advertised*, never what is *readable*. Meaningless unless IsPrivate.
    public bool IsListedWhilePrivate { get; set; }

    // Blocks text selection, copy/cut and the context menu on the rendered blog page. Same
    // redistribution-discouraging family as WatermarkText — a deterrent, not protection (the
    // page source is still one Ctrl+U away) — and like it, only applied to private posts.
    public bool DisableCopy { get; set; }

    /// <summary>
    /// T-039 — an informational post that nobody is invited to react to. Two flags rather than one
    /// "engagement off": a post can reasonably take likes but not a discussion, and the reverse is
    /// just as reasonable. Existing rows default to false, i.e. everything stays as it is.
    /// </summary>
    public bool DisableReactions { get; set; }
    public bool DisableComments { get; set; }

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

    /// <summary>
    /// Wave 1 item 8 — the shareable read-only preview link's credential. One active link per
    /// draft: creating again rotates the token, null revokes. The token IS the access — 24 random
    /// url-safe bytes (<see cref="PrivateAccess.NewToken"/>), unguessable on its own, so the page
    /// it opens needs no account and no cookie.
    /// </summary>
    public string? PreviewToken { get; set; }

    // Wave 2 item 10 — membership in the evergreen pool FillQueueSlotsJob picks from. A flag plus
    // its own limits rather than a pool entity: the draft IS the content, and everything here is
    // an answer to "may this still be picked". SendCount is incremented by the publish job when a
    // slot-filled ScheduledPost actually goes Sent, never at fill time.
    public bool IsEvergreen { get; set; }
    public string EvergreenCategory { get; set; } = "";
    public int? EvergreenMaxSends { get; set; }
    public DateTime? EvergreenUntil { get; set; }
    public int EvergreenSendCount { get; set; }

    /// <summary>
    /// Wave 2 item 17 — up to three URL buttons appended under the Telegram post, as a JSON array
    /// of <c>{ "text", "url" }</c> (max 3, text ≤ 32 chars, http/https only — validated on save).
    /// A per-post setting, not an editor node: the buttons are wire-level Telegram furniture and
    /// never appear on the blog or in the document itself. Null means no buttons.
    /// </summary>
    public string? CtaButtonsJson { get; set; }
}

// One row per invited email per private Draft. Token grants access (via a long-lived cookie
// once presented) until the row is deleted — deleting revokes immediately. No nav
// property/FK constraint, matching Draft.FolderId's convention above.
public class PostInvite
{
    /// <summary>Denormalized from the post it grants access to, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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
    /// <summary>Denormalized from the post it registered for, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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

    /// <summary>
    /// T-064/T-023 — this reader's own key to the post. Handed back in the redirect after the form
    /// is submitted, so the access travels with the link instead of living only in the cookie jar
    /// of the browser that filled it in: the incident behind this row was a reader who opened the
    /// post in Telegram's in-app browser, then in Chrome, and was asked to register twice.
    ///
    /// Per registration rather than per post, which is what makes it revocable without locking out
    /// everyone else.
    /// </summary>
    public string AccessToken { get; set; } = "";

    /// <summary>Set by the owner to withdraw this reader's access; the row stays for the record.</summary>
    public bool IsRevoked { get; set; }

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
    public string Language { get; set; } = Languages.Russian;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// Idea #11 — a term the owner defines once and has explained wherever it appears on the blog.
// Per-owner, and per content language: the same word needs a different explanation depending on
// which language's version of a post the reader is on, and a Russian description under an English
// article would be worse than no tooltip at all.
public class GlossaryTerm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Term { get; set; } = "";
    public string Description { get; set; } = "";
    // Comma-separated other spellings. Russian inflects, so "рендерер" shows up as "рендерера"
    // and "рендереру"; listing the forms beats guessing at per-language stemming rules.
    public string Aliases { get; set; } = "";
    // A /media/... path from the ordinary asset upload, exactly like ApplicationUser.AvatarUrl —
    // same whitelist, same quota, same public serving, no second pipeline.
    public string? ImageUrl { get; set; }
    public string Language { get; set; } = Languages.Russian;
    /// <summary>
    /// Off by default — a term at the start of a sentence is the same term. On where the casing is
    /// the meaning, which is the case Marty asked for: "IT" the industry against "it" the pronoun.
    /// </summary>
    public bool IsCaseSensitive { get; set; }
    /// <summary>
    /// The term this one was translated from — the *root* of the group, not the immediate source,
    /// so every language version of one idea shares a single value. Null on a hand-written term,
    /// which is then its own root. Added 01.08.2026 so the glossary's preview can switch languages:
    /// translations are separate rows keyed by translated text (ADR-061) and nothing connected them,
    /// so "show me this term in English" had no answer. Existing translated rows stay unlinked —
    /// nothing recorded where they came from, and guessing by text would link the wrong pairs.
    /// </summary>
    public Guid? SourceTermId { get; set; }
    /// <summary>
    /// T-125 (ADR-112) — the project this term belongs to, or null for a global one. A plain
    /// scalar with no FK, like <c>Draft.ProjectId</c>.
    ///
    /// A document sees global terms **plus** its own project's: "Unity" is global, "the ferry" is
    /// about one game, and an article about that game needs both. A project's term never shows up
    /// outside it — otherwise "local" would mean nothing.
    /// </summary>
    public Guid? ProjectId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ADR-197 — one local publishing exception. A term remains in the owner's glossary, but this
// document-language pair can deliberately omit it without changing any other document.
public class DraftGlossaryExclusion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid DraftId { get; set; }
    public Guid GlossaryTermId { get; set; }
    public string Language { get; set; } = Languages.Russian;
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

// ADR-125 — a post series ("Devlog #1..#N"). An entity rather than a special tag because the
// series page needs a stable slug and "part N of M" needs an order, and a flat tag string has
// neither. Slug is unique per owner; deleting a series unassigns its drafts.
public class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ADR-128 — a wiki-link derived from the primary document's text, one row per (from, to) pair,
// re-diffed on every save. Deliberately separate from the module's EntityLink: that one is a
// *stated* relation and requires a ProjectId; this one is *derived* and lives outside projects.
public class DocumentLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid FromDraftId { get; set; }
    public Guid ToDraftId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class DraftTranslation
{
    /// <summary>Denormalized from the document it translates, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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
    public string? SourceLanguage { get; set; }
}

// Immutable, per-language content history. A row is written on every explicit content save and
// when a version is published, so the UI can show both edit history and a safe publish diff.
public class DraftRevision
{
    /// <summary>Denormalized from the document it is a version of, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public string Language { get; set; } = Languages.Russian;
    public string Title { get; set; } = "";
    public string CedarJson { get; set; } = "{}";
    public string Kind { get; set; } = "save"; // save | telegram | blog
    public string? Destination { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public long TelegramChatId { get; set; }
    public string? Username { get; set; }
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }

    /// <summary>
    /// The channel's own picture, copied down from Telegram and stored under the media directory
    /// (a path relative to it, as Asset.LocalPath is). Telegram's file ids expire, so what is kept
    /// is the file and not the id.
    /// </summary>
    public string? AvatarPath { get; set; }

    public DateTime? AvatarFetchedAt { get; set; }

    // Wave 2 item 11 — this channel's own signature trio, overriding the owner-level one on
    // ApplicationUser when PostSignature is non-null. Same three-column shape as the profile's
    // (primary wording + LocalizedTextMap JSON + optional link), so the existing resolution flow
    // reads either set without a second code path. Plan gating is unchanged either way.
    public string? PostSignature { get; set; }
    public string? PostSignatureTranslationsJson { get; set; }
    public string? PostSignatureUrl { get; set; }

    /// <summary>
    /// The message this channel's scheduled auto-pin currently holds pinned, so pinning the next
    /// post can best-effort unpin the previous one instead of stacking pins forever.
    /// </summary>
    public int? LastPinnedMessageId { get; set; }
}

/// <summary>
/// Wave 2 item 10 — one weekly posting slot on one destination. A slot names a moment of the week
/// (UTC), and FillQueueSlotsJob keeps its upcoming occurrences filled from the owner's evergreen
/// pool. One destination per slot on purpose: two channels means two slots, not a channel-set
/// entity nothing else needs.
/// </summary>
public class QueueSlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;

    /// <summary>The destination, as a <see cref="PublishTarget"/> — any network, like ScheduledPost.</summary>
    public Guid TargetId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Which evergreen drafts may fill this slot; empty means any category.</summary>
    public string Category { get; set; } = "";

    /// <summary>.NET convention: 0 = Sunday.</summary>
    public int DayOfWeek { get; set; }

    /// <summary>Minutes after UTC midnight, 0-1439. UTC in storage; the client renders local.</summary>
    public int TimeUtcMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Wave 2 item 15 — one named invite link the bot created for one channel, so joins arriving via
/// chat_member updates can be attributed to it. Revoked links keep their row: the daily tallies
/// they attributed still point here.
/// </summary>
public class ChannelInviteLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ChannelId { get; set; }
    public string Name { get; set; } = "";

    /// <summary>The t.me/+… url exactly as Telegram returned it — the join update's match key.</summary>
    public string InviteLink { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Wave 2 item 15 — joins and leaves per (owner, channel, UTC day, invite link). A DAILY
/// AGGREGATE, never a per-user event log — the BlogViewGeoDaily privacy shape: the page only ever
/// asks "how many". Null InviteLinkId is the organic/unattributed row; leave updates carry no
/// invite link from Telegram, so leaves always land there.
/// </summary>
public class ChannelMemberDaily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid ChannelId { get; set; }

    /// <summary>UTC date (midnight).</summary>
    public DateTime Day { get; set; }

    public Guid? InviteLinkId { get; set; }
    public int Joins { get; set; }
    public int Leaves { get; set; }
}

/// <summary>
/// Wave 2 item 16 — one short redirect (/l/{code}) the owner hands out instead of a raw URL.
/// Clicks are counters, not visit logs — no per-visitor anything is kept, and every hit counts,
/// bots included; the UI says so rather than pretending to filter them.
/// </summary>
public class TrackedLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;

    /// <summary>8-char base62, unique across the installation — the whole address.</summary>
    public string Code { get; set; } = "";

    public string Url { get; set; } = "";

    /// <summary>The draft this link promotes, when it promotes one; plain scalar, no FK.</summary>
    public Guid? DraftId { get; set; }

    /// <summary>Which network the link was made for, when the owner said (<see cref="CedarClerk.Core.PublishNetworks"/>).</summary>
    public string? Network { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int ClickCount { get; set; }
    public DateTime? LastClickAt { get; set; }
}

/// <summary>
/// Wave 2 item 16 — clicks per (link, UTC day), the series behind a per-day bar. Same
/// aggregate-only stance as <see cref="ChannelMemberDaily"/>.
/// </summary>
public class TrackedLinkClickDaily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrackedLinkId { get; set; }
    public string OwnerId { get; set; } = default!;

    /// <summary>UTC date (midnight).</summary>
    public DateTime Day { get; set; }

    public int Clicks { get; set; }
}

public class ChannelStatSnapshot
{
    /// <summary>Denormalized from the channel it measures, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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

    // ADR-205 — the channel's own numbers, summed over its ChannelPosts. New columns rather than a
    // new meaning for the two above: those hold ADR-025's blog attribution, and redefining a stored
    // column would put a cliff in the middle of an existing chart at the day the meaning changed.
    public int TelegramReactionCount { get; set; }
    public int TelegramCommentCount { get; set; }

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

/// <summary>
/// Daily rollup of blog views split by reader country and reader language (Marty, 08.08.2026).
/// One row per (owner, UTC day, country, language) — an aggregate, never a per-visit log: the
/// page only ever asks "how many", so storing individual visits would buy nothing and turn a
/// counter into a visitor trail. Country comes from Cloudflare's CF-IPCountry header (the tunnel
/// is the only way in, see .claude/rules/production-environment.md), language from the primary
/// subtag of Accept-Language — the reader's own language, not which translation was served.
///
/// **History starts the day this ships** — same as DraftStatSnapshot, and for the same reason:
/// ViewCount is a running total with no dimensions in it, so there is nothing to backfill from.
/// </summary>
public class BlogViewGeoDaily
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = "";
    public DateTime Day { get; set; }
    // ISO-3166-1 alpha-2, uppercase. Consts.General.UnknownGeo when Cloudflare didn't say.
    public string Country { get; set; } = "";
    // Primary subtag, lowercase ("ru", "en", "de"). Consts.General.UnknownGeo when absent.
    public string Language { get; set; } = "";
    public int ViewCount { get; set; }
}

// Append-only log of every successful send, written by PostEndpoints.PublishAsync. Lets the
// stats snapshot job know which drafts (and therefore which views/likes/comments) belong to
// which channel — Draft only tracks its single *most recent* Telegram send otherwise.
public class ChannelPost
{
    /// <summary>Denormalized from the channel it was sent to, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid DraftId { get; set; }
    public int TelegramMessageId { get; set; }
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    // ADR-205 — what the bot has SEEN on this post in Telegram, which is not the same as what
    // Telegram holds: there is no call that asks for a post's current reactions, so an update
    // missed while the service was down is missed for good. Both are floors, never totals.
    public int ReactionCount { get; set; }
    public int CommentCount { get; set; }

    /// <summary>When an update last moved either number; null while the bot has seen none.</summary>
    public DateTime? StatsSeenAt { get; set; }
}

/// <summary>
/// Daily per-draft totals — the series behind the growth chart on a post's details (8.6, Marty
/// 01.08.2026). The counterpart to ChannelStatSnapshot (per channel) and BlogStatSnapshot (per
/// owner); neither could answer "how did THIS post do", which is why B23's sparkline was dropped
/// as unbuildable back in ADR-043.
///
/// **History starts the day this ships.** Nothing recorded per-draft numbers before, and the
/// current counters are running totals with no timestamps in them, so there is nothing to
/// backfill from — the chart is empty until the nightly job has run at least twice.
/// </summary>
public class DraftStatSnapshot
{
    /// <summary>Denormalized from the post it measures, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public int ViewCount { get; set; }
    public int LikeCount { get; set; }
    public int DislikeCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;
}

public class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string LocalPath { get; set; } = "";
    // ADR-089 — the bot-scoped file_id minted by the pre-upload (a column the initial schema
    // carried unused until 01.08.2026), plus which file it was minted FOR: the original or a
    // compressed derivative. A regenerated derivative must re-upload, not serve stale bytes.
    public string? TelegramFileId { get; set; }
    public string? TelegramFileIdSourcePath { get; set; }

    // Filename (bare, same MediaPaths.Dir as LocalPath) of a resized/recompressed JPEG derivative
    // generated at upload time when the original exceeds Consts.FileSizes.TelegramSafeImageBytes —
    // Telegram rejects large photos fetched by URL (see ADR in docs/DECISIONS.md). Null means the
    // original is already small enough to send to Telegram as-is. Blog/.cedar export always use
    // LocalPath (the untouched original); only PostEndpoints.PublishAsync substitutes this one in.
    public string? TelegramLocalPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string OwnerId { get; set; } = default!;

    /// <summary>
    /// ADR-204 — the project whose documents use this file. Null is an answer, not a gap: a
    /// glossary illustration or a signature image belongs to no project and the library shows that
    /// as its own bucket. The first project to claim a file keeps it.
    /// </summary>
    public Guid? ProjectId { get; set; }
}

public class Reaction
{
    /// <summary>Denormalized from the post it was left on, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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

// NF5 — one vote per (poll, visitor); switching an answer updates the existing row rather than
// adding a second one. Same anonymous VisitorHash approach as Reaction (ADR-016) — polls are a
// blog-only content block (docs/DECISIONS.md ADR following ADR-054), not a Telegram feature, so
// there is no equivalent on that surface to keep in sync.
public class PollVote
{
    /// <summary>Denormalized from the post it was cast on, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }

    /// <summary>The poll node's own `id` attr — a post can contain more than one poll.</summary>
    public string PollId { get; set; } = "";
    public string Option { get; set; } = "";
    public string VisitorHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Comment
{
    /// <summary>Denormalized from the post it was left on, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

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

    /// <summary>
    /// Telegram's chat id. Still written for Telegram rows — the ownership check and the channel
    /// name in the Posts Manager both read it — but it stopped being the destination's identity
    /// when scheduling became network-agnostic (ADR-099). Empty for every other network.
    /// </summary>
    public string ChatId { get; set; } = "";

    /// <summary>
    /// The destination as a <see cref="PublishTarget"/> — what lets a post be scheduled for any
    /// network rather than Telegram only. Null means a row written before this column: those
    /// publish through <see cref="ChatId"/>, which is the only thing they carry.
    /// </summary>
    public Guid? TargetId { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.PublishNetworks"/>, denormalised so the list reads without a join.</summary>
    public string Network { get; set; } = PublishNetworks.Telegram;

    public DateTime ScheduledAtUtc { get; set; }
    
    /// <summary>
    /// Pending, Sent, Failed
    /// </summary>
    public string Status { get; set; } = "Pending";
    
    public string? Error { get; set; }
    public int? MessageId { get; set; }
    public string OwnerId { get; set; } = default!;
    public string Format { get; set; } = Consts.ContentTypes.Markdown;
    public string Language { get; set; } = Languages.Russian;

    /// <summary>
    /// Wave 2 item 10 — which <see cref="QueueSlot"/> occurrence this row fills, or null for an
    /// ordinary hand-scheduled post. (SlotId, ScheduledAtUtc) is how the fill job knows an
    /// occurrence is already taken, whatever its status.
    /// </summary>
    public Guid? SlotId { get; set; }

    /// <summary>Wave 2 item 11 — send with disable_notification.</summary>
    public bool Silent { get; set; }

    /// <summary>Wave 2 item 11 — pin after a successful send, unpinning the channel's previous auto-pin.</summary>
    public bool PinAfterSend { get; set; }
}

/// <summary>
/// One publication of one draft to one target, as a durable row (T-090, ADR-081).
///
/// A table rather than <c>AiJobService</c>'s in-memory dictionary, and the difference is the point:
/// an AI job lost to a restart costs a retry, while a publish lost to a restart is a post that may
/// or may not exist on someone's channel. Durability here is not about convenience — it is what
/// makes "did this go out?" answerable at all.
/// </summary>
public class PublishJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid DraftId { get; set; }
    public Guid TargetId { get; set; }
    public string Network { get; set; } = "";
    public string Language { get; set; } = Languages.Russian;

    /// <summary>Pending → Running → Succeeded | Failed | Unknown. See PublishJobStatus.</summary>
    public string Status { get; set; } = PublishJobStatus.Pending;

    /// <summary>
    /// T-106 — the thread this part belongs to, and where in it. Null/0/1 is an ordinary single
    /// message. One job per part rather than one job per thread, because a thread that fails on
    /// part four must be resumable at part four: retrying the whole thing would send parts one to
    /// three a second time, and a channel cannot un-see them.
    /// </summary>
    public Guid? ThreadId { get; set; }
    public int PartIndex { get; set; }
    public int PartCount { get; set; } = 1;

    public int Attempts { get; set; }
    public string? Error { get; set; }
    /// <summary>What the network called the thing it created — a message id, an at:// URI.</summary>
    public string? RemoteId { get; set; }
    public string? PublicUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>When a runner claimed it. Also what decides that a Running job has been abandoned.</summary>
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>Earliest a failed-but-retryable job may be tried again (backoff).</summary>
    public DateTime? NextAttemptAt { get; set; }
}

public static class PublishJobStatus
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";

    /// <summary>
    /// The honest status for a job that was sending when the process died: the request left, and
    /// nothing here knows whether the network accepted it. It is NOT retried — a blind retry is how
    /// one post becomes two — and the owner is told to look.
    /// </summary>
    public const string Unknown = "Unknown";
}

/// <summary>
/// The author's own text for one network (T-087, ADR-077). Per (draft, network, language), because
/// a cross-post is a standalone post — Marty's answer to Q-14 — and a post written in two languages
/// needs its own short version in each.
///
/// Absent means "derive one": publishing must never block on writing a second version of the post,
/// so the fallback teaser is what goes out (BlueskyPostBuilder).
/// </summary>
public class DraftTargetText
{
    /// <summary>Denormalized from the document it belongs to, so the owner filter can reach this row
    /// directly instead of through a join it has no index for.</summary>
    public string OwnerId { get; set; } = default!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    /// <summary>One of <see cref="CedarClerk.Core.PublishNetworks"/> — the network, not one account of it.</summary>
    public string Network { get; set; } = "";
    public string Language { get; set; } = Languages.Russian;
    public string Text { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A tenant's connected account on one network — the general home for "where a post can go"
/// (T-084, ADR-078). Telegram channels are projected into this table in T-085; <see cref="Channel"/>
/// stays as the Telegram-specific detail row behind them, because ChannelPost, ChannelStatSnapshot
/// and BotKnownChat all key off it and none of them generalise.
/// </summary>
public class PublishTarget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public ApplicationUser? Owner { get; set; }

    /// <summary>One of <see cref="CedarClerk.Core.PublishNetworks"/>.</summary>
    public string Network { get; set; } = "";

    /// <summary>What the author sees in a picker: a channel title, a @handle.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The network's own stable identity for the account — a Telegram chat id, a Bluesky DID.
    /// A DID rather than a handle on purpose: handles are renameable and the identity must not be.
    /// </summary>
    public string RemoteId { get; set; } = "";

    /// <summary>
    /// Credentials, encrypted by <c>PublishTargetSecrets</c>. Null where a network needs none —
    /// Telegram posts through Cedar Clerk's own bot, so there is nothing of the tenant's to keep.
    /// The shape inside is the network's business; each target parses its own.
    /// </summary>
    public string? CredentialsProtected { get; set; }

    /// <summary>
    /// False keeps the row (and its history) while stopping it being offered or published to —
    /// same deactivate-don't-delete choice InviteCode made, and for the same reason: a deleted row
    /// takes the answer to "where did this post go" with it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastPublishedAt { get; set; }

    /// <summary>
    /// Why the last publish failed, kept so a broken connection is visible in the UI before the
    /// next send rather than after it. Cleared on success.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>Set for Telegram rows only — the detail table described above.</summary>
    public Guid? ChannelId { get; set; }
}

/// <summary>
/// One waitlist signup from the landing (T-154, ADR-135) — an email, the page's language and when.
/// Deliberately nothing else: the list's one purpose is a launch announcement, and every extra
/// column would be PII collected before there is even an account to attach it to.
/// </summary>
public class WaitlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = "";

    /// <summary>Which language the landing rendered in — what language to announce in.</summary>
    public string Language { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Wave 1 item 7 — an address subscribed to one owner's whole blog: <see cref="ShowcaseFollower"/>
/// generalized to the blog root, as its own table because a follower belongs to a project and a
/// subscriber to a tenant, and folding the two would make every query explain which it meant.
/// Same shape on purpose: an address and two tokens, not an account, double opt-in throughout.
/// </summary>
public class BlogSubscriber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;

    /// <summary>Stored lowercase, so the unique index needs no collation of its own.</summary>
    public string Email { get; set; } = "";

    /// <summary>Null once confirmed. An unconfirmed row is never mailed anything but its own
    /// confirmation.</summary>
    public string? ConfirmToken { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    /// <summary>In every mail. Per row, so leaving needs no account and no reply.</summary>
    public string UnsubscribeToken { get; set; } = "";

    /// <summary>Who asked, one-way hashed — the per-visitor ceiling needs something to count.</summary>
    public string VisitorHash { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Wave 1 item 7 — one notify-on-publish run, as a durable row so a slow mail burst can never
/// block (or outlive) the publish that caused it. Same durability argument as <see cref="PublishJob"/>:
/// "did the subscribers hear about this post" has to stay answerable across a restart.
/// </summary>
public class BlogNotifyJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerId { get; set; } = default!;
    public Guid DraftId { get; set; }

    /// <summary>See <see cref="BlogNotifyJobStatus"/>.</summary>
    public string Status { get; set; } = BlogNotifyJobStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
}

public static class BlogNotifyJobStatus
{
    public const string Pending = "Pending";

    /// <summary>The claim: a job a runner is mailing right now, so the kick and the sweeper cannot
    /// both send. A row stuck here past its window is swept to Failed, never re-sent blind.</summary>
    public const string Sending = "Sending";

    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

/// <summary>
/// The landing page's editable half (ADR-215) — one row, always <c>Id == 1</c>.
///
/// The page's structure, its prices and its feature list are code, because they are claims the
/// product has to keep and a hand-kept copy of them goes stale the first time a limit moves. What
/// lives here is the half that is genuinely the maintainer's to write: the headline, the note in
/// the margin, the roadmap and the story — the parts no test can check and no reader forgives
/// being wrong. Every string is nullable, and null means "use what the code says", so an install
/// that never opens the admin tab still renders a complete page.
/// </summary>
public class LandingSettings
{
    public int Id { get; set; } = 1;

    public string? KickerEn { get; set; }
    public string? KickerRu { get; set; }
    public string? HeroTitleEn { get; set; }
    public string? HeroTitleRu { get; set; }
    public string? HeroSubEn { get; set; }
    public string? HeroSubRu { get; set; }

    /// <summary>
    /// The line under the waitlist field. Empty by default on purpose: it is the one slot on the
    /// page shaped to hold a number about other people, and an invented one is the fastest way to
    /// make everything above it read as invented too.
    /// </summary>
    public string? ProofEn { get; set; }
    public string? ProofRu { get; set; }

    /// <summary>The handwritten aside beside the form, in the maintainer's own voice.</summary>
    public string? NoteEn { get; set; }
    public string? NoteRu { get; set; }

    /// <summary>Overrides <c>Cedar:ShowcaseBlog</c> without a redeploy. Null falls back to it.</summary>
    public string? ShowcaseBlog { get; set; }

    public bool ShowShots { get; set; } = true;
    public bool ShowFeatures { get; set; } = true;
    public bool ShowPricing { get; set; } = true;
    public bool ShowRoadmap { get; set; }
    public bool ShowStory { get; set; }
    /// <summary>Off until the desktop build is something to hand a stranger, like the two above.</summary>
    public bool ShowDownload { get; set; }

    /// <summary>
    /// Three lists that are lists in the page and would be three tables here. JSON in a column,
    /// because nothing ever queries inside them — they are read whole, once, to draw one section.
    /// </summary>
    public string? ShotsJson { get; set; }
    public string? RoadmapJson { get; set; }
    public string? StoryJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
