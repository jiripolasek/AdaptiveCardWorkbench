using System.Collections.ObjectModel;
using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace AdaptiveCardWorkbench.ViewModels;

public sealed partial class ArchivePageViewModel : ObservableObject
{
    private readonly WorkspaceService _workspace;
    private readonly IArchiveDialogService _dialogs;
    private bool _isActive;

    public ArchivePageViewModel(
        WorkspaceService workspace,
        IArchiveDialogService dialogs)
    {
        _workspace = workspace;
        _dialogs = dialogs;
    }

    public ObservableCollection<ArchiveGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    public partial Visibility ArchiveVisibility { get; set; } =
        Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EmptyStateVisibility { get; set; } =
        Visibility.Visible;

    public async Task ActivateAsync()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _workspace.ProjectsChanged += Workspace_Changed;
        _workspace.DocumentsChanged += Workspace_Changed;
        await _workspace.InitializeAsync();
        RefreshItems();
    }

    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _workspace.ProjectsChanged -= Workspace_Changed;
        _workspace.DocumentsChanged -= Workspace_Changed;
    }

    private void Workspace_Changed(object? sender, EventArgs e)
    {
        RefreshItems();
    }

    private void RefreshItems()
    {
        WorkbenchProject? workspace = _workspace.Project;
        List<(DateTimeOffset SortDate, ArchiveGroupViewModel Group)> groups = [];

        if (workspace is not null)
        {
            foreach (CardProject project in _workspace.ArchivedProjects)
            {
                CardDocument[] cards = workspace.Pages
                    .Where(document => document.ProjectId == project.Id)
                    .OrderByDescending(document => document.LastModified)
                    .ToArray();
                ArchiveGroupViewModel group = CreateGroup(
                    project.Id,
                    project.Name,
                    $"Archived project · {FormatCardCount(cards.Length)} · " +
                        $"{project.LastModified:g}",
                    isArchivedProject: true);
                AddCards(group, cards, projectIsArchived: true);
                groups.Add((project.LastModified, group));
            }

            foreach (CardProject project in _workspace.ActiveProjects)
            {
                CardDocument[] cards = workspace.Pages
                    .Where(document =>
                        document.ProjectId == project.Id &&
                        document.IsArchived)
                    .OrderByDescending(document => document.LastModified)
                    .ToArray();
                if (cards.Length == 0)
                {
                    continue;
                }

                ArchiveGroupViewModel group = CreateGroup(
                    project.Id,
                    project.Name,
                    $"Active project · {FormatArchivedCardCount(cards.Length)}",
                    isArchivedProject: false);
                AddCards(group, cards, projectIsArchived: false);
                groups.Add((cards.Max(document => document.LastModified), group));
            }

            CardDocument[] generalCards = workspace.Pages
                .Where(document =>
                    document.ProjectId is null &&
                    document.IsArchived)
                .OrderByDescending(document => document.LastModified)
                .ToArray();
            if (generalCards.Length > 0)
            {
                ArchiveGroupViewModel generalGroup = CreateGroup(
                    projectId: null,
                    "General",
                    $"Cards without a project · " +
                        FormatArchivedCardCount(generalCards.Length),
                    isArchivedProject: false);
                AddCards(
                    generalGroup,
                    generalCards,
                    projectIsArchived: false);
                groups.Add((
                    generalCards.Max(document => document.LastModified),
                    generalGroup));
            }
        }

        Groups.Clear();
        foreach ((DateTimeOffset _, ArchiveGroupViewModel group) in
                 groups.OrderByDescending(item => item.SortDate))
        {
            Groups.Add(group);
        }

        bool isEmpty = Groups.Count == 0;
        ArchiveVisibility = isEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        EmptyStateVisibility = isEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ArchiveGroupViewModel CreateGroup(
        Guid? projectId,
        string name,
        string description,
        bool isArchivedProject)
    {
        return new ArchiveGroupViewModel(
            projectId,
            name,
            description,
            isArchivedProject,
            RestoreProjectAsync,
            DeleteProjectAsync);
    }

    private void AddCards(
        ArchiveGroupViewModel group,
        IEnumerable<CardDocument> cards,
        bool projectIsArchived)
    {
        foreach (CardDocument document in cards)
        {
            group.Cards.Add(new ArchiveEntryViewModel(
                document.Id,
                document.Name,
                document.IsArchived
                    ? $"Card archived {document.LastModified:g}"
                    : $"Last edited {document.LastModified:g}",
                showActions: !projectIsArchived,
                RestoreCardAsync,
                DeleteCardAsync));
        }
    }

    private async Task RestoreProjectAsync(ArchiveGroupViewModel group)
    {
        if (!group.IsArchivedProject || group.ProjectId is not Guid projectId)
        {
            return;
        }

        _workspace.RestoreProject(projectId, selectDocument: false);
        await SaveWorkspaceStateAsync();
    }

    private async Task DeleteProjectAsync(ArchiveGroupViewModel group)
    {
        if (!group.IsArchivedProject ||
            group.ProjectId is not Guid projectId ||
            !await _dialogs.ConfirmDeleteProjectAsync(group))
        {
            return;
        }

        _workspace.DeleteProject(projectId);
        await SaveWorkspaceStateAsync();
    }

    private async Task RestoreCardAsync(ArchiveEntryViewModel entry)
    {
        _workspace.RestoreDocument(entry.Id, selectDocument: false);
        await SaveWorkspaceStateAsync();
    }

    private async Task DeleteCardAsync(ArchiveEntryViewModel entry)
    {
        if (!await _dialogs.ConfirmDeleteCardAsync(entry))
        {
            return;
        }

        _workspace.DeleteArchivedDocument(entry.Id);
        await SaveWorkspaceStateAsync();
    }

    private async Task SaveWorkspaceStateAsync()
    {
        try
        {
            await _workspace.SaveAsync();
        }
        catch (Exception exception)
        {
            await _dialogs.ShowAutosaveErrorAsync(exception);
        }
    }

    private static string FormatCardCount(int count)
    {
        return count == 1
            ? "1 card"
            : $"{count} cards";
    }

    private static string FormatArchivedCardCount(int count)
    {
        return count == 1
            ? "1 archived card"
            : $"{count} archived cards";
    }
}
