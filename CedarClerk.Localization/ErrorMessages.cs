using System.Globalization;
using System.Runtime.CompilerServices;

namespace CedarClerk.Localization;

/// <summary>
/// T-050 / T-194 — server-side messages, in the reader's language.
///
/// The language comes from <see cref="CultureInfo.CurrentUICulture"/>, which the server sets per
/// request from the signed-in account's <c>UiLanguage</c> (falling back to Accept-Language). That
/// is deliberate: .NET already flows CurrentUICulture across await boundaries, so **no call site
/// has to pass a language** — every `ErrorMessages.X` reference answers in the reader's language.
///
/// Members are properties, not consts: a const is baked into the caller at compile time and could
/// never be language-dependent. The English text is the member's own argument; every other UI
/// language is a table in <c>ErrorMessages.{lang}.cs</c> keyed by member name (ADR-284), and
/// <c>ErrorMessageLocalizationTests</c> fails when a table lacks a member or an endpoint answers
/// with a literal — verified to actually go red, not merely to exist.
/// </summary>
public static partial class ErrorMessages
{
    public static string ProjectRequired => T("Choose an active project for this document.");
    public static string AccountVerificationFailed => T("Check your current password and account email.");
    public static string AccountBillingMustClose => T("Resolve recurring billing before deleting your account. Contact support if needed.");
    public static string PasswordChangeFailed => T("The password could not be changed. Check the current password and new password requirements.");

