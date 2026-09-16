namespace AdaptiveCardWorkbench.ViewModels;

public sealed partial class ShellNavigationSeparatorItemViewModel
    : ShellNavigationItemViewModel
{
    public ShellNavigationSeparatorItemViewModel()
        : base(
            ShellNavigationItemKind.Separator,
            string.Empty,
            string.Empty)
    {
    }
}
