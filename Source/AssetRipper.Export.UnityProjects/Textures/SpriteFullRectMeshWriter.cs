using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using System.Numerics;
using System.Text;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>Replace packed/tight mesh data with a FullRect quad and explicit 0-1 UVs.</summary>
internal static class SpriteFullRectMeshWriter
{
	public static bool TryWrite(ISprite sprite, float width, float height, float pixelsToUnits, Vector2 pivot, [NotNullWhen(false)] out string? reason)
	{
		if (!float.IsFinite(width) || width <= 0 || !float.IsFinite(height) || height <= 0
			|| !float.IsFinite(pixelsToUnits) || pixelsToUnits == 0f
			|| !float.IsFinite(pivot.X) || !float.IsFinite(pivot.Y))
		{
			reason = "FullRect mesh inputs are not finite, positive sizes";
			return false;
		}

		ISpriteRenderData rd = sprite.RD;
		if (!rd.Has_VertexData() || rd.VertexData is null)
		{
			reason = "the sprite has no VertexData channel layout to receive a FullRect mesh";
			return false;
		}

		float pivotPx = pivot.X * width;
		float pivotPy = pivot.Y * height;
		float minX = (0f - pivotPx) / pixelsToUnits;
		float maxX = (width - pivotPx) / pixelsToUnits;
		float minY = (0f - pivotPy) / pixelsToUnits;
		float maxY = (height - pivotPy) / pixelsToUnits;
		Vector2[] positions =
		[
			new(minX, minY),
			new(maxX, minY),
			new(maxX, maxY),
			new(minX, maxY),
		];
		Vector2[] uvs =
		[
			new(0f, 0f),
			new(1f, 0f),
			new(1f, 1f),
			new(0f, 1f),
		];
		ushort[] indices = [0, 1, 2, 0, 2, 3];

		var vertexData = rd.VertexData;
		if (vertexData.Has_Channels())
		{
			vertexData.Channels!.Clear();
		}

		for (int i = 0; i < ShaderChannelExtensions.GetChannelCount(sprite.Collection.Version); i++)
		{
			vertexData.Channels!.AddNew();
		}

		vertexData.Channels![0].Dimension = 3;
		int uvIndex = ShaderChannel.UV0.ToChannel(sprite.Collection.Version);
		vertexData.Channels[uvIndex].Dimension = 2;
		vertexData.Channels[uvIndex].Offset = 12;
		vertexData.VertexCount = 4;
		if (vertexData.Has_CurrentChannels_Int32() || vertexData.Has_CurrentChannels_UInt32())
		{
			vertexData.CurrentChannels = (1 << 0) | (1 << uvIndex);
		}

		using MemoryStream data = new();
		using (BinaryWriter writer = new(data, Encoding.UTF8, leaveOpen: true))
		{
			for (int i = 0; i < positions.Length; i++)
			{
				writer.Write(positions[i].X);
				writer.Write(positions[i].Y);
				writer.Write(0f);
				writer.Write(uvs[i].X);
				writer.Write(uvs[i].Y);
			}
		}

		vertexData.Data = data.ToArray();
		if (rd.Has_SubMeshes())
		{
			rd.SubMeshes!.Clear();
			var submesh = rd.SubMeshes.AddNew();
			submesh.VertexCount = 4;
			submesh.IndexCount = (uint)indices.Length;
			submesh.FirstVertex = 0;
			submesh.FirstByte = 0;
			if (submesh.Has_BaseVertex())
			{
				submesh.BaseVertex = 0;
			}
		}

		using MemoryStream indexStream = new();
		using (BinaryWriter writer = new(indexStream, Encoding.UTF8, leaveOpen: true))
		{
			foreach (ushort index in indices)
			{
				writer.Write(index);
			}
		}

		rd.IndexBuffer = indexStream.ToArray();
		if (rd.Has_Vertices())
		{
			rd.Vertices.Clear();
		}

		if (rd.Has_Indices() && rd.Indices is not null)
		{
			rd.Indices.Clear();
		}

		reason = null;
		return true;
	}
}