    public static string PasswordRecoveryUnavailable => T("Password recovery is temporarily unavailable. Please try again later.");
    public static string PasswordResetInvalid => T("The link is invalid or expired, or the password does not meet the requirements. Request a new link and try again.");
    public static string DraftNotFound => T("Draft not found.");
    public static string InvalidToken => T("Invalid token.");
    // T-050, second half (01.08.2026): the ~60 messages that used to live as inline English
    // literals in the endpoint files. Moved verbatim in meaning — this is a translation, not a
    // rewording, so a message someone already recognises stays recognisable.
    public static string DescriptionRequired => T("A description is required");
    public static string HandleAndAppPasswordRequired => T("A handle and an app password are required");
    public static string TermRequired => T("A term is required");
    public static string AiEditProPlus => T("AI editing needs Pro and is paid in credits.");
    public static string AiEditNotConfigured => T("AI editing is not configured");
    public static string AppearancePrefsTooLarge => T("Appearance preferences are too large");
    public static string AutoTranslateNotConfigured => T("Auto-translate is not configured");
    public static string AvatarMustBeUploaded => T("Avatar must be an uploaded image");
    public static string AssetInUse => T("This file is used by posts — remove it from them first");
    public static string AssetIdOrPathRequired => T("An asset id or path is required");
    public static string UnknownMediaTypeFilter => T("Unknown media type filter.");
    public static string UnknownMediaProjectFilter => T("Unknown media project filter.");
    public static string UnknownMediaSortKey => T("Unknown media sort key.");
    public static string UnknownMediaSortDirection => T("Unknown media sort direction.");
    public static string UnknownAssetKindFilter => T("Unknown asset kind filter.");
    public static string UnknownAssetSortKey => T("Unknown asset sort key.");
    public static string UnknownAssetSortDirection => T("Unknown asset sort direction.");
    public static string UnknownAdminPostStateFilter => T("Unknown admin post state filter.");
    public static string UnknownAdminPostSortKey => T("Unknown admin post sort key.");
    public static string UnknownAdminPaymentStatusFilter => T("Unknown admin payment status filter.");
    public static string UnknownAdminPaymentSortKey => T("Unknown admin payment sort key.");
    public static string UnknownAdminSortDirection => T("Unknown admin sort direction.");
    public static string UnknownStatSource => T("Unknown stats source.");
    public static string BothTagsRequired => T("Both the old and the new tag are required");
    public static string InvalidEmail => T("Enter a valid email address");
    public static string ImportFileNotFound => T("File not found in import-tmp directory.");
    public static string FormTooLarge => T("Form is too large");
    public static string InvalidDocumentStructure => T("Invalid document structure.");
    public static string InvalidFileName => T("Invalid file name.");
    public static string InvalidInviteCode => T("Invalid invite code");
    public static string InvalidTelegramSignature => T("Invalid or expired Telegram login signature");
    public static string ExternalProviderNotConfigured => T("This sign-in method is not configured on this server");
    public static string ExternalLoginExpired => T("The provider sign-in expired — start again");
    public static string ExternalNoEmail => T("The provider returned no email address — sign in with a password instead");
    public static string ExternalAlreadyLinkedToOther => T("This provider account is already linked to a different Cedar Clerk account");
    public static string ExternalEmailTaken => T("An account already holds this address — sign in with your password and the link will be added");
    public static string ExternalLastWayIn => T("This is the only way into this account — set a password first");
    public static string TelegramNoAccount => T("No Cedar Clerk account is linked to this Telegram. Sign in another way and link it in Settings");
    public static string NewDraftDefaultsTooLarge => T("New-draft defaults are too large");
    public static string NoMarkdownInZip => T("No .md file found inside the zip.");
    public static string NoStripeSubscription => T("No Stripe subscription on this account");
    public static string ChannelNotFoundOrNoAccess => T("No TG-channel was found or no access to that channel");
    public static string LinkTelegramBeforeChannel => T("Link your Telegram in Settings → Integrations first — that is how we confirm the channel is yours.");
    public static string NotChannelAdmin => T("You are not an administrator of this channel — you can only connect your own.");
    public static string NoAccountWithEmail => T("No account with that email.");
    public static string NoSuchInviteCode => T("No such invite code");
    public static string NoTermsInLanguage => T("No terms in this language");
    public static string NothingToTranslate => T("Nothing to translate — write the texts in the source language first");
    public static string DestinationNotConnected => T("One of those destinations is not connected");
    public static string PickADestination => T("Pick at least one destination");
    public static string PickALanguage => T("Pick at least one language to translate into");
    public static string SignatureIsPro => T("Post signature is a Pro feature. Upgrade to use it.");
    public static string SignatureTranslationsTooLarge => T("Signature translations are too large");
    public static string PresetHasNoForm => T("Preset has no form");
    public static string PublishToBlogFirst => T("Publish this draft to the blog first");
    public static string RegistrationFormTooLarge => T("Registration form is too large");
    public static string SameSourceAndTarget => T("Source and target language are the same");
    public static string TagRequired => T("Tag is required");
    public static string BotNotRunningNoToken => T("Telegram bot is not running (no token configured)");
    public static string SlugHasNoUsableCharacters => T("That URL has no usable characters");
    public static string SlugTaken => T("That URL is already taken");
    public static string SeriesNameEmptySlug => T("Series name produces an empty URL — add letters or digits");
    public static string SeriesNameTaken => T("A series with this name already exists");
    public static string TreeWouldCycle => T("A document cannot be nested inside itself or its own child");
    public static string TreeTooDeep => T("The tree is too deep — 10 levels at most");
    public static string InviteCodeExists => T("That code already exists");
    public static string NameReservedForAuthor => T("That name is reserved for the post's author.");
    public static string NotAZipArchive => T("The file is not a valid .zip archive.");
    public static string FormHasNoText => T("The form has no text to translate yet");
    public static string FormAlreadyInLanguage => T("The form is already written in this language");
    public static string ServiceMustBeHttps => T("The service address must be an https:// URL");
    public static string TermAlreadyInLanguage => T("The term is already in this language");
    public static string ThirdSlotIsPro => T("The third header slot is a Pro feature. Upgrade to use it.");
    public static string TranslationUnusable => T("The translation came back unusable — try again");
    public static string TelegramAlreadyLinked => T("This Telegram account is already linked to another Cedar Clerk account");
    public static string ToolbarLayoutTooLarge => T("Toolbar layout is too large");
    public static string TrialAlreadyUsed => T("Trial has already been used on this account");
    public static string UnsupportedChatType => T("Unsupported chat type");
    public static string UnsupportedUiLanguage => T("Unsupported interface language");
    public static string UnsupportedTimeZone => T("Unsupported timezone");
    public static string WatermarkTooLong => T("Watermark text is too long");
    public static string LocationTooLong => T("Location is too long");
    public static string FeedbackEmpty => T("Write a message.");
    public static string FeedbackTooLong => T("The message is too long.");
    public static string UnknownPresetKind => T("Unknown preset kind.");
    public static string PresetNameInvalid => T("A preset needs a name (up to 60 characters).");
    public static string TeamNameInvalid(int max) => T("A team needs a name (up to {0} characters).", [max]);
    public static string TeamLimitReached(int limit) => T("No more than {0} teams.", [limit]);
    public static string TeamMemberLimitReached(int limit) => T("A team holds at most {0} people.", [limit]);
    public static string TeamMemberAlreadyInvited => T("That address is already invited to this team.");
    public static string TeamMemberBanned => T("That address is banned from this team — lift the ban to invite it again.");
    public static string UnknownTeamMemberStatus(string status) => T("Unknown member status: {0}.", [status]);

