using AdaptiveCardWorkbench.Models;
using AdaptiveCardWorkbench.Services;
using System.Text;
using System.Text.Json;

Check(AdaptiveCardCompletion.DetectVersion("{}") == (new Version(1, 6), true)
      && AdaptiveCardCompletion.DetectVersion("""{"version":"1.5"}""") == (new Version(1, 5), false)
      && AdaptiveCardCompletion.DetectVersion("""{"$schema":"https://adaptivecards.io/schemas/1.2.0/adaptive-card.json"}""") == (new Version(1, 2), false)
      && AdaptiveCardCompletion.DetectVersion("""{"version":"2.0"}""").Version is null,
    "Version detection distinguishes declared, default, and unavailable schemas");

Check(Complete("""{"|""")!.Items.Contains("body"), "Root completion offers Adaptive Card properties");
Check(Complete("""{"type":"AdaptiveCard","|""")!.Items.Contains("version"), "Root type preserves card properties");
Check(!Complete("""{"type":"AdaptiveCard","|""")!.Items.Contains("type"), "Completion omits preceding properties");
Check(Complete("""{"body":[{"type":"TextBlock","|""")!.Items.Contains("text"), "Element type selects its properties");
Check(!Complete("""{"body":[{"type":"TextBlock","|""")!.Items.Contains("url"), "TextBlock does not suggest Image properties");
Check(Complete("""{"body":[{"type":"TextBlock","|""")!.Items.Contains("spacing"), "Completion includes inherited properties");
Check(Complete("""{"body":[{"type":"TextBlock","size":"|""")!.Items.Contains("extraLarge"), "Completion resolves enum references");
Check(Complete("""{"body":[{"type":"TextBlock","wrap":t|""")!.Items.SequenceEqual(["true"]), "Completion suggests booleans");
Check(Complete("""{"body":[{"type":"|""")!.Items.Contains("TextBlock"), "Body suggests element types");
Check(!Complete("""{"body":[{"type":"|""")!.Items.Contains("Action.Submit"), "Body does not suggest action types");
Check(Complete("""{"actions":[{"type":"Action.|""")!.Items.Contains("Action.Submit"), "Actions suggest action types");
Check(Complete("""{"body":[{"type":"FactSet","facts":[{"|""")!.Items.Contains("title"), "Untyped child objects use their parent schema");
Check(Complete("""{"body":[{"type":"TextBlock","text":"Hello|""") is null, "Free text does not offer schema words");
Check(Complete("""{"body":[{"type":"TextBlock","size":"${si|""") is null, "Template expressions are left alone");
Check(Complete("""{"actions":[{"type":"Action.Submit","data":{"|""") is null, "Arbitrary data objects do not get card properties");
Check(Complete("""{"broken":!,"|""") is null, "Malformed JSON before the caret is ignored safely");
Check(ApplyCompletion("""{"body":[{"type":"TextBlock","text":"Příliš 😀","si|ze":"large"}]}""", "size")
    == """{"body":[{"type":"TextBlock","text":"Příliš 😀","size":"large"}]}""", "Completion replaces a token using UTF-8 positions without duplicating quotes or colons");
Check(ApplyCompletion("""{"body":[{"type":"TextBlock","size":"ex|traLarge"}]}""", "extraLarge")
    == """{"body":[{"type":"TextBlock","size":"extraLarge"}]}""", "Enum completion replaces the suffix of an existing value");
Check(ApplyCompletion("""{"bo|""", "body") == "{\"body\": ", "A new property gets its closing quote and colon");
Check(Complete("{|}")!.Items.Contains("\"body\""), "Explicit completion works before an opening quote");
Check(ApplyCompletion("""{"body":[{"type":"TextBlock","size":"sm|áll\"text"}]}""", "small")
    == """{"body":[{"type":"TextBlock","size":"small"}]}""", "Completion replaces Unicode and escaped quotes in a string suffix");

for (int minor = 0; minor <= 6; minor++)
{
    string card = "{\"type\":\"AdaptiveCard\",\"version\":\"1." + minor + "\",";
    var root = Complete(card + "\"|")!.Items;
    var text = Complete(card + "\"body\":[{\"type\":\"TextBlock\",\"|")!.Items;
    var inputs = Complete(card + "\"body\":[{\"type\":\"Input.Text\",\"|")!.Items;
    var types = Complete(card + "\"body\":[{\"type\":\"|")!.Items;
    var actions = Complete(card + "\"actions\":[{\"type\":\"|")!.Items;
    Check(root.Contains("body") && root.Contains("backgroundImage") && text.Contains("text") && text.Contains("spacing"),
        $"Schema 1.{minor} keeps root and inherited element properties");
    Check(root.Contains("refresh") == (minor >= 4) && root.Contains("metadata") == (minor >= 6)
          && inputs.Contains("label") == (minor >= 3) && actions.Contains("Action.Execute") == (minor >= 4)
          && types.Contains("Table") == (minor >= 5), $"Schema 1.{minor} limits properties and types to its version");
    Check(actions.Contains("Action.OpenUrl") && actions.Contains("Action.ShowCard") && actions.Contains("Action.Submit"),
        $"Schema 1.{minor} suggests the standard actions");
}

