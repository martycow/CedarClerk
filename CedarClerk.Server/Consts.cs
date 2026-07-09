namespace CedarClerk.Server;

public static class Consts
{
    public const string Url = "https://cedarclerk.mooexe.dev";
    public const string Localhost = "http://localhost:8080";
    
    public const string CurrentVersion = "0.6.0";
    public const string DataDirectoryKey = "CEDAR_DATA_DIR";
    public const string DbFileName = "cedar.db";
    
    public const int PasswordMinLength = 10;

    // Not a secret — just enough to avoid storing raw visitor IPs directly.
    public const string VisitorHashSalt = "cedar-clerk-visitor-v1";
}