    public static string PresetKindImmutable => T("A preset's kind cannot be changed.");
    public static string PresetLimitReached(int limit) => T("No more than {0} presets of one kind.", [limit]);
    public static string CannotChangeOwnAdmin => T("You cannot change your own admin rights");
    public static string CannotLockOwnAccount => T("You cannot lock your own account");
    public static string CannotDeleteOwnAccount => T("You cannot delete your own account");

    public static string StripePlanNotConfigured => T("Stripe is not configured for this plan — see docs/integrations-setup.md");
    public static string StripeNotConfigured => T("Stripe is not configured — see docs/integrations-setup.md");
    public static string PayPalAuthFailed => T("PayPal auth failed — check ClientId/Secret (and Cedar:PayPal:Mode: live vs sandbox)");
    public static string PayPalNoApprovalLink => T("PayPal did not return an approval link");
    public static string BlueskyCredentialsRefused => T("Bluesky refused those credentials — check the handle and use an app password, not your account password");
    public static string DraftNotFoundPlain => T("Draft not found");
    public static string ScheduleOnlyToOwnChannels => T("You can only schedule posts to your connected channels — connect this channel first (Channels popup)");
    /// <summary>T-106 — an earlier part of the thread did not go out, so this one must not either.</summary>
    public static string ThreadPartAbandoned(int index) => T("An earlier part of the thread did not go out — part {0} was held back rather than leaving a gap.", [index + 1]);

    public static string UnknownNetwork(string network) => T("Unknown network: {0}", [network]);

    public static string DraftIsEmpty => T("Draft is empty.");

    /// <summary>T-106 — the document shrank between parts, so the part being sent no longer exists.</summary>
    public static string ThreadPartGone => T("The document changed while the thread was being published — this part no longer exists. Publish again.");

    public static string BotNotRunning => T("Telegram bot is not running.");
    /// <summary>ADR-315 — "Send preview to me" needs the author's own Telegram user id.</summary>
    public static string PreviewNeedsTelegram => T("Link your Telegram account in Settings to send previews to yourself.");
    /// <summary>ADR-315 — Telegram refuses a bot's first message to a user who never pressed Start.</summary>
    public static string PreviewBotNotStarted => T("Open the Cedar Clerk bot in Telegram, press Start, then send the preview again.");
    public static string TelegramNotSentYet => T("This post has not been sent to Telegram yet — there is nothing to sync.");
    public static string TelegramThreadNotSyncable => T("The last send was a thread — syncing threads is not supported yet. Publish again.");
    public static string TelegramPostGone(string reason) => T("The Telegram post can no longer be edited: {0}", [reason]);
    /// <summary>ADR-189 — the bounds come from the caller: they are CreditPacks' to state, and
    /// this project does not reference Core.</summary>
    public static string CreditAmountOutOfRange(int min, int max) => T("Name a pack, or a number of credits between {0} and {1}.", [min, max]);

