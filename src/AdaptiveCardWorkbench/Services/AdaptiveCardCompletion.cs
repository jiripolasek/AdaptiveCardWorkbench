using System.Text;
using System.Text.Json;

namespace AdaptiveCardWorkbench.Services;

internal static class AdaptiveCardCompletion
{
    private static readonly Dictionary<Version, JsonElement> Schemas = Enumerable.Range(0, 7)
        .ToDictionary(minor => new Version(1, minor), minor => LoadSchema(minor));

    public static Completion? Get(string json, int position)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (position < 0 || position > bytes.Length)
        {
            return null;
        }

        var (schemaUri, cardVersion, types) = ReadDeclarations(bytes, position);
        var version = SelectVersion(schemaUri, cardVersion, out _);
        if (version is null || !Schemas.TryGetValue(version, out var schema))
        {
            return null;
        }

        Stack<Scope> scopes = new();

        Utf8JsonReader reader = new(bytes.AsSpan(0, position), false, default);
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        var schemas = scopes.Count == 0
                            ? [schema]
                            : ValueSchemas(scopes.Peek(), schema).ToArray();
                        if (types.TryGetValue(reader.TokenStartIndex, out var type))
                        {
                            if (scopes.Count == 0)
                            {
                                if (type != "AdaptiveCard")
                                {
                                    return null;
                                }
                            }
                            else
                            {
                                schemas = schemas.SelectMany(s => Expand(s, schema))
                                    .Where(s => s.TryGetProperty("properties", out var p)
                                                && p.TryGetProperty("type", out var t)
                                                && t.TryGetProperty("enum", out var values)
                                                && values.EnumerateArray().Any(v =>
                                                    v.ValueKind == JsonValueKind.String && v.GetString() == type))
                                    .ToArray();
                            }
                        }

                        scopes.Push(new Scope(schemas, reader.TokenType == JsonTokenType.StartObject));
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        scopes.Pop();
                        if (scopes.Count > 0)
                        {
                            scopes.Peek().Property = null;
                        }

                        break;

                    case JsonTokenType.PropertyName:
                        scopes.Peek().Property = reader.GetString();
                        scopes.Peek().Properties.Add(reader.GetString()!);
                        break;

                    default:
                        if (scopes.Count == 0)
                        {
                            return null;
                        }

                        scopes.Peek().Property = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (scopes.Count == 0 || !scopes.Peek().IsObject)
        {
            return null;
        }

        var current = scopes.Peek();
        var start = (int)reader.BytesConsumed;
        while (start < position && (char.IsWhiteSpace((char)bytes[start]) || bytes[start] == ','))
        {
            start++;
        }

        var quoted = start < position && bytes[start] == '"';
        if (quoted)
        {
            start++;
        }

