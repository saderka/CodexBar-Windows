using System.Security.Cryptography;

namespace CodexBar.Core;

public static class ClaudeDesktopCipher
{
    public static byte[] Decrypt(byte[] encrypted, byte[]? key, Func<byte[], byte[]> unprotect)
    {
        if (encrypted.AsSpan().StartsWith("v10"u8) || encrypted.AsSpan().StartsWith("v11"u8))
        {
            if (key?.Length != 32 || encrypted.Length < 31) throw new InvalidDataException("Claude Desktop session encryption is unsupported.");
            var plaintext = new byte[encrypted.Length - 31];
            using var aes = new AesGcm(key, 16);
            try { aes.Decrypt(encrypted.AsSpan(3, 12), encrypted.AsSpan(15, plaintext.Length), encrypted.AsSpan(encrypted.Length - 16), plaintext); }
            catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
            return plaintext;
        }
        if (encrypted.AsSpan().StartsWith("v20"u8)) throw new InvalidDataException("This Claude Desktop encryption format is unsupported.");
        return unprotect(encrypted);
    }

    public static byte[] ReadKey(string encodedKey, Func<byte[], byte[]> unprotect)
    {
        byte[]? wrapped = null, protectedKey = null;
        try
        {
            wrapped = Convert.FromBase64String(encodedKey);
            if (!wrapped.AsSpan().StartsWith("DPAPI"u8)) throw new InvalidDataException("Claude Desktop session key format is unsupported.");
            protectedKey = wrapped[5..];
            var key = unprotect(protectedKey);
            if (key.Length == 32) return key;
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("Claude Desktop session key length is unsupported.");
        }
        catch (FormatException) { throw new InvalidDataException("Claude Desktop session key is invalid."); }
        finally
        {
            if (wrapped != null) CryptographicOperations.ZeroMemory(wrapped);
            if (protectedKey != null) CryptographicOperations.ZeroMemory(protectedKey);
        }
    }
}
