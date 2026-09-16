using AdaptiveCardWorkbench.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AdaptiveCardWorkbench.Selectors;

public sealed partial class ShellNavigationItemTemplateSelector
    : DataTemplateSelector
{
    public DataTemplate? ItemTemplate { get; set; }

    public DataTemplate? SeparatorTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
    {
        return item is ShellNavigationSeparatorItemViewModel
            ? SeparatorTemplate
            : ItemTemplate;
    }
}
