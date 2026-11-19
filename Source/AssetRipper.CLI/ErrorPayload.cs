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

internal sealed class ExportPayload
{
	public required string Status { get; init; }
	public required string Output { get; init; }
	public required string Project { get; init; }
	public IReadOnlyList<string>? Files { get; init; }
	public bool? Existing { get; init; }
}

internal sealed class ExportManifest
{
	public required string Kind { get; init; }
	public required IReadOnlyList<string> Inputs { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(VersionPayload))]
[JsonSerializable(typeof(ExportPayload))]
[JsonSerializable(typeof(ExportManifest))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
