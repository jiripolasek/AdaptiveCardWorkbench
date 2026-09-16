namespace AdaptiveCardWorkbench.Models;

public sealed class CardProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled project";

    public CardSortMode SortMode { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.Now;
}
