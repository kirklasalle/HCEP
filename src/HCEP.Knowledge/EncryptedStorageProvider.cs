// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// ──────────────────────────────────────────────────────────────
//
// Encryption-at-rest provider for HCEP knowledge and biometric data.
// Uses DPAPI (Windows Data Protection API) via ProtectedData for
// machine-bound encryption without explicit key management.
//
// For cross-platform future: swap ProtectedData for AES-256-GCM
// with a device-derived key stored in the platform keychain.
// ──────────────────────────────────────────────────────────────

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HCEP.Knowledge;

/// <summary>
/// Provides encryption-at-rest for HCEP sensitive data storage.
/// Wraps Windows DPAPI for machine-scope encryption of knowledge store
/// and biometric embeddings. No explicit key management required — keys
/// are derived from the Windows user account.
/// </summary>
public sealed class EncryptedStorageProvider
{
    private readonly ILogger<EncryptedStorageProvider> _logger;

    /// <summary>Optional additional entropy (salt) for DPAPI encryption.</summary>
    private static readonly byte[] DpapiEntropy = "HCEP-KnowledgeStore-v1"u8.ToArray();

    public EncryptedStorageProvider(ILogger<EncryptedStorageProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Encrypts a UTF-8 string using DPAPI (current user scope) and writes
    /// the ciphertext to the specified file path.
    /// </summary>
    public async Task SaveEncryptedAsync(string path, string plaintext, CancellationToken ct = default)
    {
        try
        {
            byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            byte[] encrypted = ProtectedData.Protect(
                plaintextBytes,
                DpapiEntropy,
                DataProtectionScope.CurrentUser);

            // Write with a magic header so we can detect encrypted vs. plain files
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            // Magic bytes: "HCEP" + version byte
            byte[] header = [0x48, 0x43, 0x45, 0x50, 0x01]; // "HCEP" + v1
            await fs.WriteAsync(header, ct);
            await fs.WriteAsync(encrypted, ct);

            _logger.LogInformation("Encrypted data saved to {Path} ({Size} bytes)", path, encrypted.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save encrypted data to {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Reads and decrypts a DPAPI-encrypted file. Returns the UTF-8 plaintext.
    /// If the file has no HCEP header (legacy plain JSON), returns the raw content
    /// and logs a migration warning.
    /// </summary>
    public async Task<string> LoadEncryptedAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("Encrypted file not found: {Path}", path);
            return string.Empty;
        }

        try
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(path, ct);

            // Check for HCEP magic header
            if (fileBytes.Length >= 5 &&
                fileBytes[0] == 0x48 && fileBytes[1] == 0x43 &&
                fileBytes[2] == 0x45 && fileBytes[3] == 0x50 &&
                fileBytes[4] == 0x01)
            {
                // Encrypted file — strip header and decrypt
                byte[] ciphertext = fileBytes[5..];
                byte[] decrypted = ProtectedData.Unprotect(
                    ciphertext,
                    DpapiEntropy,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decrypted);
            }

            // Legacy plain JSON file — return as-is with migration warning
            _logger.LogWarning(
                "File {Path} is not encrypted (legacy format). " +
                "Data will be encrypted on next save. Consider re-saving immediately.",
                path);
            return Encoding.UTF8.GetString(fileBytes);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "Decryption failed for {Path}. File may have been created by a different user account.", path);
            throw;
        }
    }

    /// <summary>
    /// Migrates a legacy plaintext file to encrypted format in-place.
    /// Safe to call on already-encrypted files (no-op).
    /// </summary>
    public async Task MigrateToEncryptedAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path)) return;

        byte[] fileBytes = await File.ReadAllBytesAsync(path, ct);

        // Already encrypted — no-op
        if (fileBytes.Length >= 5 &&
            fileBytes[0] == 0x48 && fileBytes[1] == 0x43 &&
            fileBytes[2] == 0x45 && fileBytes[3] == 0x50)
        {
            _logger.LogDebug("File {Path} is already encrypted — skipping migration", path);
            return;
        }

        // Plain text — encrypt and overwrite
        string plaintext = Encoding.UTF8.GetString(fileBytes);
        await SaveEncryptedAsync(path, plaintext, ct);
        _logger.LogInformation("Migrated {Path} from plaintext to encrypted format", path);
    }
}
