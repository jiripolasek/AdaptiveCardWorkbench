using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace AdaptiveCardWorkbench.ViewModels;

public sealed class ArchiveGroupViewModel
{
    public ArchiveGroupViewModel(
        Guid? projectId,
        string name,
        string description,
        bool isArchivedProject,
        Func<ArchiveGroupViewModel, Task> restore,
        Func<ArchiveGroupViewModel, Task> delete)
    {
        ProjectId = projectId;
        Name = name;
        Description = description;
        IsArchivedProject = isArchivedProject;
        RestoreCommand = new AsyncRelayCommand(() => restore(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public Guid? ProjectId { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsArchivedProject { get; }

    public Visibility ProjectActionsVisibility =>
        IsArchivedProject
            ? Visibility.Visible
            : Visibility.Collapsed;

    public ObservableCollection<ArchiveEntryViewModel> Cards { get; } = [];

    public IAsyncRelayCommand RestoreCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}

public sealed class ArchiveEntryViewModel
{
    public ArchiveEntryViewModel(
        Guid id,
        string name,
        string description,
        bool showActions,
        Func<ArchiveEntryViewModel, Task> restore,
        Func<ArchiveEntryViewModel, Task> delete)
    {
        Id = id;
        Name = name;
        Description = description;
        ActionsVisibility = showActions
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestoreCommand = new AsyncRelayCommand(() => restore(this));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Description { get; }

    public Visibility ActionsVisibility { get; }

    public IAsyncRelayCommand RestoreCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}
