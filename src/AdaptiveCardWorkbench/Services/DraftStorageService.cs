using System.Text.Json;
using AdaptiveCardWorkbench.Models;
using Windows.Storage;

namespace AdaptiveCardWorkbench.Services;

public sealed class DraftStorageService
{
    private const string DraftFileName = "draft-project.json";
    private const int CurrentFormatVersion = 3;
    private readonly string _draftPath;

    public DraftStorageService(string? folderPath = null)
    {
        _draftPath = Path.Combine(
            folderPath ?? ApplicationData.Current.LocalFolder.Path,
            DraftFileName);
    }

    public async Task<WorkbenchProject> LoadAsync()
    {
        try
        {
            string json = await File.ReadAllTextAsync(_draftPath);
            WorkbenchProject workspace =
                JsonSerializer.Deserialize(
                    json,
                    WorkbenchJsonSerializerContext.Default.WorkbenchProject)
                ?? throw new JsonException("The saved workspace is null.");
            return NormalizeWorkspace(workspace);
        }
        catch (FileNotFoundException)
        {
            return CreateDefaultWorkspace();
        }
    }

    public async Task SaveAsync(WorkbenchProject workspace)
    {
        workspace.LastModified = DateTimeOffset.Now;
        string json = JsonSerializer.Serialize(
            workspace,
            WorkbenchJsonSerializerContext.Default.WorkbenchProject);
        string temporaryPath = _draftPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json);
        File.Move(temporaryPath, _draftPath, overwrite: true);
    }

    private static WorkbenchProject NormalizeWorkspace(WorkbenchProject workspace)
    {
        workspace.Id = workspace.Id == Guid.Empty
            ? Guid.NewGuid()
            : workspace.Id;
        workspace.Name = string.IsNullOrWhiteSpace(workspace.Name)
            ? "My card workspace"
            : workspace.Name.Trim();
        workspace.Projects ??= [];
        workspace.Pages ??= [];

        HashSet<Guid> projectIds = [];
        foreach (CardProject project in workspace.Projects)
        {
            if (project.Id == Guid.Empty || !projectIds.Add(project.Id))
            {
                project.Id = Guid.NewGuid();
                projectIds.Add(project.Id);
            }

            project.Name = string.IsNullOrWhiteSpace(project.Name)
                ? "Untitled project"
                : project.Name.Trim();
            if (project.LastModified == default)
            {
                project.LastModified = workspace.LastModified;
            }
        }

        HashSet<Guid> documentIds = [];
        foreach (CardDocument document in workspace.Pages)
        {
            if (document.Id == Guid.Empty || !documentIds.Add(document.Id))
            {
                document.Id = Guid.NewGuid();
                documentIds.Add(document.Id);
            }

            document.Name = string.IsNullOrWhiteSpace(document.Name)
                ? "Untitled card"
                : document.Name.Trim();
            document.PayloadJson ??= Samples.DefaultPayload;
            document.DataJson ??= Samples.DefaultData;
            if (document.ProjectId is Guid projectId &&
                !projectIds.Contains(projectId))
            {
                document.ProjectId = null;
            }
        }

        if (workspace.ActivePageId is not Guid activePageId ||
            workspace.Pages.All(document =>
                document.Id != activePageId ||
                document.IsArchived))
        {
            workspace.ActivePageId = workspace.Pages
                .FirstOrDefault(document => !document.IsArchived)
                ?.Id;
        }

        workspace.FormatVersion = CurrentFormatVersion;
        return workspace;
    }

    private static WorkbenchProject CreateDefaultWorkspace()
    {
        CardDocument welcomeCard = new() { Name = "Release approval" };
        return new WorkbenchProject
        {
            Pages = [welcomeCard],
            ActivePageId = welcomeCard.Id
        };
    }
}
