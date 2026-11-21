using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Import.Logging;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using System.Drawing;
using System.Numerics;
namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>
/// Export-local contract-C baking. Never rewrites the reusable Session Sprite.
/// Independent PNG + FullRect YAML Sprite; FlipHorizontal is baked through native UVs.
/// </summary>
internal sealed class YamlBakedSpriteExporter(YamlSpriteExporter yamlExporter)
{
	private readonly List<IExportCollection> extraExportCollections = [];
	private readonly Dictionary<ITexture2D, DirectBitmap?> bitmapCache = new();
	private bool colorSpaceResolved;
	private SpriteOfflineColorSpace colorSpace = SpriteOfflineColorSpace.GammaUnorm;

	public IReadOnlyList<IExportCollection> ExtraExportCollections => extraExportCollections;

	public bool TryCreateCollection(ISprite sprite, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		exportCollection = null;
		if (!SpriteSourceSnapshotStore.TryGet(sprite, out SpriteSourceSnapshot? snapshot))
		{
			Logger.Warning(LogCategory.Export, $"Cannot bake sprite '{sprite.GetBestName()}' as an independent texture: no source snapshot is available. Falling back to the YAML atlas or sheet reference.");
			return false;
		}

		if (!SpriteBakeValidator.TryValidate(snapshot, sprite, out string? reason)
			|| !SpriteCopyGuard.CanCopy(sprite, out reason)
			|| !TryGetDecodedBitmap(snapshot.Texture, out DirectBitmap? bitmap, out reason)
			|| bitmap is null)
		{
			LogFallback(sprite, reason);
			return false;
		}

		EnsureColorSpace(sprite);
		(Vector2[] vertices, int[] indices) = MeshForBake(snapshot);
		SpriteOfflineRasterRequest request = new(
			snapshot.Rect.Width,
			snapshot.Rect.Height,
			snapshot.PixelsToUnits,
			snapshot.Pivot,
			snapshot.TextureRect.X,
			snapshot.TextureRect.Y,
			snapshot.TextureRect.Width,
			snapshot.TextureRect.Height,
			snapshot.TextureRectOffset,
			snapshot.UvTransform!.Value,
			snapshot.DownscaleMultiplier ?? 1f,
			snapshot.PackingRotation,
			vertices,
			indices,
			GetFilter(snapshot.Texture!),
			colorSpace,
			MipCount(snapshot.Texture!),
			HasVertexColors: false);

		if (!SpriteOfflineRasterizer.TryRasterize(bitmap, request, out DirectBitmap? canvas, out reason) || canvas is null)
		{
			LogFallback(sprite, reason);
			return false;
		}

		try
		{
			ProcessedAssetCollection collection = new GameBundle().AddNewProcessedCollection("YamlBakedSprites", sprite.Collection.Version);
			ITexture2D bakedTexture = CreateBakedTexture(collection, sprite, snapshot.Texture, canvas.Width, canvas.Height);
			ISprite exportSprite = collection.CreateSprite();
			exportSprite.CopyValues(sprite);
			if (!TryRewriteSprite(exportSprite, bakedTexture, snapshot, canvas.Width, canvas.Height, out reason))
			{
				LogFallback(sprite, reason);
				return false;
			}

			extraExportCollections.Add(new UnpackedSpriteTextureExportCollection(new BinaryAssetExporter(), bakedTexture, snapshot.Texture!, canvas));
			exportCollection = new YamlUnpackedSpriteExportCollection(yamlExporter, sprite, exportSprite);
			return true;
		}
		catch (Exception exception)
		{
			LogFallback(sprite, exception.Message);
			return false;
		}
	}

