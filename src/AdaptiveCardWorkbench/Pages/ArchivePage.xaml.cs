using AdaptiveCardWorkbench.Services;
using AdaptiveCardWorkbench.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdaptiveCardWorkbench.Pages;

public sealed partial class ArchivePage : Page, IArchiveDialogService
{
    private bool _isDialogOpen;

    public ArchivePageViewModel ViewModel { get; }

    public ArchivePage()
    {
        ViewModel = new ArchivePageViewModel(App.Workspace, this);
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.ActivateAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Deactivate();
    }

    public async Task<bool> ConfirmDeleteProjectAsync(ArchiveGroupViewModel group)
    {
        if (_isDialogOpen)
        {
            return false;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {group.Name}?",
            Content = new TextBlock
            {
                Text =
                    $"This permanently deletes the archived project and its " +
                    $"{FormatCardCount(group.Cards.Count)}. " +
                    "This action can't be undone.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete project",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    public async Task<bool> ConfirmDeleteCardAsync(ArchiveEntryViewModel entry)
    {
        if (_isDialogOpen)
        {
            return false;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {entry.Name}?",
            Content = new TextBlock
            {
                Text =
                    "This permanently deletes the archived card. " +
                    "This action can't be undone.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete card",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    public async Task ShowAutosaveErrorAsync(Exception exception)
    {
        if (_isDialogOpen)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = XamlRoot,
            Title = "The workspace could not be autosaved",
            Content = exception.Message,
            CloseButtonText = "Close"
        };
        _isDialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private static string FormatCardCount(int count)
    {
        return count == 1
            ? "1 card"
            : $"{count} cards";
    }
}
