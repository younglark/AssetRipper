using AssetRipper.Export.Modules.Textures;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>Verified source protocol and explicit fallback boundaries for YAML unpacking.</summary>
internal static class SpriteUnpackValidator
{
	private const float MappingTolerance = 0.001f;

	public static bool TryValidate(SpriteSourceSnapshot snapshot, [NotNullWhen(false)] out string? reason)
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

		// Unity 2022.3 serialized non-packed sheet RD carries (-1,-1), even
		// though no atlas is bound. Accept only this observed sentinel, not an
		// arbitrary nonzero offset on an unbound/packed source.
		bool nonAtlasSentinel = !snapshot.HasAtlasBinding && !snapshot.IsPacked
			&& snapshot.AtlasRectOffset == new Vector2(-1);
		// Restoring the original logical canvas does not consume atlasRectOffset.
		// Limit nonzero Atlas offsets to the evidenced unrotated mesh path; the
		// pixel-grid and UV/geometry checks below still have to succeed.
		bool restoresUnrotatedAtlas = snapshot.HasAtlasBinding && snapshot.IsPacked
			&& snapshot.IsAtlasBindingResolved && snapshot.HasAtlasRenderDataMatch
			&& snapshot.PackingRotation == 0 && snapshot.UvTransform is not null
			&& snapshot.LocalTriangles.Count > 0;
		if (!nonAtlasSentinel && !restoresUnrotatedAtlas && snapshot.AtlasRectOffset is Vector2 atlasOffset && (MathF.Abs(atlasOffset.X) > MappingTolerance || MathF.Abs(atlasOffset.Y) > MappingTolerance))
		{
			reason = $"atlasRectOffset ({atlasOffset.X}, {atlasOffset.Y}) has unverified version semantics";
			return false;
		}

		if ((snapshot.SettingsRaw & ~0x7Fu) != 0)
		{
			reason = "SettingsRaw contains unknown packing protocol bits";
			return false;
		}

		int rotation = snapshot.PackingRotation;
		if (rotation is not (SpriteUnpacker.RotationNone or SpriteUnpacker.RotationFlipHorizontal or SpriteUnpacker.RotationFlipVertical or SpriteUnpacker.RotationRotate180))
		{
			reason = $"packing rotation {rotation} is not supported";
			return false;
		}

		reason = null;
		return true;
	}

}
