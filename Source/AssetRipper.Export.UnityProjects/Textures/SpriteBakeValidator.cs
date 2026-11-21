using AssetRipper.Export.Modules.Textures;
using AssetRipper.Primitives;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>
/// Support boundary for offline FullRect baking. FlipHorizontal is handled by
/// native RecalculateUVs; this is not the YamlUnpacked integer-phase unpack guard.
/// Non-zero atlasRectOffset is allowed only on the evidenced 2022.3 packed RD path.
/// </summary>
internal static class SpriteBakeValidator
{
	private const float MappingTolerance = 0.001f;

	public static bool TryValidate(SpriteSourceSnapshot snapshot, ISprite sprite, [NotNullWhen(false)] out string? reason)
	{
		if (snapshot.HasSpriteInstanceDataBranch)
		{
			reason = "the SpriteInstanceData render branch is not supported";
			return false;
		}

		if (snapshot.HasAtlasBinding && (!snapshot.IsAtlasBindingResolved || !snapshot.HasAtlasRenderDataMatch))
		{
			reason = "the original atlas binding could not be resolved or its render-data key did not match";
			return false;
		}

		if (snapshot.HasConflictingAtlasCandidates)
		{
			reason = "multiple conflicting atlas candidates were found";
			return false;
		}

		if (!snapshot.HasTextureReference || !snapshot.IsTextureResolved)
		{
			reason = snapshot.HasTextureReference
				? "the source texture reference could not be resolved"
				: "the source texture reference is null";
			return false;
		}

		if (!SpriteUnpackNumericGuard.IsSafe(snapshot))
		{
			reason = "unsafe numeric values, dimensions, or unpacking pixel budget";
			return false;
		}

		ITexture2D source = snapshot.Texture!;
		if (!SpriteTextureSemantics.TryDescribe(source, out reason))
		{
			return false;
		}

		if (snapshot.HasAlphaTextureReference && !snapshot.IsAlphaTextureResolved)
		{
			reason = "the source alpha texture reference could not be resolved";
			return false;
		}

		if (snapshot.HasAlphaTextureReference || snapshot.IsAlphaTextureResolved)
		{
			reason = "a split alpha texture is not supported";
			return false;
		}

		if (snapshot.HasSecondaryTextures)
		{
			reason = "secondary textures are not supported";
			return false;
		}

		if (snapshot.MeshFailureReason is { } meshFailure)
		{
			reason = meshFailure;
			return false;
		}

		if (snapshot.LocalTriangles.Count == 0)
		{
			// Ordinary FullRect / rectangle sheets often have no stored mesh.
			// Tight packing without a mesh cannot invent a silhouette.
			if (snapshot.MeshType != 0)
			{
				reason = "the mesh has no triangles";
				return false;
			}
		}

		if (snapshot.HasBones)
		{
			reason = "skinned sprite bones are not supported";
			return false;
		}

		if (snapshot.HasBlendShapes)
		{
			reason = "sprite blend shapes are not supported";
			return false;
		}

		if (snapshot.HasSourceSkin)
		{
			reason = "sprite source skin data is not supported";
			return false;
		}

		if (snapshot.HasUsableAtlasRD)
		{
			reason = "an alternate AtlasRD render branch is not supported";
			return false;
		}

		if (snapshot.DownscaleMultiplier is float downscale && MathF.Abs(downscale - 1f) > MappingTolerance)
		{
			reason = $"downscaleMultiplier {downscale} is not a verified 1:1 mapping";
			return false;
		}

		if (snapshot.UvTransform is null)
		{
			reason = "uvTransform is missing";
			return false;
		}

		if (!TryInspectVertexColors(sprite, out bool hasVertexColors, out reason))
		{
			return false;
		}

		if (hasVertexColors)
		{
			reason = "vertex colors are not supported";
			return false;
		}

		if (!TryValidateAtlasRectOffset(snapshot, sprite.Collection.Version, out reason))
		{
			return false;
		}

		if ((snapshot.SettingsRaw & ~0x7Fu) != 0)
		{
			reason = "SettingsRaw contains unknown packing protocol bits";
			return false;
		}

		int rotation = snapshot.PackingRotation;
		if (rotation is not (SpriteNativeUv.RotationNone or SpriteNativeUv.RotationFlipHorizontal))
		{
			reason = rotation is SpriteNativeUv.RotationFlipVertical or SpriteNativeUv.RotationRotate180
				? $"packing rotation {rotation} is implemented in native UV math but is not an evidenced raster mode in this version"
				: $"packing rotation {rotation} is not supported";
			return false;
		}

		reason = null;
		return true;
	}

	private static bool TryValidateAtlasRectOffset(SpriteSourceSnapshot snapshot, UnityVersion version, [NotNullWhen(false)] out string? reason)
	{
		reason = null;
		if (snapshot.AtlasRectOffset is not Vector2 atlasOffset)
		{
			return true;
		}

		if (MathF.Abs(atlasOffset.X) <= MappingTolerance && MathF.Abs(atlasOffset.Y) <= MappingTolerance)
		{
			return true;
		}

		// Unity 2022.3 serialized non-packed sheet RD carries (-1,-1), even
		// though no atlas is bound. Accept only this observed sentinel.
		if (!snapshot.HasAtlasBinding && !snapshot.IsPacked && atlasOffset == new Vector2(-1))
		{
			return true;
		}

		// RecalculateUVs does not consume atlasRectOffset (evidenced on 2022.3.57f1).
		// Allow the field to ride along only on that packed typeless/legacy RD path.
		// This is not a claim that the field is fully supported on other versions.
		bool evidencedPackedRd = snapshot.HasAtlasBinding
			&& snapshot.IsPacked
			&& snapshot.IsAtlasBindingResolved
			&& snapshot.HasAtlasRenderDataMatch
			&& !snapshot.HasUsableAtlasRD
			&& snapshot.PackingRotation is SpriteNativeUv.RotationNone or SpriteNativeUv.RotationFlipHorizontal
			&& snapshot.UvTransform is not null
			&& snapshot.LocalTriangles.Count > 0
			&& snapshot.MeshFailureReason is null
			&& version.Major == 2022
			&& version.Minor == 3;
		if (evidencedPackedRd)
		{
			return true;
		}

		reason = $"atlasRectOffset ({atlasOffset.X}, {atlasOffset.Y}) has unverified version or render-data semantics; allowed only on Unity 2022.3 packed typeless/legacy RD with a matching atlas binding. RecalculateUVs does not consume this field.";
		return false;
	}

	private static bool TryInspectVertexColors(ISprite sprite, out bool hasVertexColors, [NotNullWhen(false)] out string? reason)
	{
		hasVertexColors = false;
		if (!sprite.RD.Has_VertexData() || sprite.RD.VertexData is null || sprite.RD.VertexData.VertexCount == 0)
		{
			reason = null;
			return true;
		}

		if (!sprite.RD.VertexData.Has_Channels())
		{
			reason = null;
			return true;
		}

		try
		{
			var channel = sprite.RD.VertexData.GetChannel(sprite.Collection.Version, ShaderChannel.Color);
			hasVertexColors = channel.IsSet() && channel.GetDataDimension() > 0;
			reason = null;
			return true;
		}
		catch (Exception)
		{
			reason = "vertex color channels could not be inspected";
			return false;
		}
	}
}
