using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using MessagesModel = Anthropic.Models.Messages.Model;
using HomeInventory.Client;

namespace HomeInventory;

/// <summary>What the assistant read from a receipt, plus a plain-text transcription kept for search.</summary>
public sealed record ReceiptReading(ReceiptReadDto Receipt, string Transcription);

/// <summary>AI helpers (receipt reading, identifying items from photos). Behind an interface so tests never call the API.</summary>
public interface IInventoryAssistant
{
    bool IsConfigured { get; }
    Task<ReceiptReading> ReadReceiptAsync(byte[] content, string mediaType, CancellationToken cancellationToken);
    Task<AssetSuggestionDto> IdentifyItemAsync(byte[] image, string currency, CancellationToken cancellationToken);
}

public sealed class AssistantException(string message) : Exception(message);

/// <summary>
/// Calls Claude through the official Anthropic SDK. The API key comes from configuration (<c>Anthropic:ApiKey</c>,
/// e.g. user secrets) or the <c>ANTHROPIC_API_KEY</c> environment variable; without one the AI features stay off.
/// </summary>
public sealed class ClaudeInventoryAssistant(IConfiguration configuration) : IInventoryAssistant
{
    const string ModelId = "claude-opus-5-5";
    readonly string? apiKey = configuration["Anthropic:ApiKey"] is { Length: > 0 } configured ? configured : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    AnthropicClient? client;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(apiKey);

    AnthropicClient Client => client ??= new AnthropicClient { ApiKey = apiKey };

    public async Task<ReceiptReading> ReadReceiptAsync(byte[] content, string mediaType, CancellationToken cancellationToken)
    {
        var json = await AskAsync(FileBlock(content, mediaType), """
            This is a receipt, invoice or warranty document for something in a home inventory.
            Extract the merchant, the purchase date (as YYYY-MM-DD), the total amount paid, the currency as a three-letter ISO code,
            the line items, and the warranty length in months if the document states one. Use null for anything the document doesn't show.
            "summary" is one short sentence describing the purchase. "transcription" is all the readable text, in reading order.
            """, ReceiptSchema, cancellationToken);
        var receipt = new ReceiptReadDto(
            String(json, "merchant"),
            DateOnly.TryParseExact(String(json, "purchaseDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            Number(json, "total"),
            String(json, "currency")?.ToUpperInvariant(),
            json.GetProperty("items").EnumerateArray().Select(i => new ReceiptLineDto(String(i, "description") ?? "", Number(i, "quantity"), Number(i, "price"))).ToList(),
            json.GetProperty("warrantyMonths").ValueKind == JsonValueKind.Number ? json.GetProperty("warrantyMonths").GetInt32() : null,
            String(json, "summary") ?? "");
        return new ReceiptReading(receipt, String(json, "transcription") ?? "");
    }

    public async Task<AssetSuggestionDto> IdentifyItemAsync(byte[] image, string currency, CancellationToken cancellationToken)
    {
        var json = await AskAsync(FileBlock(image, "image/jpeg"), $"""
            This photo shows an item someone wants to add to their home inventory.
            Identify it: a short name, a broad category (e.g. Electronics, Furniture, Appliances, Tools, Kitchen, Jewellery, Garden),
            the brand and model if they are visible or clearly recognisable, and a one-sentence description.
            Estimate its current second-hand value in {currency} as a single number, or null if you can't reasonably estimate it.
            "confidence" says how sure you are of the identification.
            """, ItemSchema, cancellationToken);
        return new AssetSuggestionDto(String(json, "name") ?? "Unknown item", String(json, "category") ?? "General", String(json, "brand"), String(json, "model"),
            String(json, "description"), Number(json, "estimatedValue"), String(json, "confidence") ?? "low");
    }

    async Task<JsonElement> AskAsync(BetaContentBlockParam file, string instructions, Dictionary<string, JsonElement> schema, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new AssistantException("AI features are off: no Anthropic API key is configured.");
        var response = await Client.Beta.Messages.Create(new MessageCreateParams
        {
            Model = ModelId,
            MaxTokens = 8192,
            // Re-serve the rare policy refusal on a fallback model inside the same call.
            Betas = [AnthropicBeta.ServerSideFallback2026_06_01],
            Fallbacks = new List<BetaFallbackParam> { new() { Model = MessagesModel.ClaudeOpus4_8 } },
            OutputConfig = new BetaOutputConfig { Effort = Effort.Low, Format = new BetaJsonOutputFormat { Schema = schema } },
            Messages = [new BetaMessageParam { Role = Role.User, Content = new List<BetaContentBlockParam> { file, new BetaTextBlockParam { Text = instructions } } }],
        }, cancellationToken);
        if (response.StopReason == "refusal") throw new AssistantException("Claude declined to read this file.");
        if (response.StopReason == "max_tokens") throw new AssistantException("The document was too long to read in one go.");
        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new AssistantException("Claude's answer could not be read.");
        }
    }

    static BetaContentBlockParam FileBlock(byte[] content, string mediaType) => mediaType == "application/pdf"
        ? new BetaRequestDocumentBlock { Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(content) } }
        : new BetaImageBlockParam { Source = new BetaBase64ImageSource { Data = Convert.ToBase64String(content), MediaType = MediaType.ImageJpeg } };

    static string? String(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    static decimal? Number(JsonElement json, string name) => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    static JsonElement Nullable(string type) => JsonSerializer.SerializeToElement(new { anyOf = new object[] { new { type }, new { type = "null" } } });

    static Dictionary<string, JsonElement> Object(Dictionary<string, JsonElement> properties) => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(properties),
        ["required"] = JsonSerializer.SerializeToElement(properties.Keys),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };

    static readonly Dictionary<string, JsonElement> ReceiptSchema = Object(new()
    {
        ["merchant"] = Nullable("string"),
        ["purchaseDate"] = Nullable("string"),
        ["total"] = Nullable("number"),
        ["currency"] = Nullable("string"),
        ["items"] = JsonSerializer.SerializeToElement(new
        {
            type = "array",
            items = Object(new()
            {
                ["description"] = JsonSerializer.SerializeToElement(new { type = "string" }),
                ["quantity"] = Nullable("number"),
                ["price"] = Nullable("number"),
            }),
        }),
        ["warrantyMonths"] = Nullable("integer"),
        ["summary"] = JsonSerializer.SerializeToElement(new { type = "string" }),
        ["transcription"] = JsonSerializer.SerializeToElement(new { type = "string" }),
    });

    static readonly Dictionary<string, JsonElement> ItemSchema = Object(new()
    {
        ["name"] = JsonSerializer.SerializeToElement(new { type = "string" }),
        ["category"] = JsonSerializer.SerializeToElement(new { type = "string" }),
        ["brand"] = Nullable("string"),
        ["model"] = Nullable("string"),
        ["description"] = Nullable("string"),
        ["estimatedValue"] = Nullable("number"),
        ["confidence"] = JsonSerializer.SerializeToElement(new { type = "string", @enum = new[] { "high", "medium", "low" } }),
    });
}
