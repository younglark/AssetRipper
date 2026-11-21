using AssetRipper.IO.Endian;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using System.Buffers.Binary;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>Rebases explicit, normalized bottom-left texture coordinates on an export-local copy.</summary>
internal static class SpriteUvRemapper
{
	private const float Tolerance = 0.001f;

	public static bool TryRewrite(ISprite sprite, SpriteSourceSnapshot source, EndianType endianType, out string? reason)
	{
		reason = null;
		if (!TryValidateTransform(source, out reason)) return false;
		try
		{
			var rd = sprite.RD;
			bool hasExplicitUv = false;
			if (rd.Has_Vertices())
			{
				bool implicitUv = rd.Vertices.Any(v => v.Has_Uv()) && rd.Vertices.Where(v => v.Has_Uv()).All(v => v.Uv!.X == 0 && v.Uv!.Y == 0);
				if (implicitUv && !CanDeriveUnrotatedUv(source))
					throw new NotSupportedException("all-zero legacy UVs may be placeholders");
				foreach (var vertex in rd.Vertices)
				{
					if (!vertex.Has_Uv()) continue;
					hasExplicitUv = true;
					Vector2 uv = Map(vertex.Uv, new Vector2(vertex.Pos.X, vertex.Pos.Y), source, implicitUv);
					if (!IsFinite(uv)) throw new NotSupportedException("non-finite legacy UV");
					vertex.Uv.CopyValues(uv);
				}
			}
			if (rd.Has_VertexData() && rd.VertexData.VertexCount > 0)
			{
				var data = rd.VertexData;
				var version = sprite.Collection.Version;
				if (endianType != EndianType.LittleEndian)
					throw new NotSupportedException("big-endian vertex buffers have not been validated");
				if (data.Has_Streams() || data.Has_Streams_0_())
					throw new NotSupportedException("explicit legacy stream descriptors have not been validated");
				if (!data.Has_Channels() || version.LessThan(5, 6))
					throw new NotSupportedException("unverified vertex channel layout");
				foreach (var stream in data.Channels.Where(c => c.IsSet()).GroupBy(c => c.Stream))
				{
					int offset = 0;
					foreach (var active in stream.OrderBy(c => c.Offset))
					{
						if (active.Offset != offset) throw new NotSupportedException("gapped or overlapping vertex channel layout");
						offset = checked(offset + active.GetStride(version));
					}
				}
				for (ShaderChannel uvChannel = ShaderChannel.UV0; uvChannel <= ShaderChannel.UV7; uvChannel++)
				{
					if (!uvChannel.HasChannel(version) || uvChannel.ToChannel(version) >= data.Channels.Count) continue;
					var channel = data.GetChannel(version, uvChannel);
					if (!channel.IsSet()) continue;
					hasExplicitUv = true;
					if (uvChannel != ShaderChannel.UV0 || channel.GetDataDimension() != 2 || channel.Format != 0)
						throw new NotSupportedException($"unsupported {uvChannel} format {channel.Format}, dimension {channel.GetDataDimension()}");
					var position = data.GetChannel(version, ShaderChannel.Vertex);
					if (!position.IsSet() || position.Format != 0 || position.GetDataDimension() != 3)
						throw new NotSupportedException("UV verification requires float3 local positions");
					int positionStride = data.GetStreamStride(version, position.Stream);
					int positionStart = data.GetStreamOffset(version, position.Stream) + position.Offset;
					int stride = data.GetStreamStride(version, channel.Stream);
					int start = data.GetStreamOffset(version, channel.Stream) + channel.Offset;
					if (channel.Offset + 8 > stride) throw new NotSupportedException("overlapping UV channel layout");
					byte[] bytes = (byte[])data.Data.Clone();
					bool bigEndian = endianType == EndianType.BigEndian;
					bool anyNonzero = false;
					for (int i = 0; i < data.VertexCount; i++)
					{
						ReadOnlySpan<byte> pair = bytes.AsSpan(checked(start + i * stride), 8);
						anyNonzero |= ReadFloat(pair, bigEndian) != 0 || ReadFloat(pair[4..], bigEndian) != 0;
					}
					bool implicitUv = !anyNonzero && CanDeriveUnrotatedUv(source);
					for (int i = 0; i < data.VertexCount; i++)
					{
						Span<byte> pair = bytes.AsSpan(checked(start + i * stride), 8);
						Vector2 uv = new(ReadFloat(pair, bigEndian), ReadFloat(pair[4..], bigEndian));
						anyNonzero |= uv != Vector2.Zero;
						ReadOnlySpan<byte> local = data.Data.AsSpan(checked(positionStart + i * positionStride), 12);
						if (!float.IsFinite(ReadFloat(local[8..], bigEndian))) throw new NotSupportedException("non-finite numeric vertex Z");
						uv = Map(uv, new Vector2(ReadFloat(local, bigEndian), ReadFloat(local[4..], bigEndian)), source, implicitUv);
						if (!IsFinite(uv)) throw new NotSupportedException("non-finite vertex UV");
						WriteFloat(pair, uv.X, bigEndian);
						WriteFloat(pair[4..], uv.Y, bigEndian);
					}
					if (!anyNonzero && !implicitUv) throw new NotSupportedException("all-zero vertex UVs may be placeholders");
					data.Data = bytes;
				}
			}
			bool fractional = source.TextureRect.X != MathF.Round(source.TextureRect.X)
				|| source.TextureRect.Y != MathF.Round(source.TextureRect.Y)
				|| source.TextureRect.Width != MathF.Round(source.TextureRect.Width)
				|| source.TextureRect.Height != MathF.Round(source.TextureRect.Height);
			if (fractional && !hasExplicitUv)
				throw new NotSupportedException("fractional tight bounds require explicit UV/geometry evidence");
			if (source.UvTransform is not null && source.PackingRotation != 0 && !hasExplicitUv)
				throw new NotSupportedException("rotated uvTransform requires explicit UV/geometry evidence");
			return true;
		}
		catch (Exception exception) when (exception is NotSupportedException or ArgumentException or OverflowException or IndexOutOfRangeException)
		{
			reason = $"explicit UV remapping failed: {exception.Message}";
			return false;
		}
	}