    // T-089 — the one Bluesky failure an author can act on: the stored app password no longer opens
    // a session (revoked in Bluesky's settings, or unreadable because the DataProtection key ring
    // and the database were separated — see PublishTargetSecrets).
    /// <summary>
    /// A document the network would refuse, known before sending (T-086). The issue list travels
    /// beside this so the client can say WHICH limit — this line only says whose verdict it is.
    /// </summary>
    public static string PublishWontFit(string network) => T("This post does not fit {0}'s limits — fix what is listed and try again.", [network]);

    public static string XReconnect => T("The X connection is no longer valid — reconnect the account in publishing settings.");
    public static string XMediaScopeMissing => T("Pictures were left out of the X post: the connection has no media permission — reconnect the account in Settings.");
    public static string XNotConfigured => T("X publishing is not configured on the server.");
    public static string NotEnoughCredits => T("Not enough credits — top up your balance in Settings → Credits.");
    public static string XApiCreditsDepleted => T("The X app is out of API credits — top up pay-per-use billing in the X Developer Portal (this is not your Cedar Clerk credits).");
    public static string BlueskyReconnect => T("Could not sign in to Bluesky — reconnect the account in settings.");
    public static string LinkedInReconnect => T("The LinkedIn connection has expired or was revoked — reconnect the account in Settings → Integrations.");
    public static string LinkedInNotConfigured => T("LinkedIn publishing is not configured on the server.");
    public static string LinkedInNoScheduling => T("LinkedIn takes posts only when you press Publish — its API terms forbid scheduled sending.");
    public static string DiscordWebhookRequired => T("Paste a Discord webhook URL — the channel's settings issue one under Integrations → Webhooks.");
    public static string DiscordWebhookInvalid => T("Discord refused this webhook — check the URL (it must start with https://discord.com/api/webhooks/).");
    public static string WaitlistEmailInvalid => T("That does not look like an email address.");
    public static string LandingImageUnsupported(string contentType) => T("That format does not belong here: {0}. A screenshot is PNG, JPEG or WebP.", [contentType]);
    public static string LandingImageTooLarge(long maxMb) => T("The file is too large — {0}MB at most.", [maxMb]);
    public static string LandingBadFileName => T("There is no such file here.");
    public static string ShowcaseSlugEmpty => T("The slug came out empty — use latin letters or digits.");
    public static string UsernameRequired => T("Pick a name — it becomes the address of your blog.");
    public static string UsernameInvalid => T("A name may hold only latin letters, digits and inner hyphens — up to 63 characters, and some names are reserved.");
    public static string UsernameTaken(string username) => T("'{0}' is already taken — pick another name.", [username]);
    public static string BuildDownloadUrlInvalid => T("A download link has to start with http:// or https://.");
    public static string BuildPublicNeedsUrl => T("Offering a build for download needs a link to it.");
    public static string ShowcaseGalleryTooLong(int maxChars) => T("The image list is too long — {0} characters at most.", [maxChars]);
    public static string ShowcaseTrailerNotYouTube => T("That is not a link to a YouTube video.");
    public static string ShowcaseDomainInvalid => T("That does not look like a domain name — mygame.com, for example.");
    public static string ShowcaseDomainIsOurs => T("That domain is already ours — a domain of your own is a different one.");
    public static string ShowcaseDomainTaken(string domain) => T("'{0}' is already claimed by another project.", [domain]);
    public static string ShowcaseSlugTaken(string slug) => T("'{0}' is already taken — pick another slug.", [slug]);
    public static string DiscordReconnect => T("The Discord webhook no longer works — likely deleted in the channel's settings. Reconnect it in settings.");
    public static string LinkYouTelegram => T("Link your Telegram account first.");
    public static string TelegramBillingNotConfigured => T("Telegram Stars billing is not configured!");
    public static string PaypalNotConfigured => T("PayPal is not wired up yet.");
    public static string AutoTranslateProPlus => T("Auto-translate needs Pro and is paid in credits.");
    public static string NotEnoughCreditsForAi => T("Not enough credits for this AI call — top up in Settings → Billing.");
    public static string LanguageRequiresPro => T("This language is a Pro feature — Free is limited to English and Japanese.");
    public static string AutoTranslateNoProvider => T("Auto-translate is not available with the configured provider");

