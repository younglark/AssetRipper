using AssetRipper.SourceGenerated.Classes.ClassID_213;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>
/// Exports a rewritten Sprite copy while keeping the original Sprite as the collection identity
/// so scenes and prefabs still resolve to this YAML asset.
/// </summary>
public sealed class YamlUnpackedSpriteExportCollection : AssetExportCollection<ISprite>
{
	private readonly ISprite exportSprite;

	public YamlUnpackedSpriteExportCollection(IAssetExporter assetExporter, ISprite identity, ISprite exportSprite)
		: base(assetExporter, identity)
	{
		this.exportSprite = exportSprite;
	}

	protected override bool ExportInner(IExportContainer container, string filePath, string dirPath, FileSystem fileSystem)
	{
		return AssetExporter.Export(container, exportSprite, filePath, fileSystem);
	}
}
