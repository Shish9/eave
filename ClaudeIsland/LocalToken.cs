using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeIsland;

/// The shared secret between Eave and its hook relay (and a second Eave asking the first to open Studio).
///
/// Eave listens on a local port. Without a check, any program on the PC, another signed-in user, or a web page open
/// in a browser could send it fake Claude events or fake permission prompts. The secret lives in a file in your own
/// profile (%APPDATA%\Eave\token), so only your account's programs can read it, and requests without it are refused.
public static class LocalToken
{
    public const string Header = "X-Eave-Token";

    public static string FilePath => Path.Combine(Brand.DataDir, "token");

    /// Reads the secret, creating it the first time. Empty if the file can't be written (Eave then still checks
    /// where a request comes from, see LocalGuard).
    public static string LoadOrCreate()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string existing = File.ReadAllText(FilePath).Trim();
                if (existing.Length >= 32) return existing;
            }
            string created = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            Directory.CreateDirectory(Brand.DataDir);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, created);
            File.Move(tmp, FilePath, true);
            return created;
        }
        catch (Exception ex)
        {
            Diag.Log("local token unavailable: " + ex.Message);
            return "";
        }
    }

    /// Compares in constant time, so the answer doesn't leak how much of a guess was right.
    public static bool Matches(string? given, string expected) =>
        !string.IsNullOrEmpty(given) && expected.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
}

/// Decides whether a request to Eave's local port is let in. Returns the HTTP status to refuse with, or null to accept.
public static class LocalGuard
{
    public static int? Check(string? host, string? origin, string? givenToken, string expectedToken, int port)
    {
        // A browser adds an Origin header to requests made by a web page; Eave's own programs never do.
        if (origin != null) return 403;
        // The relay talks to 127.0.0.1 directly. Another host name here means DNS rebinding from a web page.
        if (host != $"127.0.0.1:{port}" && host != $"localhost:{port}") return 403;
        // No secret available (file unwritable): the two checks above still apply.
        if (expectedToken.Length == 0) return null;
        return LocalToken.Matches(givenToken, expectedToken) ? null : 401;
    }
}