    public static string CreateLanguageBeforePrimary => T("Create this language version before making it primary.");

    // ADR-065 — the publish guard's answer to "you confirmed a diff of something else".
    public static string PublishConfirmationStale => T("This post changed after the update was previewed — review the changes and confirm again.");

    // T-018.1 / T-018.3 — the two ways a save is refused rather than silently applied.
    public static string SaveShrinkNeedsConfirmation => T("This save would delete most of the text — confirm that it's intentional.");
    public static string SaveConflict => T("This version was edited elsewhere after you loaded it — reload before saving.");

    // T-013 — named provider and language, because the fix is switching one or picking the other.
    public static string LanguageNotSupportedByProvider(string lang, string provider) => T("The configured translation provider ({0}) cannot translate into {1}.", [provider, lang.ToUpperInvariant()]);

    public static string AiDailyLimitReached(int limit) => T("Daily AI limit ({0} calls) reached — resets at midnight UTC.", [limit]);

    public static string LanguageIsPrimary(string lang) => T("{0} is this draft's primary language — edit it on the main tab.", [lang.ToUpperInvariant()]);

    public static string NoVersionInLanguage(string lang) => T("No {0} version of this draft", [lang.ToUpperInvariant()]);

    // Indie-gamedev module (T-120, ADR-102/103). Interpolated messages live here for the same
    // reason the plain ones do — the test above only catches `error = "..."`, so an interpolated
    // literal would have slipped through the guard while still answering in English regardless of
    // who is reading.
    public static string DocumentTypeNotPublishable => T("This document is working material, not a post, so it cannot be published. Change its type if publishing is what you meant.");

    public static string DocumentTypeBlogPublished => T("This post is published on the blog. Unpublish it before changing its type to working material.");

    public static string ProjectNeedsOneDocument => T("A project must keep at least one document. Delete the project instead, or add another document first.");

    // Admin credit adjustments (11.08.2026).
    public static string CreditAmountRequired => T("Say how many credits to add or take back — zero changes nothing.");

    public static string CreditsWouldGoNegative(int balance) => T("The balance is {0}; taking more would go negative, which nothing in the app can read.", [balance]);

    // T-122 — the asset index. Pushed up from the desktop agent since ADR-117, so these are refusals
    // aimed at a client that is uploading rather than at a server that is walking.
    public static string AssetFolderRequired => T("Choose a folder to index first.");

    public static string AssetFolderNotFound(string path) => T("There is no folder at '{0}' — check the path, or plug the drive back in.", [path]);

    public static string AssetMachineRequired => T("The machine holding the folder was not named — without it, a file cannot be told from its fingerprint.");

    public static string AssetScanStampRequired => T("The scan's start time is missing — without it, there is no way to tell which files the walk did not find.");

    public static string AssetBatchTooLarge(int max) => T("At most {0} files are accepted at once.", [max]);

    public static string AssetIndexFull(int max) => T("A project's index holds at most {0} files. Choose a narrower folder — Assets alone, without builds and caches.", [max]);

    public static string AssetPathRejected(string path) => T("The path '{0}' was rejected: a path inside the chosen folder was expected.", [path]);

    public static string AssetThumbFormRequired => T("Previews are uploaded as a file form.");

    public static string AssetThumbBatchTooLarge(int max) => T("At most {0} previews are accepted at once.", [max]);

