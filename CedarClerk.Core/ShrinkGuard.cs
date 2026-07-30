namespace CedarClerk.Core;

/// <summary>
/// T-018.1, from the 29.07.2026 incident: the 1.2s autosave honestly saves whatever the editor
/// holds at that instant, including the empty document that exists for a moment while a table is
/// being deleted or a select-all is pending a paste. The server used to accept it without a word,
/// and the blog renders the stored document live — so a transient empty state became the published
/// post. This decides when a save is drastic enough to require the author to say "yes, really".
/// </summary>
public static class ShrinkGuard
{
    /// <summary>
    /// Below this the stored version is a stub, and losing it is not the incident this guards
    /// against — nobody needs a confirmation dialog to clear two words.
    /// </summary>
    public const int MinGuardedTextLength = 200;

    /// <summary>
    /// Keeping under this share of the stored text is what counts as drastic. Deliberately
    /// generous: a legitimate heavy edit rarely deletes 80% of a post in one autosave window,
    /// and the false positive costs one click while the false negative cost a real post.
    /// </summary>
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
