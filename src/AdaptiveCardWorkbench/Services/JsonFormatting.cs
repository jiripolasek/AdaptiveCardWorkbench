using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AdaptiveCardWorkbench.Services;

internal static class JsonFormatting
{
    public static string Format(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            document.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
