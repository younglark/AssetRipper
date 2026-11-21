namespace AssetRipper.Export.Configuration;

public enum SpriteExportMode
{
	/// <summary>
	/// Export as yaml assets which can be viewed in the editor.
	/// This is the only mode that ensures a precise recovery of all metadata of sprites.
	/// <see href="https://github.com/trouger/AssetRipper/issues/2"/>
	/// </summary>
	Yaml,
	/// <summary>
	/// Export in the native asset format, where all sprites data are stored in texture importer settings.
	/// </summary>
	/// <remarks>
	/// The output from this mode was substantially changed by
	/// <see href="https://github.com/AssetRipper/AssetRipper/commit/084b3e5ea7826ac2f54ed2b11cbfbbf3692ddc9c"/>.
	/// Using this is inadvisable.
	/// </remarks>
	Native,
	/// <summary>
	/// Export as a Texture2D png image
	/// </summary>
	/// <remarks>
	/// The output from this mode was substantially changed by
	/// <see href="https://github.com/AssetRipper/AssetRipper/commit/084b3e5ea7826ac2f54ed2b11cbfbbf3692ddc9c"/>.
	/// Using this is inadvisable.
	/// </remarks>
	Texture2D,
	/// <summary>
	/// Export as yaml Sprite assets that reference a newly unpacked individual texture for each sprite.
	/// Experimental: unsupported packing combinations fall back to the regular YAML atlas or sheet reference.
	/// </summary>
	YamlUnpacked,
	/// <summary>
	/// Export as yaml FullRect Sprite assets whose current mesh and native UVs have been
	/// offline-sampled onto the original logical canvas as an independent RGBA PNG.
	/// Experimental: unsupported sprites fall back to the regular YAML atlas or sheet reference.
	/// Does not change the semantics of <see cref="Yaml"/> or <see cref="YamlUnpacked"/>.
	/// </summary>
	YamlBaked,
}

public static class SpriteExportModeExtensions
{
	public static bool UsesYamlSpriteAssets(this SpriteExportMode mode)
	{
		return mode is SpriteExportMode.Yaml or SpriteExportMode.YamlUnpacked or SpriteExportMode.YamlBaked;
	}

	public static string ToCliValue(this SpriteExportMode mode)
	{
		return mode switch
		{
			SpriteExportMode.Yaml => "yaml",
			SpriteExportMode.YamlUnpacked => "yaml-unpacked",
			SpriteExportMode.YamlBaked => "yaml-baked",
			SpriteExportMode.Native => "native",
			SpriteExportMode.Texture2D => "texture2d",
			_ => mode.ToString().ToLowerInvariant(),
		};
	}

	public static bool TryParseCliValue(string? value, out SpriteExportMode mode)
	{
		switch (value?.Trim().ToLowerInvariant())
		{
			case "yaml":
				mode = SpriteExportMode.Yaml;
				return true;
			case "yaml-unpacked":
				mode = SpriteExportMode.YamlUnpacked;
				return true;
			case "yaml-baked":
				mode = SpriteExportMode.YamlBaked;
				return true;
			case "native":
				mode = SpriteExportMode.Native;
				return true;
			case "texture2d":
				mode = SpriteExportMode.Texture2D;
				return true;
			default:
				mode = default;
				return false;
		}
	}
}
