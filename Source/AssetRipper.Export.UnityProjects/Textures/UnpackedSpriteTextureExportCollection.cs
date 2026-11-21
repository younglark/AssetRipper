using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_1006;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.TextureImporterPlatformSettings;

namespace AssetRipper.Export.UnityProjects.Textures;

public sealed class UnpackedSpriteTextureExportCollection : AssetExportCollection<ITexture2D>
{
	private readonly DirectBitmap bitmap;
	private readonly ITexture2D sourceTexture;

	public UnpackedSpriteTextureExportCollection(
		IAssetExporter assetExporter,
		ITexture2D texture,
		ITexture2D sourceTexture,
		DirectBitmap bitmap)
		: base(assetExporter, texture)
	{
		this.bitmap = bitmap;
		this.sourceTexture = sourceTexture;
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		using Stream stream = fileSystem.File.Create(filePath);
		bitmap.Save(stream, ImageExportFormat.Png);
		return true;
	}

	protected override string GetExportExtension(IUnityObjectBase asset)
	{
		return "png";
	}

	protected override IUnityObjectBase CreateImporter(IExportContainer container)
	{
		ITextureImporter importer = ImporterFactory.GenerateTextureImporter(container, sourceTexture);
		importer.TextureTypeE = TextureImporterType.Default;
		importer.Format = (int)TextureImporterFormat.RGBA32;
		// Keep decoded RGBA, including transparent RGB; do not ask Unity for dilation.
		importer.AlphaIsTransparency = 0;
		importer.NPOTScaleE = TextureImporterNPOTScale.None;
		importer.MipMaps.EnableMipMap = 0;
		importer.SpriteModeE = SpriteImportMode.Single;
		int maxSize = CalculateMaxTextureSize(bitmap.Width, bitmap.Height);
		importer.MaxTextureSize = maxSize;
		foreach (ITextureImporterPlatformSettings platformSettings in importer.PlatformSettings)
		{
			platformSettings.MaxTextureSize = maxSize;
			platformSettings.TextureCompression = (int)TextureImporterCompression.Uncompressed;
			platformSettings.Format = (int)TextureImporterFormat.RGBA32;
		}

		return importer;
	}

	private static int CalculateMaxTextureSize(int width, int height)
	{
		int maxSideLength = int.Max(width, height);
		int result = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(maxSideLength, 1));
		return int.Clamp(result, 2048, 16384);
	}
}
