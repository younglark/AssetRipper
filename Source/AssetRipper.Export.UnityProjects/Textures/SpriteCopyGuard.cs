using AssetRipper.SourceGenerated.Classes.ClassID_213;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>CopyValues resolves PPtrs; do not let missing references silently become null.</summary>
internal static class SpriteCopyGuard
{
	public static bool CanCopy(ISprite sprite, out string? reason)
	{
		foreach (var (field, pointer) in sprite.FetchDependencies())
		{
			if (!pointer.IsNull && sprite.Collection.TryGetAsset(pointer) is null)
			{
				reason = $"unresolved reference in {field} cannot be copied reliably";
				return false;
			}
		}
		reason = null;
		return true;
	}
}
