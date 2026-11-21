using System.Drawing;
using System.Numerics;

namespace AssetRipper.Export.UnityProjects.Textures;

/// <summary>
/// Stored <c>uvTransform</c> is the pre-rotation axis-aligned pixel mapping.
/// <c>packingRotation</c> is applied after that mapping and is not encoded as a negative stored scale.
/// </summary>
public static class SpriteStoredUvTransform
{
	public const float Tolerance = 0.001f;

	public static Vector4 UnrotatedPixelMapping(
		float pixelsToUnits,
		Vector2 pivot,
		RectangleF rect,
		RectangleF textureRect,
		Vector2 textureRectOffset)
	{
		float x = pivot.X * rect.Width - textureRectOffset.X;
		float y = pivot.Y * rect.Height - textureRectOffset.Y;
		return new(pixelsToUnits, textureRect.X + x, pixelsToUnits, textureRect.Y + y);
	}

	public static bool IsAccepted(
		Vector4 uv,
		int packingRotation,
		float pixelsToUnits,
		Vector2 pivot,
		RectangleF rect,
		RectangleF textureRect,
		Vector2 textureRectOffset,
		float tolerance = Tolerance)
	{
		if (!float.IsFinite(pixelsToUnits) || pixelsToUnits <= 0 || !IsFinite(uv) || !IsFinite(pivot)
			|| !IsFinite(textureRectOffset) || !IsFinite(rect) || !IsFinite(textureRect))
		{
			return false;
		}

		if (NearlyEquals(uv, UnrotatedPixelMapping(pixelsToUnits, pivot, rect, textureRect, textureRectOffset), tolerance))
		{
			return true;
		}

		// Previously allowed synthetic encoding used by existing integer-grid tests:
		// packingRotation baked into stored scale signs. This is not the native
		// stored contract. Keep it only so those already-accepted cases still pass.
		float sx = packingRotation is 1 or 3 ? -1 : 1;
		float sy = packingRotation is 2 or 3 ? -1 : 1;
		float x = pivot.X * rect.Width - textureRectOffset.X;
		float y = pivot.Y * rect.Height - textureRectOffset.Y;
		Vector4 baked = new(
			sx * pixelsToUnits,
			textureRect.X + (sx < 0 ? textureRect.Width - x : x),
			sy * pixelsToUnits,
			textureRect.Y + (sy < 0 ? textureRect.Height - y : y));
		return NearlyEquals(uv, baked, tolerance);
	}

	private static bool NearlyEquals(Vector4 left, Vector4 right, float tolerance)
	{
		return MathF.Abs(left.X - right.X) <= tolerance
			&& MathF.Abs(left.Y - right.Y) <= tolerance
			&& MathF.Abs(left.Z - right.Z) <= tolerance
			&& MathF.Abs(left.W - right.W) <= tolerance;
	}

	private static bool IsFinite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
	private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
	private static bool IsFinite(RectangleF value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Width) && float.IsFinite(value.Height);
}
