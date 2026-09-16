using AdaptiveCardWorkbench.ViewModels;

namespace AdaptiveCardWorkbench.Services;

public interface IArchiveDialogService
{
    Task<bool> ConfirmDeleteProjectAsync(ArchiveGroupViewModel group);

    Task<bool> ConfirmDeleteCardAsync(ArchiveEntryViewModel entry);

    Task ShowAutosaveErrorAsync(Exception exception);
}
