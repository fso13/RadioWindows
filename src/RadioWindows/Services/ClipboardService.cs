using Avalonia.Controls;

namespace RadioWindows.Services;

/// <summary>Clipboard access bound to the main window (diagnostics, history, logs).</summary>
public sealed class ClipboardService
{
    private TopLevel? _topLevel;

    public void Attach(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<bool> SetTextAsync(string text)
    {
        try
        {
            if (_topLevel?.Clipboard is not { } clipboard)
            {
                return false;
            }

            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }
}

/// <summary>Opens files, folders and URLs with the Windows shell.</summary>
public static class ShellOpen
{
    public static void Reveal(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var target = File.Exists(path) || Directory.Exists(path)
                ? path
                : Path.GetDirectoryName(path) ?? path;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{target}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Shell integration is best effort.
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Ignore: the user can copy the link manually.
        }
    }
}
