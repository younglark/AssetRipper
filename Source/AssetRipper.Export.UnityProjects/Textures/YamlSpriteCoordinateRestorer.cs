using AssetRipper.Assets.Bundles;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>Keep native Sprite layout metadata independent of its sampled texture rectangle.</summary>
internal static class YamlSpriteCoordinateRestorer
{
	public static bool TryCreate(YamlSpriteExporter exporter, ISprite sprite, [NotNullWhen(true)] out IExportCollection? collection)
	{
		collection = null;
		if (!SpriteSourceSnapshotStore.TryGet(sprite, out SpriteSourceSnapshot? source)
			|| source.PackingRotation != 0 || source.LocalTriangles.Count == 0
			|| !SpriteUnpackValidator.TryValidate(source, out _)
			|| !SpriteCopyGuard.CanCopy(sprite, out _))
		{
			return false;
		}

		// SpriteProcessor also feeds TextureImporter metadata, which needs atlas
		// coordinates. Do not change it or the reusable session. Native Sprite YAML
		// already has a separate RD.textureRect; its Rect/Border/Pivot describe the
		// logical canvas used by SpriteRenderer.Sliced and UGUI's padding calculation.
		var assets = new GameBundle().AddNewProcessedCollection("YamlSpriteCoordinates", sprite.Collection.Version);
		ISprite copy = assets.CreateSprite();
		copy.CopyValues(sprite);
		copy.Rect.CopyValues(source.Rect);
		copy.Pivot?.CopyValues(source.Pivot);
		copy.Border?.CopyValues(source.Border);
		copy.Offset.CopyValues(source.Offset);
		copy.RD.TextureRectOffset.CopyValues(source.TextureRectOffset);
		collection = new YamlUnpackedSpriteExportCollection(exporter, sprite, copy);
		return true;
	}
}