    public static string AssetThumbBudgetExhausted(long budget) => T("The preview allowance is used up — the limit is {0} MB. The index still works; new previews are not being stored.", [budget / (1024 * 1024)]);

    public static string UnknownProjectType(string type) => T("Unknown project type '{0}'.", [type]);
    public static string UnknownModuleKey(string key) => T("Unknown module '{0}'.", [key]);
    public static string DocumentsModuleRequired => T("The Documents module cannot be switched off.");
    public static string UnknownProjectEngine(string engine) => T("Unknown engine '{0}'.", [engine]);
    public static string UnknownProjectPlatform(string platform) => T("Unknown platform '{0}'.", [platform]);

    public static string UnknownDiscoveryCategory(string category) => T("Unknown Discovery category '{0}'.", [category]);

    public static string UnknownDocumentType(string type) => T("Unknown document type '{0}'.", [type]);

    public static string ProjectNameLength(int max) => T("Project name must be 1-{0} characters", [max]);

    public static string ProjectDescriptionLength(int max) => T("Project description must be at most {0} characters", [max]);

    // T-123 — the task tracker.
    public static string TaskTitleLength(int max) => T("Task title must be 1-{0} characters", [max]);

    public static string TaskDescriptionLength(int max) => T("Task description must be at most {0} characters. A task that needs more is really a document.", [max]);

    public static string TaskAssigneeLength(int max) => T("Assignee must be at most {0} characters", [max]);

    public static string UnknownTaskStatus(string status) => T("Unknown task status '{0}'.", [status]);

    public static string UnknownTaskPriority(int priority) => T("Priority is 1, 2 or 3 — got {0}.", [priority]);

    public static string UnknownLinkTarget(string type) => T("Unknown link target '{0}'.", [type]);

    public static string TaskCannotLinkToItself => T("A task cannot be linked to itself.");

    // T-124 — sprints.
    public static string SprintNameLength(int max) => T("Sprint name must be 1-{0} characters", [max]);

    public static string SprintEndsBeforeItStarts => T("A sprint cannot end before it starts.");

    public static string UnknownSprint => T("There is no such sprint — it may have been deleted.");

    // T-126 — builds and versions.
    public static string BuildVersionLength(int max) => T("A version must be 1-{0} characters", [max]);

    public static string BuildNotesLength(int max) => T("Build notes must be at most {0} characters", [max]);

    public static string BuildVersionTaken(string version) => T("This project already has a version '{0}'.", [version]);

    public static string UnknownBuild => T("There is no such version — it may have been deleted.");

    // ADR-116 — /downloads/latest before any desktop build has been shipped, or with a manifest
    // that names no installer. One message for both: from the outside they are the same situation.
    public static string NoDesktopBuildPublished => T("No desktop build has been published yet.");

    // T-301 — project collaborators and the reference board.
    public static string UnknownProjectRole(string role) => T("Unknown project role '{0}'.", [role]);

    public static string CannotInviteYourself => T("You are already on this project.");

    public static string ProjectMemberAlreadyInvited => T("That address is already invited to this project.");

    public static string ProjectMemberLimitReached(int max) => T("A project holds at most {0} collaborators.", [max]);

    public static string InviteNotFound => T("That invitation is no longer valid.");

    public static string InviteAlreadyAccepted => T("That invitation has already been used by another account.");

    // The one refusal in the module that is a 403 rather than a 404: a viewer is looking at the
    // thing they were just refused, so "no such project" would deny what is on their screen.
    public static string NoWriteAccessToProject => T("You can look at this project, but not change it.");

    public static string BoardNameLength(int max) => T("A board name must be 1-{0} characters", [max]);

    public static string UnknownCanvasBackground(string value) => T("Unknown canvas background '{0}'.", [value]);

    public static string BoardLimitReached(int max) => T("A project holds at most {0} boards.", [max]);

    public static string CanvasItemLimitReached(int max) => T("A board holds at most {0} items.", [max]);

