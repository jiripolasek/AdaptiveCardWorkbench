namespace AdaptiveCardWorkbench.Models;

public sealed class WorkbenchProject
{
    public int FormatVersion { get; set; } = 3;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "My card workspace";

    public List<CardProject> Projects { get; set; } = [];

    public CardSortMode GeneralSortMode { get; set; }

    public List<CardDocument> Pages { get; set; } = [];

    public Guid? ActivePageId { get; set; }

    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.Now;
}
