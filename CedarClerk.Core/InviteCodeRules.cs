namespace CedarClerk.Core;

// Whether an invite code may still be used (IF2, step 3).
//
// Pure and shared on purpose: the same three conditions decide whether registration ACCEPTS a
// code and whether the admin panel shows it as usable. Written out twice they would eventually
// disagree, and the copy that drifts is the one guarding registration.
public static class InviteCodeRules
{
    public static bool IsUsable(bool isActive, DateTime? expiresAt, int? maxUses, int uses, DateTime nowUtc)
    {
        if (!isActive) return false;
        // Expiry is an instant, not a day: the caller decides what "end of the 5th" means.
        if (expiresAt is { } expiry && expiry <= nowUtc) return false;
        // Null means unlimited. >= rather than >, so a cap of 5 admits exactly five accounts.
        if (maxUses is { } cap && uses >= cap) return false;
        return true;
    }
}
