using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Serializable;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>
/// Resolves the bake color space from a game's PlayerSettings.
/// Search starts at <see cref="Bundle.GetRoot"/> so a Sprite in a child AssetBundle
/// can still see PlayerSettings in <c>globalgamemanagers</c> on the GameBundle.
/// Missing, conflicting, or unknown values are not guessed: the exporter uses
/// <see cref="SpriteOfflineColorSpace.GammaUnorm"/> and records a diagnostic.
/// Do not call <see cref="GameBundle.FetchAssetsInHierarchy"/> (obsolete-error);
/// use <c>bundle.GetRoot().FetchAssets()</c>.
/// </summary>
internal static class SpriteBakeColorSpaceResolver
{
	public static SpriteOfflineColorSpace Resolve(Bundle? bundle, out string diagnostic)
	{
		Bundle? root = bundle?.GetRoot();
		if (root is not null)
		{
			(ColorSpaceReadStatus status, SpriteOfflineColorSpace space, string message) = ReadFromLoadedAssets(root);
			switch (status)
			{
				case ColorSpaceReadStatus.Unique:
					diagnostic = message;
					return space;
				case ColorSpaceReadStatus.Conflict:
				case ColorSpaceReadStatus.Indeterminate:
					diagnostic = message;
					Logger.Warning(LogCategory.Export, diagnostic);
					return SpriteOfflineColorSpace.GammaUnorm;
			}
		}

		diagnostic = "PlayerSettings m_ActiveColorSpace was not found; baking in GammaUnorm (explicit default). This is not a claim that the source project is Gamma.";
		Logger.Warning(LogCategory.Export, diagnostic);
		return SpriteOfflineColorSpace.GammaUnorm;
	}

	internal static bool TryInterpret(int value, out SpriteOfflineColorSpace space)
	{
		switch (value)
		{
			case 0:
				space = SpriteOfflineColorSpace.GammaUnorm;
				return true;
			case 1:
				space = SpriteOfflineColorSpace.LinearSrgb;
				return true;
			default:
				space = default;
				return false;
		}
	}

	internal static bool TryReadFromFields(SerializableStructure? fields, out SpriteOfflineColorSpace space)
	{
		return ReadFields(fields, out space) == FieldRead.Known;
	}

	private static (ColorSpaceReadStatus Status, SpriteOfflineColorSpace Space, string Diagnostic) ReadFromLoadedAssets(Bundle root)
	{
		List<PlayerSettingsRead> reads = [];
		foreach (IUnityObjectBase asset in root.FetchAssets())
		{
			if (asset.ClassID != 129)
			{
				continue;
			}

			reads.Add(ReadPlayerSettings(asset));
		}

		if (reads.Count == 0)
		{
			return (ColorSpaceReadStatus.Missing, default, "");
		}

		if (reads.Exists(static read => read.Status is PlayerSettingsStatus.Unknown or PlayerSettingsStatus.Unreadable))
		{
			return (ColorSpaceReadStatus.Indeterminate, SpriteOfflineColorSpace.GammaUnorm,
				"PlayerSettings m_ActiveColorSpace could not be determined (unknown or unreadable value); baking in GammaUnorm (explicit default). This is not a claim that the source project is Gamma.");
		}

		List<SpriteOfflineColorSpace> known = [.. reads.Select(static read => read.Space).Distinct()];
		if (known.Count > 1)
		{
			return (ColorSpaceReadStatus.Conflict, SpriteOfflineColorSpace.GammaUnorm,
				"conflicting PlayerSettings m_ActiveColorSpace values were found; baking in GammaUnorm (explicit default)");
		}

		SpriteOfflineColorSpace space = known[0];
		string diagnostic = space is SpriteOfflineColorSpace.LinearSrgb
			? "PlayerSettings m_ActiveColorSpace is Linear"
			: "PlayerSettings m_ActiveColorSpace is Gamma";
		return (ColorSpaceReadStatus.Unique, space, diagnostic);
	}

	private static PlayerSettingsRead ReadPlayerSettings(IUnityObjectBase asset)
	{
		if (asset is not TypeTreeObject typeTree)
		{
			return new PlayerSettingsRead(PlayerSettingsStatus.Unreadable, default);
		}

		FieldRead editor = ReadFields(typeTree.EditorFields, out SpriteOfflineColorSpace editorSpace);
		FieldRead release = ReadFields(typeTree.ReleaseFields, out SpriteOfflineColorSpace releaseSpace);
		if (editor is FieldRead.Known && release is FieldRead.Known && editorSpace != releaseSpace)
		{
			return new PlayerSettingsRead(PlayerSettingsStatus.Unknown, default);
		}

		if (editor is FieldRead.Known)
		{
			return new PlayerSettingsRead(PlayerSettingsStatus.Known, editorSpace);
		}

		if (release is FieldRead.Known)
		{
			return new PlayerSettingsRead(PlayerSettingsStatus.Known, releaseSpace);
		}

		if (editor is FieldRead.Unknown || release is FieldRead.Unknown)
		{
			return new PlayerSettingsRead(PlayerSettingsStatus.Unknown, default);
		}

		return new PlayerSettingsRead(PlayerSettingsStatus.Unreadable, default);
	}

	private static FieldRead ReadFields(SerializableStructure? fields, out SpriteOfflineColorSpace space)
	{
		space = default;
		if (fields is null)
		{
			return FieldRead.Missing;
		}

		foreach (string name in new[] { "m_ActiveColorSpace", "activeColorSpace", "ActiveColorSpace" })
		{
			if (!fields.TryGetField(name, out SerializableValue value))
			{
				continue;
			}

			try
			{
				return TryInterpret(value.AsInt32, out space) ? FieldRead.Known : FieldRead.Unknown;
			}
			catch (Exception)
			{
				return FieldRead.Unreadable;
			}
		}

		return FieldRead.Missing;
	}

	private enum ColorSpaceReadStatus
	{
		Missing,
		Unique,
		Conflict,
		Indeterminate,
	}

	private enum FieldRead
	{
		Missing,
		Known,
		Unknown,
		Unreadable,
	}

	private enum PlayerSettingsStatus
	{
		Known,
		Unknown,
		Unreadable,
	}

	private readonly record struct PlayerSettingsRead(PlayerSettingsStatus Status, SpriteOfflineColorSpace Space);
}
