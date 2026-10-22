using Avalonia.Markup.Xaml;
using RadioWindows.Core;

namespace RadioWindows.Localization;

/// <summary>Static localised text: <c>Text="{loc:Tr Nav.All}"</c>.</summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
