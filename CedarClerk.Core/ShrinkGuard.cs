namespace CedarClerk.Core;

// T-018.1, from the 29.07.2026 incident: the 1.2s autosave saves whatever the editor holds at that
// instant, including the empty document that exists for a moment while a table is deleted or a
// select-all waits for a paste. The blog renders the stored document live, so that transient state
// became the published post. This decides when a save needs the author to say "yes, really".
public static class ShrinkGuard
{
    // Below this the stored version is a stub — nobody needs a dialog to clear two words.
    public const int MinGuardedTextLength = 200;

    // Generous on purpose: a legitimate heavy edit rarely drops 80% of a post in one autosave
    // window, and the false positive costs a click where the false negative cost a real post.
    public const double SuspiciousRemainingShare = 0.2;

    public record Verdict(bool Suspicious, int StoredTextLength, int IncomingTextLength);

    public static Verdict Inspect(string storedCedarJson, string incomingCedarJson)
    {
        var stored = TextLength(storedCedarJson);
        var incoming = TextLength(incomingCedarJson);
        var suspicious = stored >= MinGuardedTextLength && incoming <= stored * SuspiciousRemainingShare;
        return new Verdict(suspicious, stored, incoming);
    }

    // Human-visible characters, not raw JSON length: the JSON of an empty document is not much
    // shorter than that of a one-line one, and structural churn (a table becoming a paragraph)
    // must not read as data loss.
    private static int TextLength(string cedarJson)
    {
        try
        {
            return TipTapTextNodes.ExtractTexts(cedarJson).Sum(t => t.Length);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or ArgumentException)
        {
            // Unparseable content can't be measured; refusing to guard is better than refusing
            // every save of a document the guard simply doesn't understand.
            return 0;
        }
    }
}
