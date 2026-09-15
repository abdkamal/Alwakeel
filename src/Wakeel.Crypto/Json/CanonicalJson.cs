using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wakeel.Crypto;

/// <summary>
/// Deterministic JSON: object members sorted by ordinal key order, no whitespace,
/// invariant number formatting, instants normalised to UTC with millisecond precision,
/// UTF-8 output. Every structure that is signed goes through here, so two machines
/// always produce the same bytes for the same object.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    /// <summary>Serializer settings shared by every signed structure in the product.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return Canonicalize(raw);
    }

    public static string Serialize<T>(T value) =>
        System.Text.Encoding.UTF8.GetString(SerializeToUtf8Bytes(value));

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(utf8Json, Options);
            if (value is null)
            {
                throw new CryptoException(ErrorCode.Corrupt, "The structure could not be read.");
            }

            return value;
        }
        catch (JsonException exception)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The structure could not be read.", exception);
        }
        catch (NotSupportedException exception)
        {
            // A document that asks for a shape the serializer cannot build is damage, not a bug.
            throw new CryptoException(ErrorCode.Corrupt, "The structure could not be read.", exception);
        }
    }

    public static T Deserialize<T>(string json) =>
        Deserialize<T>(System.Text.Encoding.UTF8.GetBytes(json ?? string.Empty));

    /// <summary>Rewrites arbitrary UTF-8 JSON into the canonical form.</summary>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8Json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json.ToArray());
        }
        catch (JsonException exception)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The structure is not valid JSON.", exception);
        }

        using (document)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            {
                Write(document.RootElement, writer);
            }

            return buffer.ToArray();
        }
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    writer.WriteNumberValue(integer);
                }
                else if (element.TryGetDecimal(out var dec))
                {
                    writer.WriteNumberValue(dec);
                }
                else
                {
                    writer.WriteNumberValue(element.GetDouble());
                }

                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NumberHandling = JsonNumberHandling.Strict,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new UtcInstantConverter());
        return options;
    }

    /// <summary>Instants are always written as UTC with millisecond precision.</summary>
    private sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (string.IsNullOrEmpty(text))
            {
                throw new JsonException("Missing instant.");
            }

            // Every parse failure has to leave through JsonException, because that is the only
            // exception the deserializer wraps; anything else would escape the error contract.
            if (!DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var value))
            {
                throw new JsonException("Unreadable instant.");
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
    }
}