	private static bool CanDeriveUnrotatedUv(SpriteSourceSnapshot source) => source.PackingRotation == 0
		&& source.UvTransform is not null && source.LocalTriangles.Count > 0
		&& (source.TextureRect.X != MathF.Round(source.TextureRect.X)
			|| source.TextureRect.Y != MathF.Round(source.TextureRect.Y));

	private static Vector2 Map(Vector2 uv, Vector2 localPosition, SpriteSourceSnapshot source, bool implicitUv = false)
	{
		// UVs are continuous edge coordinates, not pixel indices: reflection uses width-x,
		// not width-1-x. Both source and target use Unity's bottom-left convention.
		Vector2 pixel = implicitUv
			? localPosition * source.PixelsToUnits + source.Pivot * new Vector2(source.Rect.Width, source.Rect.Height) - source.TextureRectOffset
			: uv * new Vector2(source.Texture!.Width_C28, source.Texture.Height_C28) - new Vector2(source.TextureRect.X, source.TextureRect.Y);
		// Serialized mesh UVs can lie on the enclosing texel edges, outside a
		// fractional tight bound. Verify against the copied pixel support AND
		// independently against geometry below, without widening the epsilon.
		if (!IsFinite(pixel)
			|| pixel.X < MathF.Floor(source.TextureRect.X) - source.TextureRect.X - Tolerance
			|| pixel.Y < MathF.Floor(source.TextureRect.Y) - source.TextureRect.Y - Tolerance
			|| pixel.X > MathF.Ceiling(source.TextureRect.Right) - source.TextureRect.X + Tolerance
			|| pixel.Y > MathF.Ceiling(source.TextureRect.Bottom) - source.TextureRect.Y + Tolerance)
			throw new NotSupportedException("UV points outside the sampled textureRect");
		if (source.PackingRotation is 1 or 3) pixel.X = source.TextureRect.Width - pixel.X;
		if (source.PackingRotation is 2 or 3) pixel.Y = source.TextureRect.Height - pixel.Y;
		Vector2 logical = pixel + source.TextureRectOffset;
		Vector2 fromGeometry = localPosition * source.PixelsToUnits
			+ source.Pivot * new Vector2(source.Rect.Width, source.Rect.Height);
		if (!IsFinite(fromGeometry) || MathF.Abs(logical.X - fromGeometry.X) > Tolerance
			|| MathF.Abs(logical.Y - fromGeometry.Y) > Tolerance)
			throw new NotSupportedException("explicit UVs disagree with the local geometry and packing mapping");
		return logical / new Vector2(source.Rect.Width, source.Rect.Height);
	}

	private static bool TryValidateTransform(SpriteSourceSnapshot source, out string? reason)
	{
		reason = null;
		if (!IsFinite(source.Pivot) || !float.IsFinite(source.PixelsToUnits) || source.PixelsToUnits <= 0)
		{
			reason = "invalid pivot or pixels-per-unit for UV mapping";
			return false;
		}
		if (source.UvTransform is not Vector4 uv) return true;
		// Stored uvTransform is the pre-rotation affine evidenced by #525 and TightTwo.
		// packingRotation is a later vertex UV operation, not a negative stored scale.
		// FlipHorizontal may therefore store a positive X scale such as TightTwo's
		// (64, 34.96, 64, 55.56). Do not put the native post-transform mirror formula
		// into this allow path; integer-grid tests may still use the older baked-flip
		// stored encoding, which IsAccepted also recognizes.
		if (!SpriteStoredUvTransform.IsAccepted(
			uv,
			source.PackingRotation,
			source.PixelsToUnits,
			source.Pivot,
			source.Rect,
			source.TextureRect,
			source.TextureRectOffset,
			Tolerance))
		{
			reason = "uvTransform does not match the supported axis-aligned pixel mapping";
			return false;
		}
		return true;
	}

	private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
	private static float ReadFloat(ReadOnlySpan<byte> data, bool bigEndian) => bigEndian
		? BinaryPrimitives.ReadSingleBigEndian(data) : BinaryPrimitives.ReadSingleLittleEndian(data);
	private static void WriteFloat(Span<byte> data, float value, bool bigEndian)
	{
		if (bigEndian) BinaryPrimitives.WriteSingleBigEndian(data, value);
		else BinaryPrimitives.WriteSingleLittleEndian(data, value);
	}
}
