namespace CedarClerk.Server;

/// <summary>
/// Reading a boolean setting without trusting what is in it (found 10.08.2026).
///
/// <c>IConfiguration.GetValue&lt;bool&gt;</c> **throws** on an empty string — and an empty string is
/// an entirely ordinary thing to find in an environment variable. The desktop shell already sets
/// <c>Cedar__BotToken=''</c> deliberately, and any script that clears a variable by assigning ""
/// produces the same shape. The result was not a wrong answer but an
/// <c>InvalidOperationException</c> out of whatever endpoint happened to read the flag, which is a
/// spectacular way for a configuration typo to take down a request.
///
/// A flag that cannot be read is off. That is the only safe reading: every one of these gates a
/// capability, and "we could not tell" must never mean "yes".
/// </summary>
public static class ConfigFlags
{
    public static bool IsOn(this IConfiguration config, string key) =>
        bool.TryParse(config[key], out var value) && value;
}
