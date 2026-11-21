using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;

namespace AssetRipper.Export.UnityProjects.Textures;

internal static class SpriteTextureSemantics
{
	public static bool CanUseRgbaPng(ITexture2D source) => TryDescribe(source, out _);

	public static bool TryDescribe(ITexture2D source, [NotNullWhen(false)] out string? reason)
	{
		if (source.Mips)
		{
			reason = "the source texture has mipmaps";
			return false;
		}

		if (source.StreamingMipmaps_C28)
		{
			reason = "the source texture has streaming mipmaps";
			return false;
		}

		if (!IsLdrColor(source.Format_C28E))
		{
			reason = $"the source texture format {source.Format_C28E} is not a verified decoded color format";
			return false;
		}

		TextureUsageMode usage = source.LightmapFormat_C28E;
		if (usage == TextureUsageMode.Default)
		{
			reason = null;
			return true;
		}

		// Official 2022.3 meaning: "Texture is always padded if NPOT and on low-end
		// hardware." This is not a color-encoding mode. Allow it only when the
		// serialized image is exactly one mip of the logical Width x Height.
		if (usage == TextureUsageMode.AlwaysPadded)
		{
			if (TryGetLogicalMip0ByteCount(source.Format_C28E, source.Width_C28, source.Height_C28, out int expected)
				&& source.ImageCount_C28 <= 1
				&& source.ImageDataLength == expected
				&& (source.CompleteImageSize <= 0 || source.CompleteImageSize == expected))
			{
				reason = null;
				return true;
			}

			reason = "the source texture AlwaysPadded usage has serialized image data that does not match the logical width and height";
			return false;
		}

		reason = $"the source texture usage mode {usage} is not a verified LDR color semantic";
		return false;
	}

	// Positive list of ordinary unsigned LDR color formats on TextureConverter's
	// byte-channel path. No HDR, signed, high-precision, or unknown future formats.
	// This preserves decoded texels, not a claim of bit-exact native GPU filtering.
	private static bool IsLdrColor(TextureFormat format) => format is
		TextureFormat.Alpha8 or TextureFormat.ARGB4444 or TextureFormat.RGB24 or TextureFormat.RGBA32
		or TextureFormat.ARGB32 or TextureFormat.RGB565 or TextureFormat.BGR24 or TextureFormat.RGBA4444
		or TextureFormat.BGRA32_14 or TextureFormat.BGRA32_37
		or TextureFormat.DXT1 or TextureFormat.DXT3 or TextureFormat.DXT5
		or TextureFormat.DXT1Crunched or TextureFormat.DXT5Crunched or TextureFormat.BC7
		or TextureFormat.PVRTC_RGB2 or TextureFormat.PVRTC_RGBA2 or TextureFormat.PVRTC_RGB4 or TextureFormat.PVRTC_RGBA4
		or TextureFormat.ETC_RGB4 or TextureFormat.ETC2_RGB or TextureFormat.ETC2_RGBA1 or TextureFormat.ETC2_RGBA8
		or TextureFormat.ETC_RGB4Crunched or TextureFormat.ETC2_RGBA8Crunched
		or TextureFormat.ATC_RGB4 or TextureFormat.ATC_RGBA8
		or TextureFormat.ASTC_4x4 or TextureFormat.ASTC_5x5 or TextureFormat.ASTC_6x6
		or TextureFormat.ASTC_8x8 or TextureFormat.ASTC_10x10 or TextureFormat.ASTC_12x12
		or TextureFormat.ASTC_RGBA_4x4 or TextureFormat.ASTC_RGBA_5x5 or TextureFormat.ASTC_RGBA_6x6
		or TextureFormat.ASTC_RGBA_8x8 or TextureFormat.ASTC_RGBA_10x10 or TextureFormat.ASTC_RGBA_12x12;

	// Uncompressed and standard block sizes only. Crunched and PVRTC payloads are
	// not a simple Width x Height function, so AlwaysPadded cannot be verified.
	private static bool TryGetLogicalMip0ByteCount(TextureFormat format, int width, int height, out int size)
	{
		size = 0;
		if (width <= 0 || height <= 0)
		{
			return false;
		}

		int bytes = format switch
		{
			TextureFormat.Alpha8 => width * height,
			TextureFormat.RGB24 or TextureFormat.BGR24 => checked(width * height * 3),
			TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32_14 or TextureFormat.BGRA32_37
				=> checked(width * height * 4),
			TextureFormat.ARGB4444 or TextureFormat.RGBA4444 or TextureFormat.RGB565 => checked(width * height * 2),
			TextureFormat.DXT1 or TextureFormat.ETC_RGB4 or TextureFormat.ETC2_RGB or TextureFormat.ETC2_RGBA1
				or TextureFormat.ATC_RGB4 => BlockSize(width, height, 4, 4, 8),
			TextureFormat.DXT3 or TextureFormat.DXT5 or TextureFormat.BC7 or TextureFormat.ETC2_RGBA8
				or TextureFormat.ATC_RGBA8 => BlockSize(width, height, 4, 4, 16),
			TextureFormat.ASTC_4x4 or TextureFormat.ASTC_RGBA_4x4 => BlockSize(width, height, 4, 4, 16),
			TextureFormat.ASTC_5x5 or TextureFormat.ASTC_RGBA_5x5 => BlockSize(width, height, 5, 5, 16),
			TextureFormat.ASTC_6x6 or TextureFormat.ASTC_RGBA_6x6 => BlockSize(width, height, 6, 6, 16),
			TextureFormat.ASTC_8x8 or TextureFormat.ASTC_RGBA_8x8 => BlockSize(width, height, 8, 8, 16),
			TextureFormat.ASTC_10x10 or TextureFormat.ASTC_RGBA_10x10 => BlockSize(width, height, 10, 10, 16),
			TextureFormat.ASTC_12x12 or TextureFormat.ASTC_RGBA_12x12 => BlockSize(width, height, 12, 12, 16),
			_ => -1,
		};
		if (bytes <= 0)
		{
			return false;
		}

		size = bytes;
		return true;
	}

	private static int BlockSize(int width, int height, int blockWidth, int blockHeight, int bytesPerBlock)
	{
		int blocksX = (width + blockWidth - 1) / blockWidth;
		int blocksY = (height + blockHeight - 1) / blockHeight;
		return checked(blocksX * blocksY * bytesPerBlock);
	}
}
