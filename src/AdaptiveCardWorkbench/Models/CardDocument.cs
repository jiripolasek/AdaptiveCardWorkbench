namespace AdaptiveCardWorkbench.Models;

public sealed class CardDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled card";

    public string PayloadJson { get; set; } = Samples.DefaultPayload;

    public string DataJson { get; set; } = Samples.DefaultData;

    public Guid? ProjectId { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset LastAccessed { get; set; } = DateTimeOffset.Now;
}
