using AssetRipper.Assets.Bundles;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;

namespace AssetRipper.Assets.Collections;

/// <summary>
/// A collection of engine assets injected when the game files do not include
/// <c>unity default resources</c> or <c>unity_builtin_extra</c>.
/// These entries carry type and name only; they are not loaded from a serialized engine file.
/// </summary>
public sealed class InjectedEngineAssetCollection : VirtualAssetCollection
{
	public InjectedEngineAssetCollection(string name, UnityVersion version, Bundle bundle) : base(bundle)
	{
		Name = name;
		Version = version;
		OriginalVersion = version;
		Platform = BuildTarget.NoTarget;
		Flags = TransferInstructionFlags.SerializeGameRelease | TransferInstructionFlags.IsBuiltinResourcesFile;
	}

	public void Add(IUnityObjectBase asset) => AddAsset(asset);
}
