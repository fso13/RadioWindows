namespace RadioWindows.Core.Data;

public enum RadioUrlAction
{
    Unknown,
    Open,
    Play,
    Pause,
    Settings,
}

/// <summary>
/// Deep links used by shortcuts and browsers:
/// <c>radiowindows://play/&lt;station-id&gt;</c>, <c>radiowindows://pause</c>,
/// <c>radiowindows://open</c>, <c>radiowindows://settings</c>.
/// The macOS scheme <c>radiomac://</c> is accepted as an alias.
/// </summary>
public sealed record RadioUrlCommand(RadioUrlAction Action, string? StationId = null)
{
    public static RadioUrlCommand Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)
            || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("radiowindows" or "radiomac"))
        {
            return new RadioUrlCommand(RadioUrlAction.Unknown);
        }

        var host = uri.Host.ToLowerInvariant();
        var rest = uri.AbsolutePath.Trim('/');
        return host switch
        {
            "open" => new RadioUrlCommand(RadioUrlAction.Open),
            "pause" => new RadioUrlCommand(RadioUrlAction.Pause),
            "settings" => new RadioUrlCommand(RadioUrlAction.Settings),
            "play" => new RadioUrlCommand(
                string.IsNullOrEmpty(rest) ? RadioUrlAction.Open : RadioUrlAction.Play,
                string.IsNullOrEmpty(rest) ? null : Uri.UnescapeDataString(rest)),
            _ => new RadioUrlCommand(RadioUrlAction.Unknown),
        };
    }
}
