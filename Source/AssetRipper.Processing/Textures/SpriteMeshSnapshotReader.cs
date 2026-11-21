using AssetRipper.IO.Endian;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using System.Buffers.Binary;
using System.Numerics;

namespace AssetRipper.Processing.Textures;

/// <summary>Feature-local, all-or-nothing triangle decoding. Never silently drops a submesh.</summary>
internal static class SpriteMeshSnapshotReader
{
	public static IReadOnlyList<SpriteMeshTriangle> Read(ISprite sprite, out string? reason)
	{
		reason = null;
		var rd = sprite.RD;
		List<SpriteMeshTriangle> triangles = [];
		try
		{
			if (rd.Has_VertexData()) SpriteVertexDataValidator.Validate(sprite);
			if (rd.Has_VertexData() && rd.VertexData.VertexCount > 0 && rd.SubMeshes.Count > 0)
			{
				var data = rd.VertexData;
				var version = sprite.Collection.Version;
				if (sprite.Collection.EndianType != EndianType.LittleEndian || version.LessThan(5, 6)
					|| data.Has_Streams() || data.Has_Streams_0_() || !data.Has_Channels())
					throw new NotSupportedException("unverified vertex-buffer version or endianness");
				var channel = data.GetChannel(version, ShaderChannel.Vertex);
				if (!channel.IsSet() || channel.Format != 0 || channel.GetDataDimension() != 3)
					throw new NotSupportedException("mesh positions require float3");
				int stride = data.GetStreamStride(version, channel.Stream);
				int start = checked(data.GetStreamOffset(version, channel.Stream) + channel.Offset);
				if (stride < channel.Offset + 12) throw new NotSupportedException("overlapping position layout");
				foreach (var mesh in rd.SubMeshes)
				{
					if ((mesh.Has_Topology() && (int)mesh.TopologyE != 0) || mesh.IndexCount % 3 != 0)
						throw new NotSupportedException("mesh topology is not a complete triangle list");
					ulong endVertex = (ulong)mesh.FirstVertex + mesh.VertexCount;
					ulong endByte = (ulong)mesh.FirstByte + (ulong)mesh.IndexCount * 2;
					if (endVertex > data.VertexCount || endByte > (ulong)rd.IndexBuffer.Length || mesh.FirstByte % 2 != 0)
						throw new NotSupportedException("mesh vertex/index range is invalid");
					// The legacy RD schema has no IndexFormat: indices are UInt16. The
					// newer SpriteInstanceData with an explicit format is separately refused.
					for (uint i = 0; i < mesh.IndexCount; i += 3)
					{
						triangles.Add(new(ReadVertex(i), ReadVertex(i + 1), ReadVertex(i + 2)));
					}

					Vector2 ReadVertex(uint index)
					{
						int offset = checked((int)((ulong)mesh.FirstByte + index * 2));
						ulong absolute = BinaryPrimitives.ReadUInt16LittleEndian(rd.IndexBuffer.AsSpan(offset, 2));
						if (mesh.Has_BaseVertex()) absolute += mesh.BaseVertex;
						if (absolute < mesh.FirstVertex || absolute >= endVertex)
							throw new NotSupportedException("mesh index is outside its declared vertex range");
						int position = checked(start + checked((int)absolute * stride));
						ReadOnlySpan<byte> bytes = data.Data.AsSpan(position, 12);
						if (!float.IsFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes[8..])))
							throw new NotSupportedException("non-finite numeric vertex Z");
						return new(BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]));
					}
				}
			}
			else if (rd.Has_Vertices() && rd.Vertices.Count > 0 && rd.Indices is not null)
			{
				if (rd.Indices.Count % 3 != 0) throw new NotSupportedException("incomplete legacy triangle list");
				for (int i = 0; i < rd.Indices.Count; i += 3)
				{
					triangles.Add(new(ReadVertex(i), ReadVertex(i + 1), ReadVertex(i + 2)));
				}
				Vector2 ReadVertex(int index)
				{
					int vertex = rd.Indices[index];
					if ((uint)vertex >= (uint)rd.Vertices.Count) throw new NotSupportedException("invalid legacy triangle index");
					var position = rd.Vertices[vertex].Pos;
					return new(position.X, position.Y);
				}
			}
			return triangles.AsReadOnly();
		}
		catch (Exception exception)
		{
			reason = $"mesh decoding failed: {exception.Message}";
			return [];
		}
	}
}
