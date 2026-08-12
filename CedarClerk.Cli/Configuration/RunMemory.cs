using System.Text.Json;

namespace CedarClerk.Cli.Configuration;

// How many tests the last run produced, so the next one can draw the whole field of empty cells and
// fill it in rather than growing a row at a time.
//
// Treated as a guess: the field is drawn to whichever is larger, the remembered count or the results
// in hand, and the final frame comes from the results alone — a stale number can never make a red
// run look green. Kept out of config.json, which holds what Marty chose rather than what the tool
// observed; mixing them makes "delete the config and start over" throw away the wrong thing.
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
