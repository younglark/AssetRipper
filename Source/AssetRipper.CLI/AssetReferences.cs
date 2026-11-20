using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace AssetRipper.CLI;

/// <summary>Collection-instance identifiers shared by list, search, and get for one loaded game.</summary>
internal sealed class AssetReferences
{
	private static readonly ConditionalWeakTable<GameBundle, AssetReferences> maps = new();
	private readonly AssetCollection[] collections;
	private readonly Dictionary<AssetCollection, int> indices = new(ReferenceEqualityComparer.Instance);

	private AssetReferences(GameBundle bundle)
	{
		collections = bundle.FetchAssetCollections().ToArray();
		for (int i = 0; i < collections.Length; i++) indices.Add(collections[i], i);
	}

	public static AssetReferences For(GameBundle bundle) => maps.GetValue(bundle, static value => new(value));

	public string Get(IUnityObjectBase asset) => string.Create(CultureInfo.InvariantCulture, $"c{indices[asset.Collection]}:{asset.PathID}");

	public IUnityObjectBase? Find(string reference)
	{
		int separator = reference.IndexOf(':');
		if (separator < 2 || reference[0] != 'c'
			|| !int.TryParse(reference.AsSpan(1, separator - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
			|| index >= collections.Length
			|| !long.TryParse(reference.AsSpan(separator + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long pathId))
		{
			return null;
		}
		return collections[index].TryGetAsset(pathId);
	}
}
