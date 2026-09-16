using AdaptiveCardWorkbench.Models;

namespace AdaptiveCardWorkbench.Services;

public sealed class WorkspaceService
{
    private readonly DraftStorageService _storage;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private Task? _initializationTask;

    public WorkspaceService(DraftStorageService storage)
    {
        _storage = storage;
    }

    public WorkbenchProject? Project { get; private set; }

    public CardDocument? ActiveDocument { get; private set; }

    public IReadOnlyList<CardDocument> VisibleDocuments =>
        Project?.Pages
            .Where(IsDocumentVisible)
            .ToArray()
        ?? [];

    public IReadOnlyList<CardProject> ActiveProjects =>
        Project?.Projects
            .Where(project => !project.IsArchived)
            .ToArray()
        ?? [];

    public IReadOnlyList<CardProject> ArchivedProjects =>
        Project?.Projects
            .Where(project => project.IsArchived)
            .OrderByDescending(project => project.LastModified)
            .ToArray()
        ?? [];

    public IReadOnlyList<CardDocument> ArchivedDocuments =>
        Project?.Pages
            .Where(document =>
                document.IsArchived &&
                (document.ProjectId is not Guid projectId ||
                 GetProject(projectId) is { IsArchived: false }))
            .OrderByDescending(document => document.LastModified)
            .ToArray()
        ?? [];

    public event EventHandler? ProjectsChanged;

    public event EventHandler? DocumentsChanged;

    public event EventHandler? ActiveDocumentChanged;

    public Task InitializeAsync()
    {
        return _initializationTask ??= InitializeCoreAsync();
    }

    public CardProject AddProject(string name)
    {
        WorkbenchProject workspace = GetWorkspace();
        CardProject project = new()
        {
            Name = GetUniqueProjectName(
                string.IsNullOrWhiteSpace(name)
                    ? "Untitled project"
                    : name.Trim())
        };
        workspace.Projects.Add(project);

        CardDocument document = new()
        {
            Name = "Card 1",
            ProjectId = project.Id
        };
        workspace.Pages.Add(document);

        ProjectsChanged?.Invoke(this, EventArgs.Empty);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        SelectDocument(document);
        return project;
    }

