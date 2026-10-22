using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.ViewModels;
using Xunit;

namespace RadioWindows.UiTests;

public sealed class ShellTests
{
    [Fact]
    public void Main_window_renders_in_light_and_dark()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        Assert.Equal(800, window.Width);
        Assert.Equal(680, window.Height);
        HeadlessApp.Capture(window, "main-light");

        shell.ApplyTheme();
        services.Theme.SetPreference(ThemePreference.Dark);
        HeadlessApp.Pump(100);
        HeadlessApp.Capture(window, "main-dark");
        Assert.True(services.Theme.Preference == ThemePreference.Dark);
    }

    [Fact]
    public void Search_filters_the_station_table()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        shell.Main.SearchText = "SomaFM";
        HeadlessApp.Pump(50);
        Assert.NotEmpty(shell.Main.Rows);
        Assert.All(shell.Main.Rows, row => Assert.Contains("SomaFM", row.Name, StringComparison.OrdinalIgnoreCase));
        HeadlessApp.Capture(window, "main-search");
        window.Close();
    }

    [Fact]
    public void Empty_state_appears_for_an_unknown_query()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        shell.Main.SearchText = "zzz-no-such-station";
        HeadlessApp.Pump(50);
        Assert.True(shell.Main.IsEmpty);
        Assert.Equal(Loc.T("Empty.SearchTitle"), shell.Main.EmptyTitle);
        HeadlessApp.Capture(window, "main-empty");
        window.Close();
    }

    [Fact]
    public void Genre_navigation_and_favorites_work()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        var jazz = shell.Main.GenreItems.First(item => item.Label == GenreCatalog.Jazz);
        jazz.Command.Execute(null);
        HeadlessApp.Pump(40);
        Assert.All(shell.Main.Rows, row => Assert.Equal(GenreCatalog.Jazz, row.Genre));

        var first = shell.Main.Rows[0];
        first.ToggleFavoriteCommand.Execute(null);
        HeadlessApp.Pump(40);
        Assert.True(services.Library.ById(first.Station.Id)!.IsFavorite);

        shell.Main.SetFilter(StationFilter.Favorites);
        HeadlessApp.Pump(40);
        Assert.Contains(shell.Main.Rows, row => row.Station.Id == first.Station.Id);
        window.Close();
    }

    [Fact]
    public void Playing_a_station_updates_transport_and_row_state()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        var row = shell.Main.Rows.First(row => row.Station.Id == "soma-groove");
        row.PlayCommand.Execute(null);
        var started = HeadlessApp.WaitUntil(() => services.Player.NowPlaying is not null
                                             && services.Player.Status == PlaybackState.Playing);
        Assert.True(started,
            $"status={services.Player.Status} station={services.Player.Station?.Name} engine={services.Player.ActiveEngineName} error={services.Player.ErrorMessage}");

        Assert.Equal("soma-groove", services.Player.Station?.Id);
        Assert.Equal(PlaybackState.Playing, services.Player.Status);
        Assert.True(row.IsCurrent);
        Assert.True(row.IsPlaying);
        Assert.Contains("Kerala", shell.Main.PlayerTitle);
        HeadlessApp.Capture(window, "main-playing");
        window.Close();
    }

    [Fact]
    public void All_modals_render()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;

        shell.ShowModal(new EqualizerViewModel(services, shell));
        HeadlessApp.Capture(window, "modal-equalizer");

        shell.ShowModal(new StationEditorViewModel(services, shell, null));
        HeadlessApp.Capture(window, "modal-editor");

        var browser = new BrowserViewModel(services, shell);
        browser.LoadResults(
        [
            new RadioBrowserStation("1", "FIP", "https://icecast.radiofrance.fr/fip-midfi.mp3",
                null, "France", "Jazz", "MP3", 128, 1200),
        ]);
        shell.ShowModal(browser);
        HeadlessApp.Capture(window, "modal-browser");

        shell.ShowModal(new TransferViewModel(services, shell));
        HeadlessApp.Capture(window, "modal-transfer");

        shell.ShowModal(new HistoryViewModel(services, shell));
        HeadlessApp.Capture(window, "modal-history");

        shell.ShowModal(new DiagnosticsViewModel(services, shell));
        HeadlessApp.Capture(window, "modal-diagnostics");

        shell.ShowModal(new LastFmSetupViewModel(services, shell));
        HeadlessApp.Capture(window, "modal-lastfm");

        Assert.True(shell.HasModal);
        shell.CloseModal();
        Assert.False(shell.HasModal);
        window.Close();
    }

    [Fact]
    public void Settings_page_and_help_render()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        shell.ShowSettings();
        HeadlessApp.Pump(80);
        Assert.IsType<SettingsPageViewModel>(shell.Page);
        HeadlessApp.Capture(window, "settings");

        ((SettingsPageViewModel)shell.Page!).OpenHelpCommand.Execute(null);
        HeadlessApp.Pump(60);
        HeadlessApp.Capture(window, "settings-help");
        ((SettingsPageViewModel)shell.Page!).GoBack();
        HeadlessApp.Pump(40);
        Assert.IsType<SettingsPageViewModel>(shell.Page);
        window.Close();
    }

    [Fact]
    public void Mini_player_renders_and_reflects_the_player()
    {
        var (services, shell, _) = HeadlessApp.CreateShell();
        using var __ = services;
        services.Player.Play(services.Library.Stations.First(station => station.Id == "soma-groove"));
        var started = HeadlessApp.WaitUntil(() => services.Player.NowPlaying is not null
                                             && services.Player.Status == PlaybackState.Playing);
        Assert.True(started,
            $"status={services.Player.Status} station={services.Player.Station?.Name} engine={services.Player.ActiveEngineName} error={services.Player.ErrorMessage}");

        var model = new MiniPlayerViewModel(services, shell);
        var window = new RadioWindows.Views.MiniPlayerWindow { DataContext = model, AutoHideOnDeactivate = false };
        window.ShowActivated = false;
        window.Show();
        HeadlessApp.Pump(400);
        Assert.True(model.IsPlaying);
        Assert.Contains("Kerala", model.TrackLine);
        HeadlessApp.Capture(window, "mini-player");
        window.Close();
    }

    [Fact]
    public void Theme_choice_updates_the_application_variant()
    {
        var (services, shell, window) = HeadlessApp.CreateShell();
        using var _ = services;
        shell.ShowSettings();
        var settings = (SettingsPageViewModel)shell.Page!;
        settings.SetTheme(ThemePreference.Dark);
        HeadlessApp.Pump(50);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        Assert.True(settings.ThemeOptions.Single(option => option.Preference == ThemePreference.Dark).IsSelected);
        window.Close();
    }
}
