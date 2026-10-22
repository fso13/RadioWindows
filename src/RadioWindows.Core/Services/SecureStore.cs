using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("RadioWindows.Core.Tests")]

namespace RadioWindows.Core.Services;

/// <summary>
/// Small named-value credential store living next to the settings file.
///
/// On Windows values are encrypted with DPAPI for the current user; elsewhere
/// they are written as plain UTF-8 files with owner-only permissions. Secrets
/// are a convenience, never a prerequisite: every operation swallows I/O and
/// platform failures so a read-only or locked profile cannot break the app.
/// </summary>
public static class SecureStore
{
    private const string DirectoryName = "credentials";
    private const string FileExtension = ".bin";

    private static readonly object Gate = new();
    private static string? _root;

    /// <summary>Test/host seam: redirects the store away from <see cref="AppPaths.Root"/>.</summary>
    internal static void UseRoot(string? root)
    {
        lock (Gate)
        {
            _root = string.IsNullOrWhiteSpace(root) ? null : root;
        }
    }

    public static void Save(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || value is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var path = FilePathFor(name);
                var directory = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(directory);
                HardenDirectory(directory);

                var temporary = path + ".tmp";
                if (OperatingSystem.IsWindows())
                {
                    var encrypted = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(value),
                        optionalEntropy: null,
                        DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(temporary, encrypted);
                }
                else
                {
                    File.WriteAllText(temporary, value, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    HardenFile(temporary);
                }

                File.Move(temporary, path, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    HardenFile(path);
                }
            }
        }
        catch (Exception)
        {
            // Credentials are optional; save failures simply leave the old value.
        }
    }

    public static string? Load(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        try
        {
            lock (Gate)
            {
                var path = FilePathFor(name);
                if (!File.Exists(path))
                {
                    return null;
                }

                if (OperatingSystem.IsWindows())
                {
                    var decrypted = ProtectedData.Unprotect(
                        File.ReadAllBytes(path),
                        optionalEntropy: null,
                        DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }

                return File.ReadAllText(path, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var path = FilePathFor(name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception)
        {
            // Best effort: a leftover file is overwritten on the next save.
        }
    }

    private static string FilePathFor(string name)
    {
        var safe = Sanitize(name);
        var root = _root ?? new AppPaths().Root;
        return Path.Combine(root, DirectoryName, safe + FileExtension);
    }

    /// <summary>Keeps dotted names (lastfm.session) while removing path separators.</summary>
    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_');
        }

        var safe = builder.ToString();
        while (safe.Contains("..", StringComparison.Ordinal))
        {
            safe = safe.Replace("..", "__", StringComparison.Ordinal);
        }

        return safe.Length == 0 || safe is "." or ".." ? "_" : safe;
    }

    private static void HardenDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // Best effort: a restrictive parent directory is enough.
        }
    }

    private static void HardenFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Best effort on file systems without POSIX modes.
        }
    }
}
