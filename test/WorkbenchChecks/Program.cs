using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Services;
using System.Text.Json;

const string compactJson = """{"text":"Příliš <&>","values":[true,null,1e1000],"duplicate":1,"duplicate":2}""";
string formattedJson = JsonFormatting.Format(compactJson);
Check(formattedJson.Contains('\n') && formattedJson.Contains("  \"text\""),
    "Formatting adds two-space indentation");
Check(formattedJson.Contains("Příliš <&>") && formattedJson.Contains("1e1000"),
    "Formatting preserves Unicode and large numeric values");
using (JsonDocument originalJson = JsonDocument.Parse(compactJson))
using (JsonDocument formattedDocument = JsonDocument.Parse(formattedJson))
{
    Check(JsonElement.DeepEquals(originalJson.RootElement, formattedDocument.RootElement),
        "Formatting preserves JSON values and duplicate properties");
}
Check(JsonFormatting.Format(formattedJson) == formattedJson, "Formatting twice is a no-op");
await ExpectFailure<JsonException>(() => Task.FromResult(JsonFormatting.Format("{broken")));
Check(JsonFormatting.Format("null") == "null", "Formatting accepts valid scalar JSON");

string folder = Path.Combine(Path.GetTempPath(), "WorkbenchChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    DraftStorageService storage = new(folder);
    string draftPath = Path.Combine(folder, "draft-project.json");
    CardDocument original = new() { Name = "Original card" };
    WorkbenchProject workspace = new() { Pages = [original], ActivePageId = original.Id };
    await storage.SaveAsync(workspace);
    string savedJson = await File.ReadAllTextAsync(draftPath);

    workspace.Pages.Add(new CardDocument { Name = "New card" });
    using (FileStream locked = new(draftPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
    {
        await ExpectFailure<IOException>(() => storage.SaveAsync(workspace));
    }
    Check(await File.ReadAllTextAsync(draftPath) == savedJson, "A failed temporary write preserves the saved draft");

    using (FileStream locked = new(draftPath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        await ExpectFailure<UnauthorizedAccessException>(() => storage.SaveAsync(workspace));
    }
    Check(await File.ReadAllTextAsync(draftPath) == savedJson, "A failed replacement preserves the saved draft");

    await storage.SaveAsync(workspace);
    Check((await storage.LoadAsync()).Pages.Count == 2, "Saving succeeds after a failed attempt");
    Check(!File.Exists(draftPath + ".tmp"), "A successful save leaves no temporary file");

    using (FileStream locked = new(draftPath, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        WorkspaceService failedWorkspace = new(storage);
        await ExpectFailure<IOException>(() => failedWorkspace.InitializeAsync());
        await failedWorkspace.SaveAsync();
        Check(failedWorkspace.Project is null, "A read failure does not create a replacement workspace");
    }
    Check((await storage.LoadAsync()).Pages.Count == 2, "A read failure preserves the existing cards");

    foreach (string invalidJson in new[] { "{broken", "null" })
    {
        await File.WriteAllTextAsync(draftPath, invalidJson);
        WorkspaceService failedWorkspace = new(storage);
        await ExpectFailure<JsonException>(() => failedWorkspace.InitializeAsync());
        await failedWorkspace.SaveAsync();
        Check(await File.ReadAllTextAsync(draftPath) == invalidJson, "Invalid JSON is preserved after failed initialization and save");
    }

    File.Delete(draftPath);
    Check((await storage.LoadAsync()).Pages.Count == 1, "A missing draft still creates the initial sample");

    WorkspaceService emptyWorkspace = new(storage);
    await emptyWorkspace.InitializeAsync();
    foreach (CardDocument document in emptyWorkspace.VisibleDocuments)
    {
        emptyWorkspace.DeleteDocument(document.Id);
    }
    await emptyWorkspace.SaveAsync();
    WorkspaceService reopenedWorkspace = new(storage);
    await reopenedWorkspace.InitializeAsync();
    Check(reopenedWorkspace.Project!.Pages.Count == 0 && reopenedWorkspace.ActiveDocument is null,
        "Deleting the last card and reopening preserves an empty workspace");
}
finally
{
    foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
    Directory.Delete(folder);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

static async Task ExpectFailure<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
