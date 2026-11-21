using AssetRipper.Assets;
using AssetRipper.Assets.Traversal;

namespace AssetRipper.Processing.Textures;

/// <summary>Checks serialized numeric fields without following references or decoding textures.</summary>
public static class SpriteSourceValueValidator
{
	public static bool HasNonFiniteValues(IUnityAssetBase asset)
	{
		FiniteWalker walker = new();
		asset.WalkStandard(walker);
		return walker.Invalid;
	}

	private sealed class FiniteWalker : AssetWalker
	{
		public bool Invalid { get; private set; }
		public override bool EnterList<T>(IReadOnlyList<T> list) => typeof(T) != typeof(byte) && !Invalid;
		public override void VisitPrimitive<T>(T value)
		{
			Invalid |= value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d);
		}
	}
}