    public static string CanvasBatchLimitReached(int max) => T("One request may carry at most {0} items.", [max]);

    public static string UnknownCanvasItemKind(string kind) => T("Unknown canvas item '{0}'.", [kind]);

    public static string CanvasPayloadTooLarge(int max) => T("That item carries more than {0} characters of data.", [max]);

    public static string CanvasPayloadInvalid => T("That item's contents are not in a shape this board understands.");

    public static string CanvasGeometryInvalid => T("An item needs a positive width and height.");

    public static string UnknownBoard => T("There is no such board — it may have been deleted.");

    // Wave 2 "Rhythm" — the queue, calendar, tracked-link and template answers.
    public static string QueueSlotDayInvalid => T("dayOfWeek must be 0 (Sunday) to 6 (Saturday)");

    public static string QueueSlotTimeInvalid => T("timeUtcMinutes must be 0 to 1439");

    public static string EvergreenMaxSendsInvalid => T("maxSends must be at least 1");

    public static string ScheduledPostNotPending => T("Only a pending post can be rescheduled");

    public static string TrackedLinkUrlInvalid => T("URL must be absolute http(s)");

    public static string UnknownTemplate => T("There is no such template.");

    public static string FromTemplateSourceRequired => T("Name a starter template or one of your own templates");

    public static string CtaTooManyButtons(int max) => T("At most {0} buttons", [max]);

    public static string CtaButtonTextLength(int max) => T("Button text must be 1 to {0} characters", [max]);

    public static string CtaButtonUrlInvalid => T("Button URL must be absolute http(s)");

    public static string InviteLinkNameLength(int max) => T("Link name must be 1 to {0} characters", [max]);

    // Dialogue tool (Yarn) — scripts, node graphs and the xlsx translation sheet.
    public static string DialogueNameLength(int max) => T("Dialogue name must be 1 to {0} characters", [max]);

    public static string DialogueGraphInvalid => T("Dialogue graph is not a readable node list");

    public static string DialogueGraphTooLarge(int maxKb) => T("Dialogue graph exceeds {0} KB", [maxKb]);

    public static string DialogueNodeTitleDuplicate(string title) => T("Two nodes are titled \"{0}\" — Yarn addresses nodes by title", [title]);

    public static string DialogueXlsxUnreadable => T("The file is not a readable xlsx workbook");

    public static string DialogueXlsxNoIdColumn => T("The sheet has no Id column — export the sheet from this dialogue first");