	private void EnsureColorSpace(ISprite sprite)
	{
		if (colorSpaceResolved)
		{
			return;
		}

		colorSpace = SpriteBakeColorSpaceResolver.Resolve(sprite.Collection.Bundle, out _);
		colorSpaceResolved = true;
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

	private static (Vector2[] Vertices, int[] Indices) MeshForBake(SpriteSourceSnapshot snapshot)
	{
		if (snapshot.LocalTriangles.Count > 0)
		{
			return SpriteOfflineRasterizer.FlattenTriangles(ToTuples(snapshot));
		}

		float width = snapshot.Rect.Width;
		float height = snapshot.Rect.Height;
		float pivotPx = snapshot.Pivot.X * width;
		float pivotPy = snapshot.Pivot.Y * height;
		float ppu = snapshot.PixelsToUnits;
		Vector2[] vertices =
		[
			new((0f - pivotPx) / ppu, (0f - pivotPy) / ppu),
			new((width - pivotPx) / ppu, (0f - pivotPy) / ppu),
			new((width - pivotPx) / ppu, (height - pivotPy) / ppu),
			new((0f - pivotPx) / ppu, (height - pivotPy) / ppu),
		];
		return (vertices, [0, 1, 2, 0, 2, 3]);
	}

	private static IReadOnlyList<(Vector2 A, Vector2 B, Vector2 C)> ToTuples(SpriteSourceSnapshot snapshot)
	{
		(Vector2 A, Vector2 B, Vector2 C)[] triangles = new (Vector2, Vector2, Vector2)[snapshot.LocalTriangles.Count];
		for (int i = 0; i < snapshot.LocalTriangles.Count; i++)
		{
			SpriteMeshTriangle triangle = snapshot.LocalTriangles[i];
			triangles[i] = (triangle.A, triangle.B, triangle.C);
		}

		return triangles;
	}

	private static SpriteOfflineFilter GetFilter(ITexture2D texture)
	{
		// IGLTextureSettings.FilterMode: Point=0, Bilinear=1, Trilinear=2.
		// Trilinear with mips=1 is treated as Bilinear. Missing settings default to Bilinear.
		return texture.TextureSettings_C28.FilterMode == 0
			? SpriteOfflineFilter.Point
			: SpriteOfflineFilter.Bilinear;
	}

	private static int MipCount(ITexture2D texture)
	{
		return texture.Has_MipCount_C28() ? texture.MipCount_C28 : 1;
	}

	private static ITexture2D CreateBakedTexture(ProcessedAssetCollection collection, ISprite sprite, ITexture2D? source, int width, int height)
	{
		ITexture2D texture = collection.CreateTexture2D();
		string baseName = string.IsNullOrWhiteSpace(sprite.GetBestName()) ? sprite.ClassName : sprite.GetBestName();
		texture.Name = $"{baseName}_{sprite.PathID}";
		texture.Width_C28 = width;
		texture.Height_C28 = height;
		texture.ImageCount_C28 = 1;
		texture.Format_C28E = TextureFormat.RGBA32;
		if (texture.Has_CompleteImageSize_C28_Int32())
		{
			texture.CompleteImageSize_C28_Int32 = width * height * 4;
		}

		if (texture.Has_CompleteImageSize_C28_UInt32())
		{
			texture.CompleteImageSize_C28_UInt32 = (uint)(width * height * 4);
		}

		if (texture.Has_MipCount_C28())
		{
			texture.MipCount_C28 = 1;
		}

		texture.OriginalDirectory = sprite.OriginalDirectory ?? source?.OriginalDirectory;
		return texture;
	}

	private static bool TryRewriteSprite(
		ISprite sprite,
		ITexture2D bakedTexture,
		SpriteSourceSnapshot snapshot,
		int width,
		int height,
		[NotNullWhen(false)] out string? reason)
	{
		float pivotX = snapshot.Pivot.X;
		float pivotY = snapshot.Pivot.Y;
		sprite.Rect.CopyValues(new RectangleF(0, 0, width, height));
		if (sprite.Has_Pivot())
		{
			sprite.Pivot.CopyValues(snapshot.Pivot);
		}

		if (sprite.Has_Border())
		{
			// Restore the snapshot border without clamping.
			sprite.Border.CopyValues(snapshot.Border);
		}

		sprite.Offset.X = (pivotX - 0.5f) * width;
		sprite.Offset.Y = (pivotY - 0.5f) * height;
		sprite.PixelsToUnits = snapshot.PixelsToUnits;

		ISpriteRenderData rd = sprite.RD;
		rd.Texture.SetAsset(sprite.Collection, bakedTexture);
		if (rd.Has_AlphaTexture())
		{
			rd.AlphaTexture.SetAsset(sprite.Collection, null);
		}

		if (rd.Has_SecondaryTextures())
		{
			rd.SecondaryTextures.Clear();
		}

		rd.TextureRect.CopyValues(new RectangleF(0, 0, width, height));
		rd.TextureRectOffset.X = 0;
		rd.TextureRectOffset.Y = 0;
		if (rd.Has_AtlasRectOffset())
		{
			rd.AtlasRectOffset.X = 0;
			rd.AtlasRectOffset.Y = 0;
		}

		// Clear packed, packingMode, rotation, and meshType so the asset is an unpacked FullRect sprite.
		rd.SettingsRaw = snapshot.SettingsRaw & ~0b1111111u;
		if (rd.Has_UvTransform())
		{
			rd.UvTransform.X = snapshot.PixelsToUnits;
			rd.UvTransform.Y = pivotX * width;
			rd.UvTransform.Z = snapshot.PixelsToUnits;
			rd.UvTransform.W = pivotY * height;
		}

		if (rd.Has_DownscaleMultiplier())
		{
			rd.DownscaleMultiplier = 1;
		}

		sprite.SpriteAtlasP = null;
		sprite.AtlasTags?.Clear();
		return SpriteFullRectMeshWriter.TryWrite(sprite, width, height, snapshot.PixelsToUnits, snapshot.Pivot, out reason);
	}

	private static void LogFallback(ISprite sprite, string? reason)
	{
		Logger.Warning(LogCategory.Export, $"Cannot bake sprite '{sprite.GetBestName()}' as an independent FullRect texture: {reason}. Falling back to the YAML atlas or sheet reference.");
	}
}
