using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using System.Drawing;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>Export-local unpacking state. Never rewrites the reusable Session Sprite.</summary>
internal sealed class YamlUnpackedSpriteExporter(YamlSpriteExporter yamlExporter)
{
	private readonly List<IExportCollection> extraExportCollections = [];
	private readonly Dictionary<ITexture2D, DirectBitmap?> bitmapCache = new();
	public IReadOnlyList<IExportCollection> ExtraExportCollections => extraExportCollections;

	public bool TryCreateCollection(ISprite sprite, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		exportCollection = null;
		if (!SpriteSourceSnapshotStore.TryGet(sprite, out SpriteSourceSnapshot? snapshot))
		{
			Logger.Warning(LogCategory.Export, $"Cannot unpack sprite '{sprite.GetBestName()}' as an independent texture: no source snapshot is available. Falling back to the YAML atlas or sheet reference.");
			return false;
		}

		if (!SpriteUnpackValidator.TryValidate(snapshot, out string? reason)
			|| !SpriteCopyGuard.CanCopy(sprite, out reason)
			|| !TryGetDecodedBitmap(snapshot.Texture, out DirectBitmap? bitmap, out reason)
			|| bitmap is null)
		{
			LogFallback(sprite, reason);
			return false;
		}

		SpriteUnpackRequest request = new(
			snapshot.Rect.Width,
			snapshot.Rect.Height,
			snapshot.TextureRectOffset.X,
			snapshot.TextureRectOffset.Y,
			snapshot.TextureRect.X,
			snapshot.TextureRect.Y,
			snapshot.TextureRect.Width,
			snapshot.TextureRect.Height,
			snapshot.PackingRotation,
			UsesTightMesh(snapshot),
			ToUnpackTriangles(snapshot),
			snapshot.Pivot.X,
			snapshot.Pivot.Y,
			snapshot.PixelsToUnits);

		if (!SpriteUnpacker.TryUnpack(bitmap, request, out DirectBitmap? canvas, out SpriteUnpackPlan plan, out reason))
		{
			LogFallback(sprite, reason);
			return false;
		}

		try
		{
			ProcessedAssetCollection collection = new GameBundle().AddNewProcessedCollection("YamlUnpackedSprites", sprite.Collection.Version);
			ITexture2D unpackedTexture = CreateUnpackedTexture(collection, sprite, snapshot.Texture, plan);
			ISprite exportSprite = collection.CreateSprite();
			exportSprite.CopyValues(sprite);
			if (!SpriteUvRemapper.TryRewrite(exportSprite, snapshot, sprite.Collection.EndianType, out reason))
			{
				LogFallback(sprite, reason);
				return false;
			}
			RewriteSprite(exportSprite, unpackedTexture, snapshot, plan);
			extraExportCollections.Add(new UnpackedSpriteTextureExportCollection(new BinaryAssetExporter(), unpackedTexture, snapshot.Texture!, canvas));
			exportCollection = new YamlUnpackedSpriteExportCollection(yamlExporter, sprite, exportSprite);
			return true;
		}
		catch (Exception exception)
		{
			LogFallback(sprite, exception.Message);
			return false;
		}
	}

	private bool TryGetDecodedBitmap(ITexture2D? texture, [NotNullWhen(true)] out DirectBitmap? bitmap, [NotNullWhen(false)] out string? reason)
	{
		bitmap = null;
		if (texture is null)
		{
			reason = "the source texture is missing";
			return false;
		}

		if (bitmapCache.TryGetValue(texture, out DirectBitmap? cached))
		{
			if (cached is null || cached.IsEmpty)
			{
				reason = "the source texture could not be decoded";
				return false;
			}

			bitmap = cached;
			reason = null;
			return true;
		}

		if (!texture.CheckAssetIntegrity() || !TextureConverter.TryConvertToBitmap(texture, out DirectBitmap decoded) || decoded.IsEmpty)
		{
			bitmapCache[texture] = null;
			reason = "the source texture could not be decoded";
			return false;
		}

		bitmapCache[texture] = decoded;
		bitmap = decoded;
		reason = null;
		return true;
	}

	private static bool UsesTightMesh(SpriteSourceSnapshot snapshot)
	{
		// packingMode Tight=0 and meshType Tight=1 can both imply a non-rectangular shape.
		return snapshot.MeshType != 0 || (snapshot.IsPacked && snapshot.PackingMode == 0);
	}

	private static IReadOnlyList<SpriteUnpackTriangle> ToUnpackTriangles(SpriteSourceSnapshot snapshot)
	{
		if (snapshot.LocalTriangles.Count == 0)
		{
			return [];
		}

		SpriteUnpackTriangle[] triangles = new SpriteUnpackTriangle[snapshot.LocalTriangles.Count];
		for (int i = 0; i < snapshot.LocalTriangles.Count; i++)
		{
			SpriteMeshTriangle triangle = snapshot.LocalTriangles[i];
			triangles[i] = new SpriteUnpackTriangle(triangle.A, triangle.B, triangle.C);
		}

		return triangles;
	}

