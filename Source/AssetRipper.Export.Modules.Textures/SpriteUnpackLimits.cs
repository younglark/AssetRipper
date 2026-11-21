namespace AssetRipper.Export.Modules.Textures;

/// <summary>Bounds checked before integer conversion or any unpacking allocation.</summary>
public static class SpriteUnpackLimits
{
	// Unity TextureImporter maxTextureSize upper bound; never generate a larger image
	// and then silently clamp its importer. Pixel budget is 64 MiB per RGBA image.
	public const int MaximumDimension = 16384;
	public const int MaximumPixels = 16 * 1024 * 1024;

	public static bool IsSupportedSize(float width, float height) =>
		float.IsFinite(width) && float.IsFinite(height) && width > 0 && height > 0
		&& width <= MaximumDimension && height <= MaximumDimension
		&& (double)width * height <= MaximumPixels;
}
