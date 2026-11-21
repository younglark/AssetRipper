using AssetRipper.Export.Modules.Textures;
using AssetRipper.Processing.Textures;

namespace AssetRipper.Export.UnityProjects.Textures;

internal static class SpriteUnpackNumericGuard
{
	public static bool IsSafe(SpriteSourceSnapshot source)
	{
		if (source.HasNonFiniteValues || source.Texture is not { } texture
			|| texture.ImageCount_C28 != 1
			|| SpriteSourceValueValidator.HasNonFiniteValues(texture)
			|| !SpriteUnpackLimits.IsSupportedSize(texture.Width_C28, texture.Height_C28)
			|| !SpriteUnpackLimits.IsSupportedSize(source.Rect.Width, source.Rect.Height)
			|| !SpriteUnpackLimits.IsSupportedSize(source.TextureRect.Width, source.TextureRect.Height)) return false;
		// Values used in float affine arithmetic must remain finite after multiplication.
		return float.IsFinite(source.Pivot.X * source.Rect.Width)
			&& float.IsFinite(source.Pivot.Y * source.Rect.Height)
			&& float.IsFinite((source.Pivot.X - .5f) * source.Rect.Width)
			&& float.IsFinite((source.Pivot.Y - .5f) * source.Rect.Height)
			&& source.PixelsToUnits > 0;
	}
}