	private static ITexture2D CreateUnpackedTexture(ProcessedAssetCollection collection, ISprite sprite, ITexture2D? source, SpriteUnpackPlan plan)
	{
		ITexture2D texture = collection.CreateTexture2D();
		string baseName = string.IsNullOrWhiteSpace(sprite.GetBestName()) ? sprite.ClassName : sprite.GetBestName();
		texture.Name = $"{baseName}_{sprite.PathID}";
		texture.Width_C28 = plan.CanvasWidth;
		texture.Height_C28 = plan.CanvasHeight;
		texture.ImageCount_C28 = 1;
		texture.Format_C28E = TextureFormat.RGBA32;
		if (texture.Has_CompleteImageSize_C28_Int32())
		{
			texture.CompleteImageSize_C28_Int32 = plan.CanvasWidth * plan.CanvasHeight * 4;
		}

		if (texture.Has_CompleteImageSize_C28_UInt32())
		{
			texture.CompleteImageSize_C28_UInt32 = (uint)(plan.CanvasWidth * plan.CanvasHeight * 4);
		}

		if (texture.Has_MipCount_C28())
		{
			texture.MipCount_C28 = 1;
		}

		texture.OriginalDirectory = sprite.OriginalDirectory ?? source?.OriginalDirectory;
		return texture;
	}

	private static void RewriteSprite(ISprite sprite, ITexture2D unpackedTexture, SpriteSourceSnapshot snapshot, SpriteUnpackPlan plan)
	{
		float width = plan.CanvasWidth;
		float height = plan.CanvasHeight;
		float pivotX = snapshot.Pivot.X;
		float pivotY = snapshot.Pivot.Y;

		sprite.Rect.CopyValues(new RectangleF(0, 0, width, height));
		if (sprite.Has_Pivot())
		{
			sprite.Pivot.CopyValues(snapshot.Pivot);
		}

		if (sprite.Has_Border())
		{
			sprite.Border.CopyValues(snapshot.Border);
		}

		sprite.Offset.X = (pivotX - 0.5f) * width;
		sprite.Offset.Y = (pivotY - 0.5f) * height;
		sprite.PixelsToUnits = snapshot.PixelsToUnits;

		ISpriteRenderData rd = sprite.RD;
		rd.Texture.SetAsset(sprite.Collection, unpackedTexture);
		if (rd.Has_AlphaTexture())
		{
			rd.AlphaTexture.SetAsset(sprite.Collection, null);
		}

		if (rd.Has_SecondaryTextures())
		{
			rd.SecondaryTextures.Clear();
		}

		rd.TextureRect.CopyValues(new RectangleF(plan.TextureRectX, plan.TextureRectY, plan.TextureRectWidth, plan.TextureRectHeight));
		rd.TextureRectOffset.X = plan.CropOffsetX;
		rd.TextureRectOffset.Y = plan.CropOffsetY;
		if (rd.Has_AtlasRectOffset() && snapshot.AtlasRectOffset is not null)
		{
			rd.AtlasRectOffset.X = 0;
			rd.AtlasRectOffset.Y = 0;
		}

		// Clear packed and rotation bits; keep the verified meshType bit.
		rd.SettingsRaw = snapshot.SettingsRaw & ~0b0111111u;
		if (rd.Has_UvTransform() && snapshot.UvTransform is Vector4 uv)
		{
			// Compose the guarded source pixel affine transform with the SAME crop,
			// inverse reflection, and padding used for explicit UVs and pixels.
			bool flipX = snapshot.PackingRotation is 1 or 3;
			bool flipY = snapshot.PackingRotation is 2 or 3;
			float x = uv.Y - snapshot.TextureRect.X;
			float y = uv.W - snapshot.TextureRect.Y;
			rd.UvTransform.X = flipX ? -uv.X : uv.X;
			rd.UvTransform.Y = (flipX ? snapshot.TextureRect.Width - x : x) + plan.CropOffsetX;
			rd.UvTransform.Z = flipY ? -uv.Z : uv.Z;
			rd.UvTransform.W = (flipY ? snapshot.TextureRect.Height - y : y) + plan.CropOffsetY;
		}

		if (rd.Has_DownscaleMultiplier())
		{
			rd.DownscaleMultiplier = 1;
		}

		sprite.SpriteAtlasP = null;
		sprite.AtlasTags?.Clear();
	}

	private static void LogFallback(ISprite sprite, string? reason)
	{
		Logger.Warning(LogCategory.Export, $"Cannot unpack sprite '{sprite.GetBestName()}' as an independent texture: {reason}. Falling back to the YAML atlas or sheet reference.");
	}

}