    // T-194 — the interpolated `$"…"` answers that used to sit in the endpoint files, where the
    // localisation guard could not see them. Still one member per meaning: a limit that several
    // endpoints state the same way is one message with the number as its argument.
    public static string UnknownTier(string tier) => T("Unknown tier '{0}'", [tier]);
    public static string InviteCodeTooShort(int min) => T("Code must be at least {0} characters", [min]);
    public static string UnsupportedFileType(string contentType) => T("Unsupported type: {0}", [contentType]);
    public static string FileTooLarge(long maxMb) => T("File is too large ({0}MB maximum)", [maxMb]);
    public static string StorageLimitExceeded(long limitMb) => T("Storage limit of your plan ({0}MB) exceeded. Upgrade for more.", [limitMb]);
    public static string StripeApiError(int status) => T("Stripe API error ({0}) — check server logs", [status]);
    public static string PayPalApiError(int status) => T("PayPal API error ({0}) — check server logs", [status]);
    public static string UnknownPlan(string plan) => T("Unknown plan '{0}'", [plan]);
    public static string ChannelLimitReached(int max) => T("Your plan allows {0} connected channel(s). Upgrade for more.", [max]);
    public static string ChannelSwitchCooldown(DateTime until) => T("On the Free plan you can switch to a different channel after {0:d MMM yyyy}. Upgrade to Pro to connect more channels.", [until]);
    public static string BotMustBeChannelAdmin => T("Bot must have an Admin with the right to send messages OR Creator.");
    public static string BotMustBeGroupAdmin => T("Bot must be an Admin or Creator of the Group/Supergroup.");
    public static string TagTooLong(int max) => T("Tag is too long ({0} characters maximum)", [max]);
    public static string TitleTooLong(int max) => T("Title is too long ({0} characters maximum)", [max]);
    public static string UnsupportedLanguage(string? lang) => T("Unsupported language: {0}", [lang]);
    public static string UnsupportedTranslationLanguage(string lang) => T("Unsupported translation language: {0}", [lang]);
    public static string UnknownAiEditKind(string kind) => T("Unknown AI edit kind: {0}", [kind]);
    public static string NoVersionToEditYet(string lang) => T("No {0} version to edit yet", [lang]);
    public static string PackageTooManyAssets(int max) => T("Too many assets in package ({0} maximum)", [max]);
    public static string PackageAssetInvalid(string name) => T("Unsupported or invalid asset: {0}", [name]);
    public static string PackageAssetTooLarge(string name) => T("Asset too large: {0}", [name]);
    public static string ZipTooManyImages(int max) => T("Too many images in the zip ({0} maximum)", [max]);
    public static string UnknownExportTarget(string first, string second) => T("Unknown target — use '{0}' or '{1}'.", [first, second]);
    public static string FolderNameLength(int max) => T("Folder name must be 1-{0} characters", [max]);
    public static string PresetNameLength(int max) => T("Preset name must be 1-{0} characters", [max]);
    public static string SeriesNameLength(int max) => T("Series name must be 1-{0} characters", [max]);
    public static string TermTooLong(int max) => T("Term is too long ({0} characters maximum)", [max]);
    public static string DescriptionTooLong(int max) => T("Description is too long ({0} characters maximum)", [max]);
    public static string AliasesTooLong(int max) => T("Aliases are too long ({0} characters maximum)", [max]);
    public static string StoreLinksTooLong(int max) => T("Store links are too long ({0} characters maximum)", [max]);
    public static string PressFieldTooLong(int max, int factsheetMax) => T("A press field is too long ({0} characters maximum, {1} for factsheet rows)", [max, factsheetMax]);
    public static string ShowcaseTextLength(int max) => T("Showcase text must be 1–{0} characters", [max]);
    public static string UnknownShowcaseAiKind(string kind) => T("Unknown Showcase AI kind: {0}", [kind]);
    public static string CouldNotReachService(string service, string detail) => T("Could not reach {0}: {1}", [service, detail]);

    /// <summary>
    /// Every translated language, keyed by member name. The English text is the member itself, so
    /// English is never a table and a language that lacks a key falls back to it; a translation
    /// is a format string with the member's arguments as <c>{0}</c>, <c>{1}</c>, …
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations { get; }

    // A constructor rather than a field initializer: the tables live in the per-language partial
    // files, and static initializers across partial declarations run in no defined order.
    static ErrorMessages()
    {
        Translations = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [Languages.Russian] = Russian,
            [Languages.German] = German,
            [Languages.French] = French,
            [Languages.Spanish] = Spanish,
            [Languages.Japanese] = Japanese,
            [Languages.Ukrainian] = Ukrainian,
            [Languages.Belarusian] = Belarusian,
            [Languages.Georgian] = Georgian,
        };
    }

    internal static string T(string english, [CallerMemberName] string key = "") =>
        Translated(key) ?? english;

    // The arguments travel as one array so that a string argument can never bind to `key` instead:
    // `T("...{0}", name)` compiles, looks the message up under the wrong key and answers in
    // English; `T("...{0}", [name])` cannot.
    internal static string T(string english, object?[] args, [CallerMemberName] string key = "") =>
        string.Format(CultureInfo.CurrentUICulture, Translated(key) ?? english, args);

    private static string? Translated(string key) =>
        Translations.TryGetValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, out var table)
        && table.TryGetValue(key, out var text)
            ? text
            : null;
}
