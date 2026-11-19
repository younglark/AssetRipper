using System.Text.Json.Serialization;

namespace AssetRipper.CLI;

internal sealed class ErrorPayload
{
	public required string Code { get; init; }
	public required string Hint { get; init; }
	public string? Message { get; init; }
}

internal sealed class VersionPayload
{
	public required string Version { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(VersionPayload))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
