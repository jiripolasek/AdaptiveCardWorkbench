using AdaptiveCardWorkbench.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel;

namespace AdaptiveCardWorkbench.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly EditorPreferencesService _preferences;
    private bool _isActive;
    private bool _isRefreshing;

    public SettingsPageViewModel(
        EditorPreferencesService preferences,
        SystemFontService systemFonts)
    {
        _preferences = preferences;
        IReadOnlyList<string> discoveredFonts =
            systemFonts.GetMonospaceFontFamilies();
        MonospaceFontFamilies = discoveredFonts.Count > 0
            ? discoveredFonts
            : [EditorPreferencesService.DefaultFontFamily];
        DefaultEditorFontFamily = FindAvailableFont(
            EditorPreferencesService.DefaultFontFamily)
            ?? FindAvailableFont("Consolas")
            ?? MonospaceFontFamilies[0];
        if (FindAvailableFont(_preferences.EditorFontFamily) is not string selectedFont)
        {
            _preferences.SetEditorFontFamily(DefaultEditorFontFamily);
        }
        else if (!string.Equals(
            selectedFont,
            _preferences.EditorFontFamily,
            StringComparison.Ordinal))
        {
            _preferences.SetEditorFontFamily(selectedFont);
        }

        AppThemes =
        [
            "Use system setting",
            "Light",
            "Dark"
        ];
        VersionText = GetVersionText();
        Refresh();
    }

    public IReadOnlyList<string> MonospaceFontFamilies { get; }

    public string[] AppThemes { get; }

    public string VersionText { get; }

    public string DefaultEditorFontFamily { get; }

    public string EditorDefaultsDescription =>
        $"Restore {DefaultEditorFontFamily} at " +
        $"{EditorPreferencesService.DefaultFontSize:0} pt.";

    public FontFamily EditorPreviewFontFamily => new(EditorFontFamily);

    public string EditorPreviewDescription =>
        $"{EditorFontFamily} · {EditorFontSize:0} pt";

    [ObservableProperty]
    public partial double EditorFontSize { get; set; }

    [ObservableProperty]
    public partial string EditorFontFamily { get; set; } =
        EditorPreferencesService.DefaultFontFamily;

    [ObservableProperty]
    public partial int SelectedAppThemeIndex { get; set; }

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _preferences.Changed += Preferences_Changed;
        Refresh();
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _preferences.Changed -= Preferences_Changed;
    }

    partial void OnEditorFontSizeChanged(double value)
    {
        OnPropertyChanged(nameof(EditorPreviewDescription));
        if (!_isRefreshing && double.IsFinite(value))
        {
            _preferences.SetEditorFontSize(value);
        }
    }

    partial void OnEditorFontFamilyChanged(string value)
    {
        OnPropertyChanged(nameof(EditorPreviewFontFamily));
        OnPropertyChanged(nameof(EditorPreviewDescription));
        if (!_isRefreshing && !string.IsNullOrWhiteSpace(value))
        {
            _preferences.SetEditorFontFamily(value);
        }
    }

    partial void OnSelectedAppThemeIndexChanged(int value)
    {
        if (!_isRefreshing && value >= 0 && value < AppThemes.Length)
        {
            _preferences.SetAppTheme(value switch
            {
                1 => ElementTheme.Light,
                2 => ElementTheme.Dark,
                _ => ElementTheme.Default
            });
        }
    }

    [RelayCommand]
    private void ResetEditorSettings()
    {
        _preferences.SetEditorFontSize(EditorPreferencesService.DefaultFontSize);
        _preferences.SetEditorFontFamily(DefaultEditorFontFamily);
    }

    private void Preferences_Changed(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        _isRefreshing = true;
        try
        {
            EditorFontSize = _preferences.EditorFontSize;
            EditorFontFamily = _preferences.EditorFontFamily;
            SelectedAppThemeIndex = _preferences.AppTheme switch
            {
                ElementTheme.Light => 1,
                ElementTheme.Dark => 2,
                _ => 0
            };
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private static string GetVersionText()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"Version {version.Major}.{version.Minor}.{version.Build}";
        }
        catch (InvalidOperationException)
        {
            // File-system Native AOT publishes run without package identity.
            return "Version 1.0.0";
        }
    }

    private string? FindAvailableFont(string fontFamily)
    {
        return MonospaceFontFamilies.FirstOrDefault(candidate =>
            string.Equals(
                candidate,
                fontFamily,
                StringComparison.CurrentCultureIgnoreCase));
    }
}
