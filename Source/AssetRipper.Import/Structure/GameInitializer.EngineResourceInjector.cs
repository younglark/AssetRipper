using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.Mining.PredefinedAssets;
using AssetRipper.SourceGenerated;
using AssetRipper.Tpk;
using AssetRipper.Tpk.EngineAssets;
using Object = AssetRipper.Mining.PredefinedAssets.Object;

namespace AssetRipper.Import.Structure;

internal sealed partial record class GameInitializer
{
	private static class EngineResourceInjector
	{
		private sealed class InjectedEngineBundle : VirtualBundle<InjectedEngineAssetCollection>
		{
			public override string Name => "Generated Engine Collections";
		}

		private static void AddAssets(InjectedEngineAssetCollection collection, Dictionary<long, Object> assets)
		{
			foreach ((long pathID, Object @object) in assets)
			{
				IUnityObjectBase asset = AssetFactory.Create(new AssetInfo(collection, pathID, @object.TypeID));
				if (asset is INamed named && @object is NamedObject namedObject)
				{
					named.Name = namedObject.Name;
				}
				collection.Add(asset);
			}
		}

		public static void InjectEngineFilesIfNecessary(GameBundle gameBundle, UnityVersion targetVersion)
		{
			if (!gameBundle.HasAnyAssetCollections())
			{
				// If there are no asset collections, then the game bundle is empty and we should skip injecting engine resources.
				return;
			}
			bool injectDefaultResources = gameBundle.ResolveCollection(SpecialFileNames.DefaultResourceName1) is null;
			bool injectExtraResources = gameBundle.ResolveCollection(SpecialFileNames.BuiltinExtraName2) is null;
			if (!injectDefaultResources && !injectExtraResources)
			{
				return;
			}

			if (targetVersion == default)
			{
				targetVersion = gameBundle.GetMaxUnityVersion();
			}

			InjectedEngineBundle bundle = new();
			gameBundle.AddBundle(bundle);

			EngineResourceData data = GetData(targetVersion);
			if (injectDefaultResources)
			{
				InjectedEngineAssetCollection collection = new(SpecialFileNames.DefaultResourceName1, targetVersion, bundle);
				AddAssets(collection, data.DefaultResources);
			}
			if (injectExtraResources)
			{
				InjectedEngineAssetCollection collection = new(SpecialFileNames.BuiltinExtraName2, targetVersion, bundle);
				AddAssets(collection, data.ExtraResources);
			}
		}

		private static EngineResourceData GetData(UnityVersion version)
		{
			TpkFile tpk = TpkFile.FromStream(EngineAssetsTpk.GetStream());
			TpkEngineAssetsBlob blob = (TpkEngineAssetsBlob)tpk.GetDataBlob();
			if (blob.Data.Count == 0)
			{
				return new EngineResourceData();
			}

			int index;
			if (version <= blob.Data[0].Key)
			{
				index = 0;
			}
			else
			{
				index = blob.Data.Count - 1;
				for (int i = 1; i < blob.Data.Count; i++)
				{
					if (version <= blob.Data[i].Key)
					{
						index = i - 1;
						break;
					}
				}
			}

			string json = blob.Data[index].Value;
			return EngineResourceData.FromJson(json);
		}
	}
}
