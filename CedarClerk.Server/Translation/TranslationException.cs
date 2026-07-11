namespace CedarClerk.Server.Translation;

public class TranslationException(string message, Exception? inner = null) : Exception(message, inner);