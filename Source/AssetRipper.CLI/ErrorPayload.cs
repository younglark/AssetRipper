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
	public string? Id { get; set; }
	public string? Hint { get; set; }
	public required string Status { get; init; }
	public string? Output { get; init; }
	public string? Project { get; init; }
	public IReadOnlyList<string>? Files { get; init; }
	public bool? Existing { get; init; }
}

internal sealed class ExportManifest
{
	public required string Kind { get; init; }
	public required IReadOnlyList<string> Inputs { get; init; }
	public string? SettingsFingerprint { get; init; }
}

internal sealed class InspectItem
{
	public required string Name { get; init; }
	public string? Type { get; init; }
	public string? Ref { get; init; }
}

internal sealed class ListPayload
{
	public required IReadOnlyList<InspectItem> Items { get; init; }
	public bool? Truncated { get; init; }
	public string? Next { get; init; }
	public string? Hint { get; init; }
}

internal sealed class GetAssetPayload
{
	public required string Path { get; init; }
	public required string Format { get; init; }
	public required long Size { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionPayload))]
[JsonSerializable(typeof(SessionRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(ErrorPayload))]
[JsonSerializable(typeof(VersionPayload))]
[JsonSerializable(typeof(ExportPayload))]
[JsonSerializable(typeof(ExportManifest))]
[JsonSerializable(typeof(ListPayload))]
[JsonSerializable(typeof(GetAssetPayload))]
internal partial class CliJsonContext : JsonSerializerContext
{
}
