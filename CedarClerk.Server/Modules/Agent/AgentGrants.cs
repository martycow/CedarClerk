using System.Collections.Concurrent;

namespace CedarClerk.Server.Modules.Agent;

/// <summary>
/// The folders this agent may read (ADR-117, Decision 6).
///
/// A grant is added by the **shell's main process**, and only right after the OS folder dialog came
/// back — that is, only after a human gesture. The page can then ask to scan that folder, and nothing
/// else. Without this the bearer token would be the only thing standing between a compromised
/// renderer and `C:\Users\marty\Documents`, and a single stolen token would mean the whole disk.
///
/// In memory, per launch, like the token itself: a grant that outlived the window it was given in
/// would be a permission nobody remembers giving.
/// </summary>
public class AgentGrants
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly ConcurrentDictionary<string, byte> _roots = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void Grant(string root) => _roots[Normalize(root)] = 0;

    /// <summary>
    /// Whether this path is inside a granted folder. Compared on the **resolved** path, because
    /// `root\..\..\Windows` is a string that starts with the root and a location that is nowhere
    /// near it. The trailing separator matters too: without it, a grant on `C:\Art` would also
    /// cover `C:\Artwork`.
    /// </summary>
    public bool Allows(string path)
    {
        if (Resolve(path) is not { } full) return false;
        foreach (var root in _roots.Keys)
        {
            if (string.Equals(full, root, PathComparison)) return true;
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, PathComparison) && !HasLinkBelowRoot(root, full)) return true;
        }
        return false;
    }

    private static string Normalize(string path) => Resolve(path) ?? path;

    private static bool HasLinkBelowRoot(string root, string full)
    {
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
            }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            catch (Exception) { return true; }
        }
        return false;
    }

    private static string? Resolve(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            // A malformed path is not allowed anywhere, which is what a null answers here.
            return null;
        }
    }
}
