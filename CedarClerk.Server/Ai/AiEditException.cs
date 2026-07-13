namespace CedarClerk.Server.Ai;

public class AiEditException(string message, Exception? inner = null) : Exception(message, inner);