        var prefix = Encoding.UTF8.GetString(bytes, start, position - start);
        if (prefix.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '$' and not '-'))
        {
            return null;
        }

        if (current.Property is null && !quoted && reader.TokenType != JsonTokenType.StartObject &&
            !bytes.AsSpan((int)reader.BytesConsumed, position - (int)reader.BytesConsumed).Contains((byte)','))
        {
            return null;
        }

        var property = current.Property is null;
        var items = property
            ? Properties(current, schema).Select(p => p.Name).Where(name => !current.Properties.Contains(name))
            : ValueSchemas(current, schema).SelectMany(s => Expand(s, schema)).SelectMany(s => Values(s, quoted));
        var matches = items.Distinct().Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var end = position;
        while (end < bytes.Length && (char.IsAsciiLetterOrDigit((char)bytes[end]) ||
                                      bytes[end] is (byte)'.' or (byte)'_' or (byte)'$' or (byte)'-'))
        {
            end++;
        }

        var suffix = string.Empty;
        if (quoted)
        {
            // A complete string can contain escaped quotes or non-ASCII text after the caret.
            Utf8JsonReader token = new(bytes.AsSpan(start - 1), false, default);
            try
            {
                if (token.Read())
                {
                    end = start - 1 + (int)token.BytesConsumed;
                }
            }
            catch (JsonException)
            {
                // While typing an unfinished string, replace only the current word.
            }

            suffix = "\"";
        }
        else if (property)
        {
            matches = matches.Select(name => $"\"{name}\"").ToArray();
        }

        if (property)
        {
            var next = end;
            while (next < bytes.Length && char.IsWhiteSpace((char)bytes[next]))
            {
                next++;
            }

            if (next == bytes.Length || bytes[next] != ':')
            {
                suffix += ": ";
            }
        }

        return new Completion(start, end, suffix, matches);
    }

    public static (Version? Version, bool IsDefault) DetectVersion(string json)
    {
        var (schemaUri, cardVersion, _) = ReadDeclarations(Encoding.UTF8.GetBytes(json), -1);
        var version = SelectVersion(schemaUri, cardVersion, out var isDefault);
        return (version, isDefault);
    }

    private static Version? SelectVersion(string? schemaUri, string? cardVersion, out bool isDefault)
    {
        isDefault = cardVersion is null;
        var version = new Version(1, 6);
        if (schemaUri is not null)
        {
            if (!Uri.TryCreate(schemaUri, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || uri.Host is not ("adaptivecards.io" or "www.adaptivecards.io"))
            {
                return null;
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length is not (2 or 3) || segments[0] != "schemas" || segments[^1] != "adaptive-card.json")
            {
                return null;
            }

            if (segments.Length == 3)
            {
                if (!Version.TryParse(segments[1], out var declaredVersion))
                {
                    return null;
                }

                isDefault = false;
                version = new Version(declaredVersion.Major, declaredVersion.Minor);
                if (!Schemas.ContainsKey(version))
                {
                    return null;
                }
            }
        }

        if (cardVersion is not null)
        {
            if (!Version.TryParse(cardVersion, out var declaredVersion))
            {
                return null;
            }

            var target = new Version(declaredVersion.Major, declaredVersion.Minor);
            if (!Schemas.ContainsKey(target))
            {
                return null;
            }

            // Keep suggestions within both the schema URL and the card's target version.
            version = target < version ? target : version;
        }

        return version;
    }

    private static (string? Schema, string? Version, Dictionary<long, string> Types) ReadDeclarations(byte[] bytes, int position)
    {
        string? schema = null;
        string? version = null;
        string? property = null;
        Stack<long> objects = new();
        Dictionary<long, string> types = [];
        Utf8JsonReader reader = new(bytes, false, default);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    property = reader.GetString();
                    continue;
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    objects.Push(reader.TokenStartIndex);
                }
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    objects.Pop();
                }
                else if (reader.TokenType == JsonTokenType.String
                         && !(reader.TokenStartIndex < position && position < reader.BytesConsumed))
                {
                    if (property == "type" && objects.Count > 0)
                    {
                        types[objects.Peek()] = reader.GetString()!;
                    }
                    else if (reader.CurrentDepth == 1 && property == "$schema")
                    {
                        schema = reader.GetString();
                    }
                    else if (reader.CurrentDepth == 1 && property == "version")
                    {
                        version = reader.GetString();
                    }
                }

                property = null;
            }
        }
        catch (JsonException)
        {
            // ponytail: use declarations before malformed JSON; full error recovery needs a tolerant parser.
        }

        return (schema, version, types);
    }

    private static IEnumerable<JsonProperty> Properties(Scope scope, JsonElement schema)
    {
        return scope.Schemas.SelectMany(s => Expand(s, schema))
            .Where(s => s.TryGetProperty("properties", out _))
            .SelectMany(s => s.GetProperty("properties").EnumerateObject());
    }

    private static IEnumerable<JsonElement> ValueSchemas(Scope scope, JsonElement schema)
    {
        return scope.IsObject
            ? Properties(scope, schema).Where(p => p.Name == scope.Property).Select(p => p.Value)
            : scope.Schemas.SelectMany(s => Expand(s, schema)).Where(s => s.TryGetProperty("items", out _))
                .Select(s => s.GetProperty("items"));
    }

    private static IEnumerable<string> Values(JsonElement schema, bool quoted)
    {
        if (schema.TryGetProperty("enum", out var values))
        {
            foreach (var value in values.EnumerateArray())
            {
                if (!quoted || value.ValueKind == JsonValueKind.String)
                {
                    yield return quoted ? value.GetString()! : value.GetRawText();
                }
            }
        }

        if (!quoted && schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "boolean")
        {
            yield return "true";
            yield return "false";
        }
    }

    private static IEnumerable<JsonElement> Expand(JsonElement schema, JsonElement root)
    {
        if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var resolved in schema.EnumerateArray().SelectMany(s => Expand(s, root)))
            {
                yield return resolved;
            }

            yield break;
        }

        yield return schema;

        if (schema.TryGetProperty("$ref", out var reference))
        {
            var definitions = Expand(root.GetProperty("definitions")
                .GetProperty(reference.GetString()!["#/definitions/".Length..]), root);

            foreach (var resolved in definitions)
            {
                yield return resolved;
            }
        }

        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
        {
            if (!schema.TryGetProperty(keyword, out var alternatives))
            {
                continue;
            }

            foreach (var resolved in alternatives.EnumerateArray().SelectMany(s => Expand(s, root)))
            {
                yield return resolved;
            }
        }
    }

    private static JsonElement LoadSchema(int minor)
    {
        var resource = minor == 6 ? "adaptive-card.json" : $"Schemas.1.{minor}.json";
        using var stream = typeof(AdaptiveCardCompletion).Assembly.GetManifestResourceStream($"AdaptiveCardWorkbench.{resource}")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    internal sealed record Completion(int Start, int End, string Suffix, string[] Items);

    private sealed class Scope(JsonElement[] schemas, bool isObject)
    {
        public readonly bool IsObject = isObject;
        public readonly HashSet<string> Properties = [];
        public string? Property;
        public JsonElement[] Schemas = schemas;
    }
}
