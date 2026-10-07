using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexBar.Core;

// JWT claims are local display/matching hints, not verified server identity.
public sealed record CodexAccountIdentity(string Key, string Label)
{
    public static CodexAccountIdentity? TryRead(JsonElement tokens)
    {
        var account = Text(tokens, "account_id") ?? Text(tokens, "accountId");
        string? subject = null, email = null;
        foreach (var name in new[] { "id_token", "access_token", "accessToken" })
        {
            try
            {
                var parts = Text(tokens, name)?.Split('.');
                if (parts?.Length != 3 || parts[1].Length > 65536) continue;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
                using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
                var root = claims.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                email ??= Text(root, "email");
                if (name == "id_token") subject ??= Text(root, "sub");
                if (root.TryGetProperty("https://api.openai.com/auth", out var auth) && auth.ValueKind == JsonValueKind.Object)
                    subject ??= Text(auth, "chatgpt_user_id") ?? Text(auth, "user_id");
                if (root.TryGetProperty("https://api.openai.com/profile", out var profile) && profile.ValueKind == JsonValueKind.Object)
                    email ??= Text(profile, "email");
            }
            catch (Exception ex) when (ex is JsonException or FormatException) { }
        }
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(subject)) return null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject + "\0" + account)));
        return new(key, CleanLabel(email ?? "Codex account"));
    }

    internal static (CodexAccountIdentity Identity, Credentials Credentials) Validate(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("Credential file is too large.");
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var credentials = CredentialReader.Parse(Provider.Codex, document.RootElement, DateTimeOffset.UtcNow);
            var identity = TryRead(document.RootElement.GetProperty("tokens")) ??
                throw new InvalidDataException("This login has no identifiable user and workspace. Sign in again with Codex.");
            return (identity, credentials);
        }
        catch (JsonException) { throw new InvalidDataException("Codex login file is invalid. Sign in again."); }
    }

    public static string CleanLabel(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).Take(60).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "Codex account" : clean;
    }
    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed record CodexAccountInfo(string Id, string Label, string IdentityKey);

// Protection belongs to the host (Windows uses CurrentUser DPAPI). No plaintext auth on disk here.
public sealed class CodexAccountVault(string directory, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect)
{
    private sealed class SavedAccount
    {
        public string Label { get; set; } = "";
        public byte[] Auth { get; set; } = [];
        public override string ToString() => "[Saved account redacted]";
    }

    public IReadOnlyList<CodexAccountInfo> List()
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.EnumerateFiles(directory, "*.account").Select(path =>
        {
            var id = Path.GetFileNameWithoutExtension(path);
            try
            {
                var saved = Load(id);
                try { return new CodexAccountInfo(id, saved.Label, CodexAccountIdentity.Validate(saved.Auth).Identity.Key); }
                finally { CryptographicOperations.ZeroMemory(saved.Auth); }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException)
            { return new CodexAccountInfo(id, "Unavailable account · remove and sign in again", ""); }
        }).OrderBy(a => a.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public CodexAccountInfo Save(byte[] auth, string? label = null)
    {
        var identity = CodexAccountIdentity.Validate(auth).Identity;
        var existing = List().FirstOrDefault(a => a.IdentityKey == identity.Key);
        var id = existing?.Id ?? Guid.NewGuid().ToString("N");
        var name = CodexAccountIdentity.CleanLabel(label ?? existing?.Label ?? identity.Label);
        WriteProtected(PathFor(id), JsonSerializer.SerializeToUtf8Bytes(new SavedAccount { Label = name, Auth = auth }));
        return new(id, name, identity.Key);
    }

    public byte[] ReadAuth(string id) => Load(id).Auth;
    public void Remove(string id) => File.Delete(PathFor(id));

    public void PreserveRecovery(byte[] auth) => WriteProtected(Path.Combine(directory, "recovery.bin"), auth.ToArray());

    private SavedAccount Load(string id)
    {
        var path = PathFor(id);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Saved account is invalid.");
        var bytes = unprotect(File.ReadAllBytes(path));
        try
        {
            var account = JsonSerializer.Deserialize<SavedAccount>(bytes) ?? throw new InvalidDataException("Saved account is invalid.");
            if (account.Auth == null) throw new InvalidDataException("Saved account is invalid.");
            account.Label = CodexAccountIdentity.CleanLabel(account.Label);
            return account;
        }
        catch (JsonException) { throw new InvalidDataException("Saved account is invalid."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private string PathFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Unknown saved account.");
        return Path.Combine(directory, id + ".account");
    }

    private void WriteProtected(string path, byte[] plaintext)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var ciphertext = protect(plaintext);
            AtomicFile.Write(path, ciphertext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}

internal static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class CodexAuthFileSwitcher
{
    // Known active clients must be closed by the UI. Hash/readback guards detect many,
    // but not all, external writer races; this is not an atomic compare-and-swap.
    public static CodexAccountInfo Switch(CodexAccountVault vault, string id, string authPath,
        Action? beforeCommit = null, Action? afterCommit = null)
    {
        if (!Path.IsPathFullyQualified(authPath) || !Path.GetFileName(authPath).Equals("auth.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose the Codex home's auth.json file in Settings first.");
        var selected = vault.List().SingleOrDefault(a => a.Id == id) ?? throw new InvalidDataException("This saved account is no longer available.");
        var incoming = vault.ReadAuth(id);
        byte[]? outgoing = null;
        var staged = authPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (CodexAccountIdentity.Validate(incoming).Identity.Key != selected.IdentityKey)
                throw new InvalidDataException("The selected saved account changed. Open the account list again.");
            if (File.Exists(authPath))
            {
                outgoing = ReadBounded(authPath);
                vault.PreserveRecovery(outgoing); // Keep any unsupported existing login too, without overwriting it.
                _ = CodexAccountIdentity.Validate(outgoing);
                vault.Save(outgoing); // Retain current refreshed credentials before replacing them.
                // Same account: keep the current renewed credentials, not an older saved copy.
                if (CodexAccountIdentity.Validate(outgoing).Identity.Key == selected.IdentityKey) return selected;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(authPath)!);
            using (var file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(incoming); file.Flush(true); }
            beforeCommit?.Invoke();
            var latest = File.Exists(authPath) ? ReadBounded(authPath) : null;
            try
            {
                if ((outgoing == null) != (latest == null) || outgoing != null && !outgoing.AsSpan().SequenceEqual(latest))
                    throw new IOException("Codex login changed while switching. Close Codex, then try again. Your current login was preserved.");
            }
            finally { if (latest != null) CryptographicOperations.ZeroMemory(latest); }
            if (outgoing != null) File.Replace(staged, authPath, null); // Preserve the existing auth file's ACL.
            else File.Move(staged, authPath); // A concurrently-created login must not be overwritten.
            afterCommit?.Invoke();
            var actual = ReadBounded(authPath);
            try
            {
                if (!actual.AsSpan().SequenceEqual(incoming))
                    throw new IOException("Another application changed the login. No automatic rollback was performed; your previous account is saved.");
            }
            finally { CryptographicOperations.ZeroMemory(actual); }
            return selected;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(incoming);
            if (outgoing != null) CryptographicOperations.ZeroMemory(outgoing);
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    public static byte[] ReadBounded(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 1024 * 1024) throw new InvalidDataException("Credential file is too large.");
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = file.Read(buffer)) > 0)
        {
            if (bytes.Length + count > 1024 * 1024) throw new InvalidDataException("Credential file is too large.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
}
