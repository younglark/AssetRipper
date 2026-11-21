using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_687078895;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.SpriteAtlasData;
using AssetRipper.SourceGenerated.Subclasses.SpriteRenderData;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace AssetRipper.Processing.Textures;

public readonly record struct SpriteMeshTriangle(Vector2 A, Vector2 B, Vector2 C);

/// <summary>
/// Immutable Sprite definition captured before <see cref="SpriteProcessor"/> overwrites coordinates.
/// </summary>
public sealed class SpriteSourceSnapshot
{
	public required RectangleF Rect { get; init; }
	public required Vector2 Pivot { get; init; }
	public required Vector4 Border { get; init; }
	public required Vector2 Offset { get; init; }
	public required float PixelsToUnits { get; init; }
	public required uint Extrude { get; init; }
	public required RectangleF TextureRect { get; init; }
	public required Vector2 TextureRectOffset { get; init; }
	public Vector2? AtlasRectOffset { get; init; }
	public required uint SettingsRaw { get; init; }
	public Vector4? UvTransform { get; init; }
	public float? DownscaleMultiplier { get; init; }
	public ITexture2D? Texture { get; init; }
	public ITexture2D? AlphaTexture { get; init; }
	// A non-null serialized PPtr may fail resolution. Preserve that distinction
	// before SpriteProcessor's pointer conversion can replace it with null.
	public required bool HasTextureReference { get; init; }
	public required bool HasAlphaTextureReference { get; init; }
	public bool IsTextureResolved => Texture is not null;
	public bool IsAlphaTextureResolved => AlphaTexture is not null;
	public required bool HasSecondaryTextures { get; init; }
	public required bool HasBones { get; init; }
	public required bool HasBlendShapes { get; init; }
	public required bool HasSourceSkin { get; init; }
	public required bool HasUsableAtlasRD { get; init; }
	public required bool HasConflictingAtlasCandidates { get; init; }
	public required IReadOnlyList<SpriteMeshTriangle> LocalTriangles { get; init; }
	public string? MeshFailureReason { get; init; }
	public bool HasNonFiniteValues { get; init; }
	public bool HasSpriteInstanceDataBranch { get; init; }
	public bool HasAtlasBinding { get; init; }
	public bool IsAtlasBindingResolved { get; init; }
	public bool HasAtlasRenderDataMatch { get; init; }

	public bool IsPacked => (SettingsRaw & 1) != 0;
	public int PackingMode => (int)(SettingsRaw >> 1 & 1);
	public int PackingRotation => (int)(SettingsRaw >> 2 & 0xF);
	public int MeshType => (int)(SettingsRaw >> 6 & 0x1);
}

public static class SpriteSourceSnapshotStore
{
	private static readonly ConditionalWeakTable<ISprite, SpriteSourceSnapshot> Table = new();

	public static void Set(ISprite sprite, SpriteSourceSnapshot snapshot)
	{
		Table.Remove(sprite);
		Table.Add(sprite, snapshot);
	}

	public static bool TryGet(ISprite sprite, [NotNullWhen(true)] out SpriteSourceSnapshot? snapshot)
	{
		return Table.TryGetValue(sprite, out snapshot);
	}

}

internal sealed class SpriteSourceSnapshotCollector
{
	private readonly Dictionary<ISprite, RawSprite> sprites = new();
	private readonly Dictionary<ISprite, List<AtlasCandidate>> atlasCandidates = new();

	public void CaptureSprite(ISprite sprite)
	{
		RawSprite raw = RawSprite.From(sprite);
		sprites[sprite] = raw;
		if (sprite.SpriteAtlasP is { } atlas
			&& sprite.Has_RenderDataKey()
			&& atlas.RenderDataMap.TryGetValue(sprite.RenderDataKey, out ISpriteAtlasData? atlasData))
		{
			AddCandidate(sprite, atlas, atlasData);
		}
	}

