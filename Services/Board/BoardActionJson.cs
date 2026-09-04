using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiTeacher.Services.Board;

// Single source of truth for (de)serializing BoardAction / BoardScript JSON.
// Matches the golden-lesson fixture format: camelCase properties, string
// regions ("WorkArea"), snake_case "type" discriminators.
public static class BoardActionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // The model won't reliably put "type" first in each object.
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
