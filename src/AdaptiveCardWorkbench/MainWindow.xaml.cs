using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Pages;
using AdaptiveCardWorkbench.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace AdaptiveCardWorkbench;

public sealed partial class MainWindow : Window
{
    private sealed record ProjectNavigationTag(Guid? ProjectId);

    private sealed record ProjectSortTag(
        Guid? ProjectId,
        CardSortMode SortMode);

    private sealed record MoveDocumentTag(
        Guid DocumentId,
        Guid? ProjectId);

    private bool _isClosingAfterSave;
    private bool _isSavingBeforeClose;
    private bool _isSynchronizingSelection;
    private bool _isDialogOpen;

    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = new MainWindowViewModel(
            App.Workspace,
            DispatcherQueue.GetForCurrentThread());
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        AppWindow.Resize(new SizeInt32(1440, 900));
        AppWindow.Closing += AppWindow_Closing;
        ViewModel.NavigationItemsChanged += ViewModel_NavigationItemsChanged;
        App.EditorPreferences.Changed += EditorPreferences_Changed;
        Closed += MainWindow_Closed;
        ApplyAppTheme();
    }

    private void EditorPreferences_Changed(object? sender, EventArgs e)
    {
        ApplyAppTheme();
    }

    private void ApplyAppTheme()
    {
        RootLayout.RequestedTheme = App.EditorPreferences.AppTheme;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppWindow.Closing -= AppWindow_Closing;
        ViewModel.NavigationItemsChanged -= ViewModel_NavigationItemsChanged;
        App.EditorPreferences.Changed -= EditorPreferences_Changed;
        ViewModel.Dispose();
    }

    private async void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_isClosingAfterSave)
        {
            return;
        }

        args.Cancel = true;
        if (_isSavingBeforeClose)
        {
            return;
        }

        _isSavingBeforeClose = true;
        ShellNavigation.IsEnabled = false;
        try
        {
            await App.Workspace.SaveAsync();
        }
        catch (Exception exception)
        {
            ShellNavigation.IsEnabled = true;
            await ShowErrorAsync(
                "The workspace could not be saved. The window will stay open so you can retry.",
                exception);
            return;
        }
        finally
        {
            _isSavingBeforeClose = false;
        }

        _isClosingAfterSave = true;
        Close();
    }

    private async void ShellNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(
                "The saved workspace could not be opened. Your draft has not been changed.",
                exception);
            Close();
            return;
        }

        ShellNavigation.MenuItemsSource = ViewModel.MenuItems;
        ShellNavigation.FooterMenuItemsSource = ViewModel.FooterMenuItems;
        NavigateToWorkbench();
    }

    private async void ShellNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection)
        {
            return;
        }

        if (args.IsSettingsSelected)
        {
            Navigate(typeof(SettingsPage));
        }
        else if (GetSelectedNavigationItem(args) is ShellNavigationItemViewModel item)
        {
            if (item.Kind == ShellNavigationItemKind.Document &&
                item.DocumentId is Guid documentId)
            {
                App.Workspace.SelectDocument(documentId);
                NavigateToWorkbench();
                await SaveWorkspaceStateAsync();
            }
            else if (item.Kind == ShellNavigationItemKind.Archive)
            {
                Navigate(typeof(ArchivePage));
            }
            else if (item.Kind == ShellNavigationItemKind.Resources)
            {
                Navigate(typeof(ResourcesPage));
            }
        }
    }

    private async void ShellNavigation_ItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not ShellNavigationItemViewModel item)
        {
            return;
        }

        if (item.Kind == ShellNavigationItemKind.NewCard)
        {
            ViewModel.AddDocument(ViewModel.ActiveProjectId);
            NavigateToWorkbench();
            await SaveWorkspaceStateAsync();
        }
        else if (item.Kind == ShellNavigationItemKind.NewProject)
        {
            await CreateProjectAsync();
        }
    }

    private static ShellNavigationItemViewModel? GetSelectedNavigationItem(
        NavigationViewSelectionChangedEventArgs args)
    {
        return args.SelectedItem as ShellNavigationItemViewModel ??
            args.SelectedItemContainer?.Tag as ShellNavigationItemViewModel ??
            args.SelectedItemContainer?.DataContext as ShellNavigationItemViewModel;
    }

    private void ViewModel_NavigationItemsChanged(object? sender, EventArgs e)
    {
        if (ContentFrame.CurrentSourcePageType == typeof(WorkbenchPage))
        {
            SelectActiveNavigationItem();
        }
    }

    private void WorkspaceSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ViewModel.SearchText = sender.Text.Trim();
    }

    private void WorkspaceSearchBox_QuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel.FindFirstDocument() is ShellNavigationItemViewModel firstDocument)
        {
            ShellNavigation.SelectedItem = firstDocument;
        }
    }

    private void NavigationItem_ContextRequested(
        UIElement sender,
        ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement
            {
                Tag: ShellNavigationItemViewModel item
            })
        {
            return;
        }

        MenuFlyout? menu = item.Kind switch
        {
            ShellNavigationItemKind.Project =>
                CreateProjectContextMenu(item.ProjectId),
            ShellNavigationItemKind.Document
                when item.DocumentId is Guid documentId &&
                    App.Workspace.GetDocument(documentId) is CardDocument document =>
                CreateCardContextMenu(document),
            _ => null
        };
        if (menu is null)
        {
            return;
        }

        menu.ShowAt((FrameworkElement)sender);
        args.Handled = true;
    }

    private MenuFlyout CreateProjectContextMenu(Guid? projectId)
    {
        MenuFlyout menu = new();

        MenuFlyoutItem newCardItem = new()
        {
            Text = "New card",
            Icon = new FontIcon { Glyph = "\uE710" },
            Tag = new ProjectNavigationTag(projectId)
        };
        newCardItem.Click += NewCardInProjectMenuItem_Click;
        menu.Items.Add(newCardItem);

        if (projectId is Guid)
        {
            MenuFlyoutItem renameItem = new()
            {
                Text = "Rename project",
                Icon = new FontIcon { Glyph = "\uE8AC" },
                Tag = new ProjectNavigationTag(projectId)
            };
            renameItem.Click += RenameProjectMenuItem_Click;
            menu.Items.Add(renameItem);
        }

        MenuFlyoutSubItem sortItem = new()
        {
            Text = "Sort cards",
            Icon = new FontIcon { Glyph = "\uE8CB" }
        };
        CardSortMode currentSortMode =
            App.Workspace.GetProjectSortMode(projectId);
        AddSortMenuItem(
            sortItem,
            projectId,
            CardSortMode.ProjectOrder,
            "Project order",
            currentSortMode);
        AddSortMenuItem(
            sortItem,
            projectId,
            CardSortMode.RecentlyChanged,
            "Recently changed",
            currentSortMode);
        AddSortMenuItem(
            sortItem,
            projectId,
            CardSortMode.RecentlyAccessed,
            "Recently accessed",
            currentSortMode);
        menu.Items.Add(sortItem);

        if (projectId is Guid)
        {
            menu.Items.Add(new MenuFlyoutSeparator());

            MenuFlyoutItem archiveProjectItem = new()
            {
                Text = "Archive project",
                Icon = new FontIcon { Glyph = "\uE7B8" },
                Tag = new ProjectNavigationTag(projectId)
            };
            archiveProjectItem.Click += ArchiveProjectMenuItem_Click;
            menu.Items.Add(archiveProjectItem);

            MenuFlyoutItem deleteProjectItem = new()
            {
                Text = "Delete project…",
                Icon = new FontIcon { Glyph = "\uE74D" },
                Tag = new ProjectNavigationTag(projectId)
            };
            deleteProjectItem.Click += DeleteProjectMenuItem_Click;
            menu.Items.Add(deleteProjectItem);
        }

        return menu;
    }

    private void AddSortMenuItem(
        MenuFlyoutSubItem parent,
        Guid? projectId,
        CardSortMode sortMode,
        string text,
        CardSortMode currentSortMode)
    {
        ToggleMenuFlyoutItem item = new()
        {
            Text = text,
            IsChecked = sortMode == currentSortMode,
            Tag = new ProjectSortTag(projectId, sortMode)
        };
        item.Click += SortProjectCardsMenuItem_Click;
        parent.Items.Add(item);
    }

    private async void SortProjectCardsMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                Tag: ProjectSortTag sortTag
            })
        {
            App.Workspace.SetProjectSortMode(
                sortTag.ProjectId,
                sortTag.SortMode);
            await SaveWorkspaceStateAsync();
        }
    }

    private MenuFlyout CreateCardContextMenu(CardDocument document)
    {
        MenuFlyout menu = new();

        MenuFlyoutItem renameItem = new()
        {
            Text = "Rename",
            Icon = new FontIcon { Glyph = "\uE8AC" },
            Tag = document.Id
        };
        renameItem.Click += RenameCardMenuItem_Click;
        menu.Items.Add(renameItem);

        MenuFlyoutItem duplicateItem = new()
        {
            Text = "Duplicate",
            Icon = new FontIcon { Glyph = "\uE8B0" },
            Tag = document.Id
        };
        duplicateItem.Click += DuplicateCardMenuItem_Click;
        menu.Items.Add(duplicateItem);

        MenuFlyoutSubItem moveItem = new()
        {
            Text = "Move to project",
            Icon = new FontIcon { Glyph = "\uE8DE" },
            IsEnabled = App.Workspace.ActiveProjects.Count > 0
        };
        AddMoveTarget(
            moveItem,
            document,
            projectId: null,
            projectName: "General");
        foreach (CardProject project in App.Workspace.ActiveProjects)
        {
            AddMoveTarget(
                moveItem,
                document,
                project.Id,
                project.Name);
        }

        menu.Items.Add(moveItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem archiveItem = new()
        {
            Text = "Archive",
            Icon = new FontIcon { Glyph = "\uE7B8" },
            Tag = document.Id
        };
        archiveItem.Click += ArchiveCardMenuItem_Click;
        menu.Items.Add(archiveItem);

        MenuFlyoutItem deleteItem = new()
        {
            Text = "Delete…",
            Icon = new FontIcon { Glyph = "\uE74D" },
            Tag = document.Id
        };
        deleteItem.Click += DeleteCardMenuItem_Click;
        menu.Items.Add(deleteItem);

        return menu;
    }

    private void AddMoveTarget(
        MenuFlyoutSubItem parent,
        CardDocument document,
        Guid? projectId,
        string projectName)
    {
        ToggleMenuFlyoutItem item = new()
        {
            Text = projectName,
            IsChecked = document.ProjectId == projectId,
            IsEnabled = document.ProjectId != projectId,
            Tag = new MoveDocumentTag(document.Id, projectId)
        };
        item.Click += MoveCardToProjectMenuItem_Click;
        parent.Items.Add(item);
    }

    private async void MoveCardToProjectMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                Tag: MoveDocumentTag moveTag
            })
        {
            ViewModel.MoveDocument(
                moveTag.DocumentId,
                moveTag.ProjectId);
            await SaveWorkspaceStateAsync();
        }
    }

    private async void NewCardInProjectMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                Tag: ProjectNavigationTag projectTag
            })
        {
            ViewModel.AddDocument(projectTag.ProjectId);
            NavigateToWorkbench();
            await SaveWorkspaceStateAsync();
        }
    }

    private async void RenameProjectMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                Tag: ProjectNavigationTag { ProjectId: Guid projectId }
            } ||
            App.Workspace.GetProject(projectId) is not CardProject project)
        {
            return;
        }

        string? name = await PromptForNameAsync(
            "Rename project",
            "Rename",
            project.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            App.Workspace.RenameProject(projectId, name);
            await SaveWorkspaceStateAsync();
        }
    }

    private async void RenameCardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid documentId } ||
            App.Workspace.GetDocument(documentId) is not CardDocument document)
        {
            return;
        }

        string? name = await PromptForNameAsync(
            "Rename card",
            "Rename",
            document.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            App.Workspace.RenameDocument(documentId, name);
            await SaveWorkspaceStateAsync();
        }
    }

    private async void DuplicateCardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid documentId })
        {
            ViewModel.DuplicateDocument(documentId);
            await SaveWorkspaceStateAsync();
        }
    }

    private async void ArchiveCardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid documentId })
        {
            App.Workspace.ArchiveDocument(documentId);
            await SaveWorkspaceStateAsync();
        }
    }

    private async void DeleteCardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid documentId })
        {
            await ConfirmDeleteCardAsync(documentId);
        }
    }

    private async void ArchiveProjectMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                Tag: ProjectNavigationTag { ProjectId: Guid projectId }
            })
        {
            App.Workspace.ArchiveProject(projectId);
            NavigateToWorkbench();
            await SaveWorkspaceStateAsync();
        }
    }

    private async void DeleteProjectMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                Tag: ProjectNavigationTag { ProjectId: Guid projectId }
            })
        {
            await ConfirmDeleteProjectAsync(projectId);
        }
    }

    private async Task CreateProjectAsync()
    {
        string? name = await PromptForNameAsync(
            "New project",
            "Create",
            "Untitled project");
        if (string.IsNullOrWhiteSpace(name))
        {
            SelectActiveNavigationItem();
            return;
        }

        ViewModel.AddProject(name);
        NavigateToWorkbench();
        await SaveWorkspaceStateAsync();
    }

    private async Task<string?> PromptForNameAsync(
        string title,
        string primaryButtonText,
        string initialName)
    {
        if (_isDialogOpen)
        {
            return null;
        }

        TextBox nameEditor = new()
        {
            Text = initialName,
            SelectionStart = 0,
            SelectionLength = initialName.Length
        };
        ContentDialog dialog = new()
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = title,
            Content = nameEditor,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? nameEditor.Text
                : null;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private async Task ConfirmDeleteProjectAsync(Guid projectId)
    {
        if (_isDialogOpen ||
            App.Workspace.GetProject(projectId) is not CardProject project)
        {
            return;
        }

        int cardCount = App.Workspace.Project?.Pages.Count(
            document => document.ProjectId == projectId) ?? 0;
        string cardLabel = cardCount == 1
            ? "1 card"
            : $"{cardCount} cards";
        ContentDialog dialog = new()
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = $"Delete {project.Name}?",
            Content = new TextBlock
            {
                Text = $"This permanently deletes the project and its {cardLabel}. This action can't be undone. Archive the project instead if you may need it later.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete project",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        _isDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        finally
        {
            _isDialogOpen = false;
        }

        App.Workspace.DeleteProject(projectId);
        NavigateToWorkbench();
        await SaveWorkspaceStateAsync();
    }

    private async Task ConfirmDeleteCardAsync(Guid documentId)
    {
        if (_isDialogOpen ||
            App.Workspace.GetDocument(documentId) is not CardDocument document)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = $"Delete {document.Name}?",
            Content = new TextBlock
            {
                Text = "This permanently deletes the card. This action can't be undone. Archive the card instead if you may need it later.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Delete card",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        _isDialogOpen = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        finally
        {
            _isDialogOpen = false;
        }

        App.Workspace.DeleteDocument(documentId);
        await SaveWorkspaceStateAsync();
    }

    private void SelectActiveNavigationItem()
    {
        ShellNavigationItemViewModel? navigationItem =
            App.Workspace.ActiveDocument?.Id is Guid activeId
                ? ViewModel.FindDocument(activeId)
                : null;
        if (ReferenceEquals(ShellNavigation.SelectedItem, navigationItem))
        {
            return;
        }

        _isSynchronizingSelection = true;
        try
        {
            ShellNavigation.SelectedItem = navigationItem;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private async Task SaveWorkspaceStateAsync()
    {
        try
        {
            await App.Workspace.SaveAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(
                "The workspace could not be autosaved",
                exception);
        }
    }

    private async Task ShowErrorAsync(string title, Exception exception)
    {
        if (_isDialogOpen)
        {
            return;
        }

        _isDialogOpen = true;
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = ShellNavigation.XamlRoot,
                Title = title,
                Content = exception.Message,
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private void NavigateToWorkbench()
    {
        Navigate(typeof(WorkbenchPage));
        SelectActiveNavigationItem();
    }

    private void Navigate(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