	public void CaptureAtlas(ISpriteAtlas atlas)
	{
		foreach (ISprite? packed in atlas.PackedSpritesP)
		{
			if (packed is null)
			{
				continue;
			}

			if (packed.Has_RenderDataKey() && atlas.RenderDataMap.TryGetValue(packed.RenderDataKey, out ISpriteAtlasData? atlasData))
			{
				AddCandidate(packed, atlas, atlasData);
			}
		}
	}

	public void StoreResolvedSnapshots()
	{
		HashSet<ISprite> all = new(sprites.Keys);
		foreach (ISprite sprite in atlasCandidates.Keys)
		{
			all.Add(sprite);
		}

		foreach (ISprite sprite in all)
		{
			if (!sprites.TryGetValue(sprite, out RawSprite raw))
			{
				raw = RawSprite.From(sprite);
			}

			List<AtlasCandidate>? candidates = atlasCandidates.GetValueOrDefault(sprite);
			bool conflict = false;
			AtlasCandidate? chosen = null;
			if (candidates is { Count: > 0 })
			{
				chosen = candidates[0];
				for (int i = 1; i < candidates.Count; i++)
				{
					if (chosen is not { } selected || !candidates[i].SameLayout(selected))
					{
						conflict = true;
						chosen = null;
						break;
					}
				}

				if (!conflict)
				{
					chosen = candidates[0];
				}
			}

			SpriteSourceSnapshot snapshot = raw.ToSnapshot(chosen, conflict);
			SpriteSourceSnapshotStore.Set(sprite, snapshot);
		}
	}

	private void AddCandidate(ISprite sprite, ISpriteAtlas atlas, ISpriteAtlasData data)
	{
		if (!atlasCandidates.TryGetValue(sprite, out List<AtlasCandidate>? list))
		{
			list = [];
			atlasCandidates[sprite] = list;
		}

		list.Add(AtlasCandidate.From(atlas, data));
	}

	private readonly record struct AtlasCandidate(
		ITexture2D? Texture,
		ITexture2D? AlphaTexture,
		RectangleF TextureRect,
		Vector2 TextureRectOffset,
		Vector2? AtlasRectOffset,
		uint SettingsRaw,
		Vector4? UvTransform,
		float? DownscaleMultiplier,
		bool HasSecondaryTextures,
		bool HasTextureReference,
		bool HasAlphaTextureReference,
		bool HasSpriteInstanceDataBranch,
		bool HasNonFiniteValues)
	{
		public static AtlasCandidate From(ISpriteAtlas atlas, ISpriteAtlasData data)
		{
			ITexture2D? texture = data.Texture.TryGetAsset(atlas.Collection);
			ITexture2D? alpha = data.AlphaTexture.TryGetAsset(atlas.Collection);
			Vector2? atlasRectOffset = data.Has_AtlasRectOffset()
				? new Vector2(data.AtlasRectOffset.X, data.AtlasRectOffset.Y)
				: null;
			Vector4? uv = new Vector4(data.UvTransform.X, data.UvTransform.Y, data.UvTransform.Z, data.UvTransform.W);
			float downscale = data.DownscaleMultiplier;
			bool secondary = data.Has_SecondaryTextures() && data.SecondaryTextures.Count > 0;
			return new(
				texture,
				alpha,
				data.TextureRect.CastToStruct(),
				(Vector2)data.TextureRectOffset,
				atlasRectOffset,
				data.SettingsRaw,
				uv,
				downscale,
				secondary,
				!data.Texture.IsNull(),
				!data.AlphaTexture.IsNull(),
				data.Has__002AspriteInstanceData(),
				SpriteSourceValueValidator.HasNonFiniteValues(data));
		}

		public bool SameLayout(AtlasCandidate other)
		{
			return ReferenceEquals(Texture, other.Texture)
				&& ReferenceEquals(AlphaTexture, other.AlphaTexture)
				&& TextureRect == other.TextureRect
				&& TextureRectOffset == other.TextureRectOffset
				&& NullableEquals(AtlasRectOffset, other.AtlasRectOffset)
				&& SettingsRaw == other.SettingsRaw
				&& NullableEquals(UvTransform, other.UvTransform)
				&& NullableEquals(DownscaleMultiplier, other.DownscaleMultiplier)
				&& HasSecondaryTextures == other.HasSecondaryTextures
				&& HasTextureReference == other.HasTextureReference
				&& HasAlphaTextureReference == other.HasAlphaTextureReference
				&& HasSpriteInstanceDataBranch == other.HasSpriteInstanceDataBranch
				&& HasNonFiniteValues == other.HasNonFiniteValues;
		}

		private static bool NullableEquals<T>(T? left, T? right) where T : struct
		{
			if (left is null && right is null)
			{
				return true;
			}

			return left is not null && right is not null && left.Equals(right);
		}
	}

