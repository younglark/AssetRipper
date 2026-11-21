using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Extensions;
using System.Buffers.Binary;

namespace AssetRipper.Processing.Textures;

/// <summary>Validates float channels stored as opaque bytes, including unused vertices.</summary>
internal static class SpriteVertexDataValidator
{
	public static void Validate(ISprite sprite)
	{
		var data = sprite.RD.VertexData;
		var version = sprite.Collection.Version;
		if (data.VertexCount == 0) return;
		if (version.LessThan(5, 6) || !data.Has_Channels() || data.Has_Streams() || data.Has_Streams_0_())
			throw new NotSupportedException("unverified vertex-buffer numeric layout");
		foreach (var channel in data.Channels.Where(c => c.IsSet()))
		{
			int stride = data.GetStreamStride(version, channel.Stream);
			int size = channel.GetStride(version);
			int start = checked(data.GetStreamOffset(version, channel.Stream) + channel.Offset);
			if (size <= 0 || stride < channel.Offset + size || start < 0
				|| start + (long)(data.VertexCount - 1) * stride + size > data.Data.Length)
				throw new NotSupportedException("invalid numeric vertex/UV buffer range");
			// Float32=0 and Float16=1 in each supported channel-format generation.
			// Other numeric formats are bounded integer encodings, hence finite.
			if (channel.Format is not (0 or 1)) continue;
			int componentSize = channel.Format == 0 ? 4 : 2;
			bool bigEndian = sprite.Collection.EndianType == EndianType.BigEndian;
			for (uint vertex = 0; vertex < data.VertexCount; vertex++)
			{
				int offset = checked(start + (int)vertex * stride);
				for (int c = 0; c < channel.GetDataDimension(); c++)
				{
					ReadOnlySpan<byte> component = data.Data.AsSpan(offset + c * componentSize, componentSize);
					float value = componentSize == 4
						? bigEndian ? BinaryPrimitives.ReadSingleBigEndian(component) : BinaryPrimitives.ReadSingleLittleEndian(component)
						: (float)(bigEndian ? BinaryPrimitives.ReadHalfBigEndian(component) : BinaryPrimitives.ReadHalfLittleEndian(component));
					if (!float.IsFinite(value)) throw new NotSupportedException("non-finite numeric vertex channel");
				}
			}
		}
	}
}
