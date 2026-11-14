using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatalogService.api.Messaging.Envelopes;

/// <summary>
/// Læser kuvertens payload som tekst, uanset om afsenderen har sendt den som en JSON-tekst (den gamle måde) eller
/// som et JSON-objekt (checkout-planens kuvert). Et objekt gemmes som sin rå JSON-tekst, så resten af koden
/// kan deserialisere den på samme måde som før.
/// </summary>
public sealed class PayloadAsStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