    public void RenameProject(Guid projectId, string name)
    {
        CardProject? project = GetProject(projectId);
        if (project is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        project.Name = GetUniqueProjectName(name.Trim(), project.Id);
        project.LastModified = DateTimeOffset.Now;
        ProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ArchiveProject(Guid projectId)
    {
        CardProject? project = GetProject(projectId);
        if (project is null || project.IsArchived)
        {
            return;
        }

        CardDocument? activeDocument = ActiveDocument?.ProjectId == projectId
            ? ActiveDocument
            : null;
        project.IsArchived = true;
        project.LastModified = DateTimeOffset.Now;
        ProjectsChanged?.Invoke(this, EventArgs.Empty);
        if (activeDocument is not null)
        {
            SelectNearestVisibleDocument(activeDocument);
        }
    }

    public void RestoreProject(
        Guid projectId,
        bool selectDocument = true)
    {
        CardProject? project = GetProject(projectId);
        if (project is null || !project.IsArchived)
        {
            return;
        }

        project.IsArchived = false;
        project.LastModified = DateTimeOffset.Now;
        ProjectsChanged?.Invoke(this, EventArgs.Empty);

        CardDocument? document = GetWorkspace().Pages
            .Where(candidate =>
                candidate.ProjectId == projectId &&
                !candidate.IsArchived)
            .OrderByDescending(candidate => candidate.LastAccessed)
            .FirstOrDefault();
        if (selectDocument && document is not null)
        {
            SelectDocument(document);
        }
    }

    public void DeleteProject(Guid projectId)
    {
        WorkbenchProject workspace = GetWorkspace();
        CardProject? project = GetProject(projectId);
        if (project is null)
        {
            return;
        }

        List<CardDocument> documents = workspace.Pages
            .Where(document => document.ProjectId == projectId)
            .ToList();
        bool deletesActiveDocument =
            ActiveDocument?.ProjectId == projectId;
        int nearestIndex = documents.Count == 0
            ? workspace.Pages.Count
            : documents
                .Select(document => workspace.Pages.IndexOf(document))
                .Min();

        foreach (CardDocument document in documents)
        {
            workspace.Pages.Remove(document);
        }

        workspace.Projects.Remove(project);
        if (deletesActiveDocument)
        {
            SetActiveDocument(FindNearestVisibleDocument(nearestIndex));
        }

        ProjectsChanged?.Invoke(this, EventArgs.Empty);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public CardDocument AddDocument(Guid? projectId = null)
    {
        WorkbenchProject workspace = GetWorkspace();
        if (projectId is Guid id &&
            GetProject(id) is not { IsArchived: false })
        {
            projectId = null;
        }

        CardDocument document = new()
        {
            Name = GetUniqueDocumentName("Card", projectId),
            ProjectId = projectId
        };
        workspace.Pages.Add(document);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        SelectDocument(document);
        return document;
    }

    public CardDocument? DuplicateDocument(Guid documentId)
    {
        CardDocument? source = GetDocument(documentId);
        if (source is null)
        {
            return null;
        }

        CardDocument copy = new()
        {
            Name = GetUniqueDocumentName(
                $"{source.Name} copy",
                source.ProjectId),
            ProjectId = source.ProjectId,
            PayloadJson = source.PayloadJson,
            DataJson = source.DataJson
        };
        GetWorkspace().Pages.Add(copy);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        SelectDocument(copy);
        return copy;
    }

    public void RenameDocument(Guid documentId, string name)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        document.Name = name.Trim();
        document.LastModified = DateTimeOffset.Now;
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        if (ReferenceEquals(document, ActiveDocument))
        {
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveDocument(Guid documentId, Guid? targetProjectId)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is null || document.ProjectId == targetProjectId)
        {
            return;
        }

        if (targetProjectId is Guid projectId &&
            GetProject(projectId) is not { IsArchived: false })
        {
            return;
        }

        document.ProjectId = targetProjectId;
        document.Name = GetUniqueDocumentName(
            document.Name,
            targetProjectId,
            document.Id);
        document.LastModified = DateTimeOffset.Now;
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ArchiveDocument(Guid documentId)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is null || document.IsArchived)
        {
            return;
        }

        bool wasActive = ReferenceEquals(document, ActiveDocument);
        document.IsArchived = true;
        document.LastModified = DateTimeOffset.Now;
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        if (wasActive)
        {
            SelectNearestVisibleDocument(document);
        }
    }

    public void RestoreDocument(
        Guid documentId,
        bool selectDocument = true)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is null || !document.IsArchived)
        {
            return;
        }

