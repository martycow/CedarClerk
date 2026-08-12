using CedarClerk.Server.Modules.Agent;

namespace CedarClerk.Tests;

// ADR-117, Decision 6 — the second of the agent's two locks.
//
// The bearer token says "you are the shell". This says "and this is the folder the human picked". Both
// are needed: a stolen token without grants reaches nothing, and a compromised renderer with the shell's
// own token still cannot name a path nobody chose.
//
// Every case here is an escape that looks like a legitimate path, which is exactly why the check is on
// the resolved path and not on the string.
public class AgentGrantsTests
{
    private static string Temp(string name) =>
        Path.Combine(Path.GetTempPath(), "cedar-grant-" + name);

    [Fact]
    public void Nothing_is_allowed_before_a_folder_is_granted()
    {
        var grants = new AgentGrants();

        // The default has to be "no", or a bug that forgets to grant becomes a bug that reads any disk.
        Assert.False(grants.Allows(Temp("art")));
        Assert.False(grants.Allows(Path.Combine(Temp("art"), "hero.png")));
    }

    [Fact]
    public void A_granted_folder_and_everything_under_it_is_allowed()
    {
        var root = Temp("art");
        var grants = new AgentGrants();
        grants.Grant(root);

        Assert.True(grants.Allows(root));
        Assert.True(grants.Allows(Path.Combine(root, "hero.png")));
        Assert.True(grants.Allows(Path.Combine(root, "Props", "deep", "barrel.png")));
    }

    [Fact]
    public void A_sibling_folder_that_merely_starts_with_the_same_letters_is_refused()
    {
        var grants = new AgentGrants();
        grants.Grant(Temp("art"));

        // Without the trailing-separator rule, a grant on ...\art would also cover ...\artwork —
        // a whole different folder that happens to share a prefix.
        Assert.False(grants.Allows(Temp("artwork")));
        Assert.False(grants.Allows(Path.Combine(Temp("artwork"), "secret.png")));
    }

    [Fact]
    public void A_path_that_climbs_out_with_dot_dot_is_refused()
    {
        var root = Temp("art");
        var grants = new AgentGrants();
        grants.Grant(root);

        // The string starts with the granted root; the location does not. This is the case that makes
        // comparing resolved paths rather than raw strings non-negotiable.
        Assert.False(grants.Allows(Path.Combine(root, "..", "..", "Windows", "System32", "config")));
        Assert.False(grants.Allows(Path.Combine(root, "sub", "..", "..", "elsewhere", "a.png")));
    }

    [Fact]
    public void A_path_inside_that_only_looks_like_it_climbs_out_is_still_allowed()
    {
        var root = Temp("art");
        var grants = new AgentGrants();
        grants.Grant(root);

        // Resolving cuts both ways, and it must: this one really is inside.
        Assert.True(grants.Allows(Path.Combine(root, "Props", "..", "hero.png")));
    }

    [Fact]
    public void A_trailing_separator_on_the_grant_changes_nothing()
    {
        var grants = new AgentGrants();
        grants.Grant(Temp("art") + Path.DirectorySeparatorChar);

        Assert.True(grants.Allows(Path.Combine(Temp("art"), "hero.png")));
    }

    [Fact]
    public void Two_grants_are_both_honoured_and_neither_widens_the_other()
    {
        var grants = new AgentGrants();
        grants.Grant(Temp("art"));
        grants.Grant(Temp("audio"));

        Assert.True(grants.Allows(Path.Combine(Temp("art"), "hero.png")));
        Assert.True(grants.Allows(Path.Combine(Temp("audio"), "theme.wav")));
        Assert.False(grants.Allows(Path.Combine(Temp("video"), "intro.mp4")));
    }

    [Fact]
    public void A_malformed_path_is_refused_rather_than_throwing()
    {
        var grants = new AgentGrants();
        grants.Grant(Temp("art"));

        // An exception here would surface as a 500 from a security check, and a security check that
        // crashes is one nobody can reason about. It answers "no".
        Assert.False(grants.Allows("\0invalid"));
        Assert.False(grants.Allows(""));
    }
}
