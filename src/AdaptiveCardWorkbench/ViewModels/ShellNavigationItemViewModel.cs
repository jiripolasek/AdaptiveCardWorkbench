using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace AdaptiveCardWorkbench.ViewModels;

[WinRT.GeneratedBindableCustomProperty]
public partial class ShellNavigationItemViewModel : ObservableObject
{
    public ShellNavigationItemViewModel(
        ShellNavigationItemKind kind,
        string title,
        string iconGlyph,
        Guid? projectId = null,
        Guid? documentId = null)
    {
        Kind = kind;
        Title = title;
        IconGlyph = iconGlyph;
        ProjectId = projectId;
        DocumentId = documentId;
        IsExpanded = kind == ShellNavigationItemKind.Project;
    }

    public ShellNavigationItemKind Kind { get; }

    public string IconGlyph { get; }

    public Guid? ProjectId { get; }

    public Guid? DocumentId { get; }

    public bool IsEnabled => Kind != ShellNavigationItemKind.EmptySearchResult;

    public bool SelectsOnInvoked => Kind is
        ShellNavigationItemKind.Document or
        ShellNavigationItemKind.Archive or
        ShellNavigationItemKind.Resources;

    public string ToolTip => Title;

    public string HelpText => Kind switch
    {
        ShellNavigationItemKind.Project when ProjectId is Guid =>
            "Project group. Cards are listed beneath it. Use the context menu to rename, sort, archive, or delete the project.",
        ShellNavigationItemKind.Project =>
            "General card group. Cards without a project are listed beneath it. Use the context menu to sort cards.",
        ShellNavigationItemKind.Document =>
            "Open card. Use the context menu to rename, duplicate, move, archive, or delete it.",
        _ => Title
    };

    public ObservableCollection<ShellNavigationItemViewModel> Children { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    [NotifyPropertyChangedFor(nameof(HelpText))]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
