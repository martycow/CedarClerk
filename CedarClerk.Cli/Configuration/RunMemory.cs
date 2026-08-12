using System.Text.Json;

namespace CedarClerk.Cli.Configuration;

// How many tests the last run of a given selection produced, so the next one can draw the whole
// field of empty cells up front and fill it in (Marty, 12.08.2026) instead of growing a row at a
// time. Watching a known shape fill is the effect asked for; a grid that appears as it goes cannot
// give it, because there is nothing yet to fill.
//
// It is a guess, and it is treated as one. Nothing decides anything from this number: the field is
// drawn to whichever is larger, the remembered count or the results actually in hand, and the final
// frame is redrawn from the results alone. A stale number makes the animation slightly wrong for one
// run and is then overwritten — it can never make a red run look green.
//
// Kept out of config.json deliberately. That file holds what Marty chose; this holds what the tool
// observed, and mixing the two makes "delete the config and start over" throw away the wrong thing.
public static class RunMemory
{
    private const string FileName = "last-run.json";

    private static string Path_ => System.IO.Path.Combine(ConfigStore.Directory(), FileName);

    public static int Expected(string key) =>
        Load().TryGetValue(key, out var count) ? count : 0;

    public static void Remember(string key, int total)
    {
        if (total <= 0) return;

        try
        {
            var all = Load();
            all[key] = total;
            System.IO.Directory.CreateDirectory(ConfigStore.Directory());
            File.WriteAllText(Path_, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // A tool that cannot write a cosmetic hint still has to run the tests.
        }
    }

    private static Dictionary<string, int> Load()
    {
        try
        {
            return File.Exists(Path_)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path_)) ?? new()
                : new();
        }
        catch (Exception)
        {
            return new();
        }
    }
}