	private readonly record struct RawSprite(
		RectangleF Rect,
		Vector2 Pivot,
		Vector4 Border,
		Vector2 Offset,
		float PixelsToUnits,
		uint Extrude,
		RectangleF TextureRect,
		Vector2 TextureRectOffset,
		Vector2? AtlasRectOffset,
		uint SettingsRaw,
		Vector4? UvTransform,
		float? DownscaleMultiplier,
		ITexture2D? Texture,
		ITexture2D? AlphaTexture,
		bool HasSecondaryTextures,
		bool HasBones,
		bool HasBlendShapes,
		bool HasSourceSkin,
		bool HasUsableAtlasRD,
		IReadOnlyList<SpriteMeshTriangle> LocalTriangles,
		string? MeshFailureReason,
		bool HasTextureReference,
		bool HasAlphaTextureReference,
		bool HasAtlasBinding,
		bool IsAtlasBindingResolved,
		bool HasAtlasRenderDataMatch,
		bool HasNonFiniteValues)
	{
		public static RawSprite From(ISprite sprite)
		{
			ISpriteRenderData rd = sprite.RD;
			Vector2 pivot;
			if (sprite.Has_Pivot())
			{
				pivot = (Vector2)sprite.Pivot;
			}
			else
			{
				Vector2 size = sprite.Rect.Size();
				Vector2 center = new Vector2(size.X / 2.0f, size.Y / 2.0f);
				Vector2 pivotOffset = center + (Vector2)sprite.Offset;
				pivot = size.X == 0 || size.Y == 0
					? new Vector2(0.5f, 0.5f)
					: new Vector2(pivotOffset.X / size.X, pivotOffset.Y / size.Y);
			}

			Vector4 border = sprite.Has_Border() ? (Vector4)sprite.Border : default;
			Vector2? atlasRectOffset = rd.Has_AtlasRectOffset()
				? new Vector2(rd.AtlasRectOffset.X, rd.AtlasRectOffset.Y)
				: null;
			Vector4? uv = rd.Has_UvTransform()
				? new Vector4(rd.UvTransform.X, rd.UvTransform.Y, rd.UvTransform.Z, rd.UvTransform.W)
				: null;
			float? downscale = rd.Has_DownscaleMultiplier() ? rd.DownscaleMultiplier : null;
			bool secondary = rd.Has_SecondaryTextures() && rd.SecondaryTextures.Count > 0;
			bool bones = sprite.Has_Bones() && sprite.Bones.Count > 0;
			bool blendShapes = HasNonEmptyBlendShapes(rd);
			bool sourceSkin = HasNonEmptySourceSkin(rd);
			bool usableAtlasRd = HasUsableAlternateRenderData(sprite);
			ITexture2D? alpha = rd.Has_AlphaTexture() ? rd.AlphaTexture.TryGetAsset(sprite.Collection) : null;

			var triangles = SpriteMeshSnapshotReader.Read(sprite, out string? meshFailureReason);
			return new(
				sprite.Rect.CastToStruct(),
				pivot,
				border,
				(Vector2)sprite.Offset,
				sprite.PixelsToUnits,
				sprite.Extrude,
				rd.TextureRect.CastToStruct(),
				(Vector2)rd.TextureRectOffset,
				atlasRectOffset,
				rd.SettingsRaw,
				uv,
				downscale,
				rd.Texture.TryGetAsset(sprite.Collection),
				alpha,
				secondary,
				bones,
				blendShapes,
				sourceSkin,
				usableAtlasRd,
				triangles,
				meshFailureReason,
				!rd.Texture.IsNull(),
				rd.Has_AlphaTexture() && !rd.AlphaTexture.IsNull(),
				sprite.Has_SpriteAtlas() && !sprite.SpriteAtlas.IsNull(),
				sprite.SpriteAtlasP is not null,
				sprite.SpriteAtlasP is { } bound && sprite.Has_RenderDataKey() && bound.RenderDataMap.TryGetValue(sprite.RenderDataKey, out _),
				SpriteSourceValueValidator.HasNonFiniteValues(sprite));
		}

		public SpriteSourceSnapshot ToSnapshot(AtlasCandidate? atlas, bool conflict)
		{
			return new SpriteSourceSnapshot
			{
				Rect = Rect,
				Pivot = Pivot,
				Border = Border,
				Offset = Offset,
				PixelsToUnits = PixelsToUnits,
				Extrude = Extrude,
				TextureRect = atlas?.TextureRect ?? TextureRect,
				TextureRectOffset = atlas?.TextureRectOffset ?? TextureRectOffset,
				AtlasRectOffset = atlas is not null ? atlas.Value.AtlasRectOffset : AtlasRectOffset,
				SettingsRaw = atlas?.SettingsRaw ?? SettingsRaw,
				UvTransform = atlas is not null ? atlas.Value.UvTransform : UvTransform,
				DownscaleMultiplier = atlas is not null ? atlas.Value.DownscaleMultiplier : DownscaleMultiplier,
				// Choose the entire render-data source, including unresolved dependencies.
				// Atlas coordinates must never be paired with a raw RD placeholder texture.
				Texture = atlas is not null ? atlas.Value.Texture : Texture,
				AlphaTexture = atlas is not null ? atlas.Value.AlphaTexture : AlphaTexture,
				HasTextureReference = atlas is not null ? atlas.Value.HasTextureReference : HasTextureReference,
				HasAlphaTextureReference = atlas is not null ? atlas.Value.HasAlphaTextureReference : HasAlphaTextureReference,
				HasSecondaryTextures = HasSecondaryTextures || (atlas?.HasSecondaryTextures ?? false),
				HasBones = HasBones,
				HasBlendShapes = HasBlendShapes,
				HasSourceSkin = HasSourceSkin,
				HasUsableAtlasRD = HasUsableAtlasRD,
				HasConflictingAtlasCandidates = conflict,
				LocalTriangles = LocalTriangles,
				MeshFailureReason = MeshFailureReason,
				HasNonFiniteValues = HasNonFiniteValues || (atlas?.HasNonFiniteValues ?? false),
				HasSpriteInstanceDataBranch = atlas?.HasSpriteInstanceDataBranch ?? false,
				HasAtlasBinding = HasAtlasBinding,
				IsAtlasBindingResolved = IsAtlasBindingResolved,
				HasAtlasRenderDataMatch = HasAtlasRenderDataMatch,
			};
		}

		private static bool HasNonEmptyBlendShapes(ISpriteRenderData rd)
		{
			if (!rd.Has_BlendShapes())
			{
				return false;
			}

			return rd.BlendShapes.Vertices.Count > 0 || rd.BlendShapes.Shapes.Count > 0 || rd.BlendShapes.Channels.Count > 0;
		}

		private static bool HasNonEmptySourceSkin(ISpriteRenderData rd)
		{
			return rd.Has_SourceSkin() && rd.SourceSkin.Count > 0;
		}

		private static bool HasUsableAlternateRenderData(ISprite sprite)
		{
			if (!sprite.Has_AtlasRD())
			{
				return false;
			}

			try
			{
				ISpriteRenderData atlasRd = sprite.AtlasRD;
				return !atlasRd.Texture.IsNull()
					|| (atlasRd.Has_AlphaTexture() && !atlasRd.AlphaTexture.IsNull())
					|| (atlasRd.Has_SecondaryTextures() && atlasRd.SecondaryTextures.Count > 0);
			}
			catch (Exception exception) when (exception is MissingMethodException or NotSupportedException or NullReferenceException)
			{
				return false;
			}
		}

	}
}
