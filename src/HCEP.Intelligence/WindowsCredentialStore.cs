// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
//
// Windows Credential Manager wrapper for secure API key storage.
//
// Usage:
//   // Store a key (do this once from a setup wizard or settings UI):
//   WindowsCredentialStore.SaveApiKey("HCEP/OpenAI", "sk-...");
//
//   // Read the key at runtime (preferred over environment variables):
//   string? key = WindowsCredentialStore.LoadApiKey("HCEP/OpenAI")
//                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
//
// Advantages over environment variables:
//   - Keys are stored encrypted in the Windows Credential Manager vault
//   - Not visible in process listings or environment dumps
//   - Scoped to the current user account
//   - Survives reboots without re-entry
// ──────────────────────────────────────────────────────────────
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HCEP.Intelligence;

/// <summary>
/// Thin P/Invoke wrapper around the Windows Credential Manager (advapi32.dll).
/// Provides secure storage and retrieval of HCEP API keys, replacing plain
/// environment-variable access for production deployments.
///
/// All target names are prefixed with "HCEP/" for easy identification in
/// Control Panel → Credential Manager → Windows Credentials.
/// </summary>
public static class WindowsCredentialStore
{
    private const string TargetPrefix = "HCEP/";

    // ── Well-known credential target names ────────────────────
    public const string OpenAI = "HCEP/OpenAI";
    public const string Anthropic = "HCEP/Anthropic";
    public const string Gemini = "HCEP/Gemini";
    public const string Mistral = "HCEP/Mistral";
    public const string xAI = "HCEP/xAI";
    public const string Cohere = "HCEP/Cohere";
    public const string OpenRouter = "HCEP/OpenRouter";
    public const string DeepSeek = "HCEP/DeepSeek";
    public const string Groq = "HCEP/Groq";
    public const string TogetherAI = "HCEP/TogetherAI";
    public const string FireworksAI = "HCEP/FireworksAI";
    public const string Perplexity = "HCEP/Perplexity";
    public const string AI21Labs = "HCEP/AI21Labs";
    public const string Replicate = "HCEP/Replicate";
    public const string HuggingFace = "HCEP/HuggingFace";
    public const string AzureOpenAI = "HCEP/AzureOpenAI";
    public const string AmazonBedrock = "HCEP/AmazonBedrock";
    public const string NvidiaNIM = "HCEP/NvidiaNIM";
    public const string Cerebras = "HCEP/Cerebras";
    public const string MoonshotAI = "HCEP/MoonshotAI";

    /// <summary>
    /// Retrieves an API key from Windows Credential Manager.
    /// Returns <c>null</c> if the credential does not exist or the call fails
    /// (e.g. on non-Windows platforms or if Credential Manager is unavailable).
    /// </summary>
    /// <param name="targetName">Credential target — use one of the well-known constants above.</param>
    public static string? LoadApiKey(string targetName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        try
        {
            if (!NativeMethods.CredRead(targetName, CRED_TYPE.GENERIC, 0, out IntPtr credPtr))
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                    return null;

                byte[] blob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, blob, 0, (int)cred.CredentialBlobSize);
                return Encoding.Unicode.GetString(blob);
            }
            finally
            {
                NativeMethods.CredFree(credPtr);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves an API key to Windows Credential Manager (creates or overwrites).
    /// The key is stored as a Generic credential scoped to the current user.
    /// </summary>
    /// <param name="targetName">Credential target — use one of the well-known constants above.</param>
    /// <param name="apiKey">The API key value to store. Pass <c>null</c> or empty to delete.</param>
    /// <returns><c>true</c> on success; <c>false</c> if WCM is unavailable or the write failed.</returns>
    public static bool SaveApiKey(string targetName, string apiKey)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        if (string.IsNullOrEmpty(apiKey))
            return DeleteApiKey(targetName);

        try
        {
            byte[] blob = Encoding.Unicode.GetBytes(apiKey);

            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE.GENERIC,
                TargetName = targetName,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST.LOCAL_MACHINE,
                UserName = Environment.UserName,
            };

            IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
                cred.CredentialBlob = blobPtr;
                return NativeMethods.CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a credential from Windows Credential Manager.
    /// Returns <c>true</c> if deleted or not found.
    /// </summary>
    public static bool DeleteApiKey(string targetName)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            return NativeMethods.CredDelete(targetName, CRED_TYPE.GENERIC, 0)
                   || Marshal.GetLastWin32Error() == 1168; // ERROR_NOT_FOUND — already gone
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Convenience method: reads a key from Windows Credential Manager, then falls back
    /// to the specified environment variable if WCM has no entry.
    /// This is the recommended pattern for all HCEP API key lookups.
    /// </summary>
    /// <param name="targetName">WCM credential target (e.g. <see cref="OpenAI"/>).</param>
    /// <param name="envVarFallback">Environment variable name to use if WCM has no entry.</param>
    /// <param name="logger">Optional logger — logs which source provided the key (without revealing the value).</param>
    public static string? LoadWithFallback(
        string targetName,
        string envVarFallback,
        ILogger? logger = null)
    {
        var wcmValue = LoadApiKey(targetName);
        if (!string.IsNullOrEmpty(wcmValue))
        {
            logger?.LogDebug("API key for '{Target}' loaded from Windows Credential Manager", targetName);
            return wcmValue;
        }

        var envValue = Environment.GetEnvironmentVariable(envVarFallback);
        if (!string.IsNullOrEmpty(envValue))
        {
            logger?.LogDebug("API key for '{Target}' loaded from environment variable '{Env}'", targetName, envVarFallback);
            return envValue;
        }

        logger?.LogDebug("No API key found for '{Target}' in WCM or environment variable '{Env}'", targetName, envVarFallback);
        return null;
    }

    // ── Native interop ─────────────────────────────────────────

    private enum CRED_TYPE : uint { GENERIC = 1 }
    private enum CRED_PERSIST : uint { SESSION = 1, LOCAL_MACHINE = 2, ENTERPRISE = 3 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredRead(
            string targetName, CRED_TYPE type, uint flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CredDelete(
            string targetName, CRED_TYPE type, uint flags);

        [DllImport("advapi32.dll")]
        internal static extern void CredFree(IntPtr buffer);
    }
}
