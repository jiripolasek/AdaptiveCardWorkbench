using AdaptiveCardWorkbench.Models;
using System.Text.Json.Serialization;

namespace AdaptiveCardWorkbench.Services;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WorkbenchProject))]
internal sealed partial class WorkbenchJsonSerializerContext : JsonSerializerContext;
