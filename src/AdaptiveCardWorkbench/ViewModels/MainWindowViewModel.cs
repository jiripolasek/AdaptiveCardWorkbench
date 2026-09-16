using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;

namespace AdaptiveCardWorkbench.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string ProjectIconGlyph = "\uE8B7";
    private const string DocumentIconGlyph = "\uE8A5";

    private readonly WorkspaceService _workspace;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Dictionary<string, ShellNavigationItemViewModel> _projectItems = [];
    private readonly ShellNavigationItemViewModel _newCardItem = new(
        ShellNavigationItemKind.NewCard,
        "New card",
        "\uE710");
    private readonly ShellNavigationItemViewModel _newProjectItem = new(
        ShellNavigationItemKind.NewProject,
        "New project",
        "\uE8F4");
    private readonly ShellNavigationSeparatorItemViewModel _commandSeparatorItem = new();
    private readonly ShellNavigationItemViewModel _emptySearchItem = new(
        ShellNavigationItemKind.EmptySearchResult,
        "No matching cards or projects",
        string.Empty);
    private readonly ShellNavigationItemViewModel _archiveItem = new(
        ShellNavigationItemKind.Archive,
        "Archive",
        "\uE7B8");
    private readonly ShellNavigationItemViewModel _resourcesItem = new(
        ShellNavigationItemKind.Resources,
        "Resources",
        "\uE71B");

    private bool _isDisposed;
    private bool _isRefreshPending;
    private bool _expandProjectAfterRefresh;
    private Guid? _projectIdToExpand;

    public MainWindowViewModel(
        WorkspaceService workspace,
        DispatcherQueue dispatcherQueue)
    {
        _workspace = workspace;
        _dispatcherQueue = dispatcherQueue;
        SearchText = string.Empty;
        FooterMenuItems = [_archiveItem, _resourcesItem];
        _workspace.ProjectsChanged += Workspace_Changed;
        _workspace.DocumentsChanged += Workspace_Changed;
        _workspace.ActiveDocumentChanged += Workspace_ActiveDocumentChanged;
    }

    public ObservableCollection<ShellNavigationItemViewModel> MenuItems { get; } = [];

    public ObservableCollection<ShellNavigationItemViewModel> FooterMenuItems { get; }

    public event EventHandler? NavigationItemsChanged;

    public Guid? ActiveProjectId => _workspace.ActiveDocument?.ProjectId;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    public async Task InitializeAsync()
    {
        await _workspace.InitializeAsync();
        Refresh();
    }

    public CardDocument AddDocument(Guid? projectId)
    {
        RequestProjectExpansion(projectId);
        return _workspace.AddDocument(projectId);
    }

    public CardProject AddProject(string name)
    {
        CardProject project = _workspace.AddProject(name);
        RequestProjectExpansion(project.Id);
        return project;
    }

    public CardDocument? DuplicateDocument(Guid documentId)
    {
        CardDocument? source = _workspace.GetDocument(documentId);
        if (source is not null)
        {
            RequestProjectExpansion(source.ProjectId);
        }

        return _workspace.DuplicateDocument(documentId);
    }

    public void MoveDocument(Guid documentId, Guid? projectId)
    {
        RequestProjectExpansion(projectId);
        _workspace.MoveDocument(documentId, projectId);
    }

    public ShellNavigationItemViewModel? FindDocument(Guid documentId)
    {
        return MenuItems
            .SelectMany(projectItem => projectItem.Children)
            .FirstOrDefault(item => item.DocumentId == documentId);
    }

    public ShellNavigationItemViewModel? FindFirstDocument()
    {
        return MenuItems
            .Where(item => item.Kind == ShellNavigationItemKind.Project)
            .SelectMany(item => item.Children)
            .FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value)
    {
        QueueRefresh();
    }

    private void Workspace_Changed(object? sender, EventArgs e)
    {
        QueueRefresh();
    }

    private void Workspace_ActiveDocumentChanged(object? sender, EventArgs e)
    {
        if (_workspace.ActiveDocument is CardDocument activeDocument)
        {
            RequestProjectExpansion(activeDocument.ProjectId);
        }

        QueueRefresh();
    }

    private void RequestProjectExpansion(Guid? projectId)
    {
        _expandProjectAfterRefresh = true;
        _projectIdToExpand = projectId;

        if (_projectItems.TryGetValue(GetProjectKey(projectId), out ShellNavigationItemViewModel? projectItem))
        {
            projectItem.IsExpanded = true;
        }
    }

    private void QueueRefresh()
    {
        if (_isDisposed || _isRefreshPending)
        {
            return;
        }

        _isRefreshPending = true;
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                _isRefreshPending = false;
                Refresh();
            }))
        {
            _isRefreshPending = false;
        }
    }

    private void Refresh()
    {
        if (_workspace.Project is not WorkbenchProject workspace)
        {
            return;
        }

        HashSet<string> activeProjectKeys = [GetProjectKey(null)];
        foreach (CardProject project in workspace.Projects.Where(project => !project.IsArchived))
        {
            activeProjectKeys.Add(GetProjectKey(project.Id));
        }

        foreach (string obsoleteKey in _projectItems.Keys
            .Where(key => !activeProjectKeys.Contains(key))
            .ToArray())
        {
            _projectItems.Remove(obsoleteKey);
        }

        List<ShellNavigationItemViewModel> desiredItems = [];
        AddProjectItem(
            desiredItems,
            projectId: null,
            projectName: "General");

        foreach (CardProject project in workspace.Projects.Where(project => !project.IsArchived))
        {
            AddProjectItem(
                desiredItems,
                project.Id,
                project.Name);
        }

        if (!string.IsNullOrWhiteSpace(SearchText) && desiredItems.Count == 0)
        {
            desiredItems.Add(_emptySearchItem);
        }

        desiredItems.Add(_commandSeparatorItem);
        desiredItems.Add(_newCardItem);
        desiredItems.Add(_newProjectItem);
        Synchronize(MenuItems, desiredItems);

        if (_expandProjectAfterRefresh)
        {
            if (_projectItems.TryGetValue(
                GetProjectKey(_projectIdToExpand),
                out ShellNavigationItemViewModel? projectItem))
            {
                projectItem.IsExpanded = true;
            }

            _expandProjectAfterRefresh = false;
        }

        NavigationItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddProjectItem(
        ICollection<ShellNavigationItemViewModel> desiredItems,
        Guid? projectId,
        string projectName)
    {
        IReadOnlyList<CardDocument> documents =
            _workspace.GetDocuments(projectId, archived: false);
        bool projectMatches = MatchesSearch(projectName);
        IReadOnlyList<CardDocument> matchingDocuments =
            string.IsNullOrWhiteSpace(SearchText) || projectMatches
                ? documents
                : [.. documents.Where(document => MatchesSearch(document.Name))];

        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !projectMatches &&
            matchingDocuments.Count == 0)
        {
            return;
        }

        string key = GetProjectKey(projectId);
        if (!_projectItems.TryGetValue(key, out ShellNavigationItemViewModel? projectItem))
        {
            projectItem = new ShellNavigationItemViewModel(
                ShellNavigationItemKind.Project,
                projectName,
                ProjectIconGlyph,
                projectId);
            _projectItems.Add(key, projectItem);
        }
        else
        {
            projectItem.Title = projectName;
        }

        SynchronizeDocuments(projectItem, matchingDocuments);
        desiredItems.Add(projectItem);
    }

    private static void SynchronizeDocuments(
        ShellNavigationItemViewModel projectItem,
        IReadOnlyList<CardDocument> documents)
    {
        Dictionary<Guid, ShellNavigationItemViewModel> existingItems =
            projectItem.Children
                .Where(item => item.DocumentId is Guid)
                .ToDictionary(item => item.DocumentId!.Value);
        List<ShellNavigationItemViewModel> desiredItems = [];

        foreach (CardDocument document in documents)
        {
            if (!existingItems.TryGetValue(
                document.Id,
                out ShellNavigationItemViewModel? documentItem))
            {
                documentItem = new ShellNavigationItemViewModel(
                    ShellNavigationItemKind.Document,
                    document.Name,
                    DocumentIconGlyph,
                    document.ProjectId,
                    document.Id);
            }
            else
            {
                documentItem.Title = document.Name;
            }

            desiredItems.Add(documentItem);
        }

        Synchronize(projectItem.Children, desiredItems);
    }

    private bool MatchesSearch(string value)
    {
        return string.IsNullOrWhiteSpace(SearchText) ||
            value.Contains(
                SearchText,
                StringComparison.CurrentCultureIgnoreCase);
    }

    private static string GetProjectKey(Guid? projectId)
    {
        return projectId?.ToString("N") ?? "general";
    }

    private static void Synchronize<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired)
        where T : class
    {
        for (int index = 0; index < desired.Count; index++)
        {
            T item = desired[index];
            if (index < target.Count && ReferenceEquals(target[index], item))
            {
                continue;
            }

            int existingIndex = target.IndexOf(item);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, item);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _workspace.ProjectsChanged -= Workspace_Changed;
        _workspace.DocumentsChanged -= Workspace_Changed;
        _workspace.ActiveDocumentChanged -= Workspace_ActiveDocumentChanged;
    }
}
