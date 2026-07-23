using System.Text.Json.Serialization;

namespace Transpiler.Core.Languages;

/// <summary>Block delimiter configuration (SPEC tier 1).</summary>
public sealed class BlockStyle
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BlockDelimiterStyle Style { get; init; } = BlockDelimiterStyle.Keyword;

    /// <summary>Opening delimiter for <see cref="BlockDelimiterStyle.Braces"/>.</summary>
    public string Open { get; init; } = "{";

    /// <summary>Closing delimiter for <see cref="BlockDelimiterStyle.Braces"/>.</summary>
    public string Close { get; init; } = "}";
}