Check(!Complete("""{"version":"1.4","body":[{"type":"Input.ChoiceSet","style":"|""")!.Items.Contains("filtered")
      && Complete("""{"version":"1.5","body":[{"type":"Input.ChoiceSet","style":"|""")!.Items.Contains("filtered"),
    "Enum values come from the selected schema version");
Check(Complete("""{"$schema":"https://adaptivecards.io/schemas/adaptive-card.json","version":"1.3","bo|""")!.Items.Contains("body"),
    "The canonical schema URL uses the declared card version");
Check(!Complete("""{"$schema":"http://adaptivecards.io/schemas/1.2.0/adaptive-card.json","|""")!.Items.Contains("refresh"),
    "A versioned schema URL selects its version without a card version");
Check(!Complete("""{"$schema":"https://adaptivecards.io/schemas/1.2.1/adaptive-card.json","version":"1.5","|""")!.Items.Contains("refresh"),
    "A versioned schema also bounds a newer card version");
Check(Complete("""{"$schema":"https://json.schemastore.org/package.json","|""") is null
      && Complete("""{"$schema":"https://example.com/schemas/adaptive-card.json","|""") is null,
    "Unrelated schema declarations do not offer Adaptive Card suggestions");
Check(Complete("""{"version":"2.0","|""") is null
      && Complete("""{"version":"invalid","|""") is null
      && Complete("""{"$schema":"https://adaptivecards.io/schemas/2.0.0/adaptive-card.json","version":"1.5","|""") is null,
    "Unknown versions do not silently use a different schema");
Check(!Complete("""{"|":null,"version":"1.3"}""")!.Items.Contains("refresh"),
    "A version after the caret still selects the schema");
Check(Complete("""{"|":null,"$schema":"https://example.com/other.json"}""") is null
      && Complete("""{"|":null,"type":"Unrelated"}""") is null,
    "Root schema and type declarations after the caret are respected");
var reorderedText = Complete("""{"body":[{"|":null,"type":"TextBlock"}]}""")!.Items;
Check(reorderedText.Contains("text") && !reorderedText.Contains("url"), "A type after the caret selects the object's properties");
Check(Complete("""{"body":[{"size":"|","type":"TextBlock"}]}""")!.Items.Contains("extraLarge"),
    "A type after the caret also selects enum values");
Check(Complete("""{"body":[{"type":"Input.|Text"}]}""")!.Items.Contains("Input.Number"),
    "Editing an existing type offers other permitted types");
Check(Complete("""{"version":"1.2","actions":[{"type":"Action.ShowCard","card":{"type":"AdaptiveCard","|""")!.Items.Contains("body")
      && !Complete("""{"version":"1.2","actions":[{"type":"Action.ShowCard","card":{"type":"AdaptiveCard","|""")!.Items.Contains("refresh"),
    "A nested ShowCard inherits the root version");
Check(Complete("""{"version":"1.2","actions":[{"type":"Action.Submit","data":{"version":"1.6"}}],"|""")!.Items.Contains("body")
      && !Complete("""{"version":"1.2","actions":[{"type":"Action.Submit","data":{"version":"1.6"}}],"|""")!.Items.Contains("refresh"),
    "Nested data does not override the root version");

static AdaptiveCardCompletion.Completion? Complete(string markedJson)
{
    int caret = markedJson.IndexOf('|');
    return AdaptiveCardCompletion.Get(markedJson.Remove(caret, 1), Encoding.UTF8.GetByteCount(markedJson.AsSpan(0, caret)));
}

static string ApplyCompletion(string markedJson, string item)
{
    AdaptiveCardCompletion.Completion completion = Complete(markedJson)!;
    byte[] bytes = Encoding.UTF8.GetBytes(markedJson.Replace("|", ""));
    return Encoding.UTF8.GetString(bytes.AsSpan(0, completion.Start)) + item + completion.Suffix
        + Encoding.UTF8.GetString(bytes.AsSpan(completion.End));
}

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
