using AssetRipper.Assets;
using AssetRipper.Export.UnityProjects.Project;
using AssetRipper.Export.Configuration;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;

namespace AssetRipper.Export.UnityProjects.Textures;

public sealed class YamlSpriteExporter : YamlExporterBase
{
	private readonly YamlUnpackedSpriteExporter? unpacker;
	private readonly YamlBakedSpriteExporter? baker;

	public YamlSpriteExporter(SpriteExportMode mode)
	{
		if (mode == SpriteExportMode.YamlUnpacked)
		{
			unpacker = new(this);
		}
		else if (mode == SpriteExportMode.YamlBaked)
		{
			baker = new(this);
		}
	}

	public YamlSpriteExporter() : this(SpriteExportMode.Yaml) { }

	public IReadOnlyList<IExportCollection> ExtraExportCollections =>
		unpacker?.ExtraExportCollections ?? baker?.ExtraExportCollections ?? [];

	public override bool TryCreateCollection(IUnityObjectBase asset, [NotNullWhen(true)] out IExportCollection? exportCollection)
	{
		exportCollection = asset switch
		{
			ISprite sprite when unpacker is not null && unpacker.TryCreateCollection(sprite, out IExportCollection? unpacked) => unpacked,
			ISprite sprite when baker is not null && baker.TryCreateCollection(sprite, out IExportCollection? baked) => baked,
			ISprite sprite when YamlSpriteCoordinateRestorer.TryCreate(this, sprite, out IExportCollection? restored) => restored,
			ISprite sprite => new AssetExportCollection<ISprite>(this, sprite),
			ISpriteAtlas => EmptyExportCollection.Instance,
			_ => null,
		};
		return exportCollection is not null;
	}
}
