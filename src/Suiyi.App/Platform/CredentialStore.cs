using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Suiyi.App.Platform;

/// <summary>
/// Optional endpoint-bound credentials, protected by Windows DPAPI for the current user.
/// Constructing the store never reads, creates or changes a file.
/// </summary>
public sealed class CredentialStore
{
    private const int MaxKeyBytes = 8 * 1024;
    private const int MaxFileBytes = 64 * 1024;
    private const string EndpointError = "请输入完整 HTTPS 服务地址，不能包含用户名、查询参数或片段。";
    private const string ReadError = "记住的密钥无法读取或解密，请在设置中重新输入。";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string directory;
    private readonly object sync = new();

    public CredentialStore(string? directory = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Suiyi", "credentials"));
    }

    public static string NormalizeEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Any(char.IsControl) ||
            !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(EndpointError, nameof(endpoint));

        try
        {
            // Preserve the path and nondefault port: those can identify different services.
            return new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttps,
                Host = uri.IdnHost.ToLowerInvariant(),
                Port = uri.IsDefaultPort ? -1 : uri.Port
            }.Uri.AbsoluteUri;
        }
        catch (UriFormatException)
        {
            throw new ArgumentException(EndpointError, nameof(endpoint));
        }
    }

    public bool Save(string endpoint, string apiKey, out string error)
    {
        error = string.Empty;
        if (!TryNormalize(endpoint, out var normalized))
        {
            error = EndpointError;
            return false;
        }
        if (!IsValidKey(apiKey))
        {
            error = "密钥不能为空、包含控制字符或超过 8 KiB。";
            return false;
        }

        lock (sync)
        {
            byte[]? plain = null;
            byte[]? entropy = null;
            string? temporary = null;
            try
            {
                var path = GetOwnedPath(normalized, ".bin");
                temporary = GetOwnedPath(normalized, ".bin.tmp");
                plain = StrictUtf8.GetBytes(apiKey);
                entropy = CreateEntropy(normalized);
                var encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
                if (encrypted.Length > MaxFileBytes) throw new InvalidDataException();
                Directory.CreateDirectory(directory);
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(encrypted);
                    file.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
                return true;
            }
            catch
            {
                error = "无法安全保存密钥，请重试或使用仅本次会话模式。";
                return false;
            }
            finally
            {
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
                if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
                // temporary is produced only by GetOwnedPath, never from user text.
                if (temporary is not null) DeleteTemporary(temporary);
            }
        }
    }

    public bool TryLoad(string endpoint, out string apiKey, out string error)
    {
        apiKey = string.Empty;
        error = string.Empty;
        if (!TryNormalize(endpoint, out var normalized))
        {
            error = EndpointError;
            return false;
        }

        lock (sync)
        {
            byte[]? plain = null;
            byte[]? entropy = null;
            try
            {
                var path = GetOwnedPath(normalized, ".bin");
                byte[] encrypted;
                // A stable handle bounds the allocation even if another process changes the file.
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length is <= 0 or > MaxFileBytes) throw new InvalidDataException();
                    encrypted = new byte[(int)file.Length];
                    file.ReadExactly(encrypted);
                }
                entropy = CreateEntropy(normalized);
                plain = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser);
                if (plain.Length is <= 0 or > MaxKeyBytes) throw new InvalidDataException();
                var decoded = StrictUtf8.GetString(plain);
                if (!IsValidKey(decoded)) throw new InvalidDataException();
                apiKey = decoded;
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch
            {
                apiKey = string.Empty;
                error = ReadError;
                return false;
            }
            finally
            {
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
                if (entropy is not null) CryptographicOperations.ZeroMemory(entropy);
            }
        }
    }

    public bool Delete(string endpoint, out string error)
    {
        error = string.Empty;
        if (!TryNormalize(endpoint, out var normalized))
        {
            error = EndpointError;
            return false;
        }
        lock (sync)
        {
            try
            {
                // Only the two filenames derived from this service may be removed.
                DeleteIfDirectoryExists(GetOwnedPath(normalized, ".bin.tmp"));
                DeleteIfDirectoryExists(GetOwnedPath(normalized, ".bin"));
                return true;
            }
            catch
            {
                error = "无法删除此服务保存的密钥，请重试。";
                return false;
            }
        }
    }

    private static bool TryNormalize(string endpoint, out string normalized)
    {
        try
        {
            normalized = NormalizeEndpoint(endpoint);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool IsValidKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > MaxKeyBytes || apiKey.Any(char.IsControl))
            return false;
        try { return StrictUtf8.GetByteCount(apiKey) <= MaxKeyBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    private string GetOwnedPath(string normalizedEndpoint, string extension)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEndpoint)));
        var path = Path.GetFullPath(Path.Combine(directory, digest + extension));
        var relative = Path.GetRelativePath(directory, path);
        // Verify the final absolute target remains a direct child of our named directory.
        if (Path.IsPathRooted(relative) || relative.Contains(Path.DirectorySeparatorChar) ||
            relative.Contains(Path.AltDirectorySeparatorChar) || relative is "." or "..")
            throw new InvalidOperationException();
        return path;
    }

    private static byte[] CreateEntropy(string normalizedEndpoint) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("Suiyi.CredentialStore.v1\n" + normalizedEndpoint));

    private static void DeleteIfDirectoryExists(string path)
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { }
    }

    private static void DeleteTemporary(string path)
    {
        try { DeleteIfDirectoryExists(path); }
        catch { /* Do not disclose exception details or mask the operation's fixed error. */ }
    }
}
