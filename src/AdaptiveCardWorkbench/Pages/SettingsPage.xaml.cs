using AdaptiveCardWorkbench.Services;
using AdaptiveCardWorkbench.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdaptiveCardWorkbench.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = new SettingsPageViewModel(
            App.EditorPreferences,
            new SystemFontService());
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Activate();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Deactivate();
    }
}