        document.IsArchived = false;
        document.LastModified = DateTimeOffset.Now;
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        if (selectDocument)
        {
            SelectDocument(document);
        }
    }

    public void DeleteDocument(Guid documentId)
    {
        WorkbenchProject workspace = GetWorkspace();
        CardDocument? document = GetDocument(documentId);
        if (document is null)
        {
            return;
        }

        int nearestIndex = workspace.Pages.IndexOf(document);
        bool deletesActiveDocument = ReferenceEquals(document, ActiveDocument);
        workspace.Pages.Remove(document);
        if (deletesActiveDocument)
        {
            SetActiveDocument(FindNearestVisibleDocument(nearestIndex));
        }

        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteArchivedDocument(Guid documentId)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is null || !document.IsArchived)
        {
            return;
        }

        DeleteDocument(documentId);
    }

    public void SelectDocument(Guid documentId)
    {
        CardDocument? document = GetDocument(documentId);
        if (document is not null && IsDocumentVisible(document))
        {
            SelectDocument(document);
        }
    }

    public IReadOnlyList<CardDocument> GetDocuments(
        Guid? projectId,
        bool archived)
    {
        IEnumerable<CardDocument> documents = GetWorkspace().Pages
            .Where(document =>
                document.ProjectId == projectId &&
                document.IsArchived == archived);

        if (archived)
        {
            return documents
                .OrderByDescending(document => document.LastModified)
                .ToArray();
        }

        return GetProjectSortMode(projectId) switch
        {
            CardSortMode.RecentlyChanged => documents
                .OrderByDescending(document => document.LastModified)
                .ToArray(),
            CardSortMode.RecentlyAccessed => documents
                .OrderByDescending(document => document.LastAccessed)
                .ToArray(),
            _ => documents.ToArray()
        };
    }

    public CardSortMode GetProjectSortMode(Guid? projectId)
    {
        if (projectId is null)
        {
            return GetWorkspace().GeneralSortMode;
        }

        return GetProject(projectId.Value)?.SortMode
            ?? CardSortMode.ProjectOrder;
    }

    public void SetProjectSortMode(Guid? projectId, CardSortMode sortMode)
    {
        if (projectId is null)
        {
            GetWorkspace().GeneralSortMode = sortMode;
        }
        else if (GetProject(projectId.Value) is CardProject project)
        {
            project.SortMode = sortMode;
        }
        else
        {
            return;
        }

        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public CardProject? GetProject(Guid projectId)
    {
        return Project?.Projects.FirstOrDefault(project => project.Id == projectId);
    }

    public CardDocument? GetDocument(Guid documentId)
    {
        return Project?.Pages.FirstOrDefault(document => document.Id == documentId);
    }

    public async Task SaveAsync()
    {
        await _saveGate.WaitAsync();
        try
        {
            if (Project is WorkbenchProject workspace)
            {
                await _storage.SaveAsync(workspace);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task InitializeCoreAsync()
    {
        Project = await _storage.LoadAsync();
        ActiveDocument = Project.ActivePageId is Guid activePageId
            ? Project.Pages.FirstOrDefault(document =>
                document.Id == activePageId &&
                IsDocumentVisible(document))
            : null;
        ActiveDocument ??= Project.Pages.FirstOrDefault(
            IsDocumentVisible);
        Project.ActivePageId = ActiveDocument?.Id;
        if (ActiveDocument is not null)
        {
            ActiveDocument.LastAccessed = DateTimeOffset.Now;
        }

        ProjectsChanged?.Invoke(this, EventArgs.Empty);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SelectNearestVisibleDocument(CardDocument archivedDocument)
    {
        WorkbenchProject workspace = GetWorkspace();
        int archivedIndex = workspace.Pages.IndexOf(archivedDocument);
        CardDocument? nextDocument = workspace.Pages
            .Skip(archivedIndex + 1)
            .FirstOrDefault(IsDocumentVisible)
            ?? workspace.Pages
                .Take(archivedIndex)
                .LastOrDefault(IsDocumentVisible);
        SetActiveDocument(nextDocument);
    }

    private CardDocument? FindNearestVisibleDocument(int preferredIndex)
    {
        List<CardDocument> documents = GetWorkspace().Pages;
        int boundedIndex = Math.Clamp(preferredIndex, 0, documents.Count);
        return documents
            .Skip(boundedIndex)
            .FirstOrDefault(IsDocumentVisible)
            ?? documents
                .Take(boundedIndex)
                .LastOrDefault(IsDocumentVisible);
    }

    private void SelectDocument(CardDocument document)
    {
        Guid? previousProjectId = ActiveDocument?.ProjectId;
        document.LastAccessed = DateTimeOffset.Now;
        bool shouldReorder =
            GetProjectSortMode(previousProjectId) != CardSortMode.ProjectOrder ||
            GetProjectSortMode(document.ProjectId) != CardSortMode.ProjectOrder;
        if (shouldReorder)
        {
            DocumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        if (!ReferenceEquals(document, ActiveDocument))
        {
            SetActiveDocument(document);
        }
    }

    private void SetActiveDocument(CardDocument? document)
    {
        ActiveDocument = document;
        GetWorkspace().ActivePageId = document?.Id;
        ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool IsDocumentVisible(CardDocument document)
    {
        return !document.IsArchived &&
            (document.ProjectId is not Guid projectId ||
             GetProject(projectId) is { IsArchived: false });
    }

    private string GetUniqueProjectName(string baseName, Guid? exceptId = null)
    {
        string candidate = baseName;
        int suffix = 2;
        while (GetWorkspace().Projects.Any(project =>
                   project.Id != exceptId &&
                   string.Equals(
                       project.Name,
                       candidate,
                       StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private string GetUniqueDocumentName(
        string baseName,
        Guid? projectId,
        Guid? exceptId = null)
    {
        string candidate = baseName;
        int suffix = 2;
        while (GetWorkspace().Pages.Any(document =>
                   document.Id != exceptId &&
                   document.ProjectId == projectId &&
                   string.Equals(
                       document.Name,
                       candidate,
                       StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private WorkbenchProject GetWorkspace()
    {
        return Project ?? throw new InvalidOperationException(
            "The workspace must be initialized before it can be used.");
    }
}
