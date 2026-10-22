using Microsoft.Win32;

namespace RadioWindows.Services;

/// <summary>Run-at-login through the per-user Windows Run key.</summary>
public static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RadioWindows";

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    public static (bool Ok, bool Enabled, string? Error) Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, false, "Автозапуск доступен только в Windows");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null)
            {
                return (false, false, "Не удалось открыть раздел автозапуска");
            }

            if (enabled)
            {
                var executable = Environment.ProcessPath ?? string.Empty;
                if (executable.Length == 0)
                {
                    return (false, false, "Не удалось определить путь к приложению");
                }

                key.SetValue(ValueName, $"\"{executable}\" --background");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return (true, enabled, null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                           or System.Security.SecurityException or IOException)
        {
            return (false, IsEnabled, exception.Message);
        }
    }
}
