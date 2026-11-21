using System.Numerics;

namespace AssetRipper.Export.Modules.Textures;

/// <summary>
/// Offline reconstruction of Unity's <c>Sprite::RecalculateUVs</c> using scalar IEEE-754
/// binary32 operations in the native instruction order (no FMA, no algebraic rewrite).
/// Observed Unity API / Preview UVs are never used as input.
/// </summary>
public static class SpriteNativeUv
{
	public const int RotationNone = SpriteUnpacker.RotationNone;
	public const int RotationFlipHorizontal = SpriteUnpacker.RotationFlipHorizontal;
	public const int RotationFlipVertical = SpriteUnpacker.RotationFlipVertical;
	public const int RotationRotate180 = SpriteUnpacker.RotationRotate180;

	public static int PackingRotation(uint settingsRaw) => (int)(settingsRaw >> 2 & 0xF);

	/// <summary>
	/// Convert a local-space mesh vertex to logical-canvas pixels, origin at the
	/// sprite rect's bottom-left, y-up. This matches Preview placement
	/// <c>(v * (rect.width / bounds.size.x) + pivotPixels)</c> when bounds come from
	/// <c>GetBounds(false,false,false)</c>, i.e. <c>num3 = pixelsToUnits</c>.
	/// </summary>
	public static Vector2 ToCanvasPixel(Vector2 localPosition, float pixelsToUnits, Vector2 pivotPixels)
	{
		return new(
			Add(Mul(ToF32(localPosition.X), ToF32(pixelsToUnits)), ToF32(pivotPixels.X)),
			Add(Mul(ToF32(localPosition.Y), ToF32(pixelsToUnits)), ToF32(pivotPixels.Y)));
	}

	public static Vector2 PivotPixels(float rectWidth, float rectHeight, Vector2 pivotNormalized)
	{
		return new(Mul(ToF32(rectWidth), ToF32(pivotNormalized.X)), Mul(ToF32(rectHeight), ToF32(pivotNormalized.Y)));
	}

	/// <summary>
	/// Compute packed UVs for every local-space vertex. Rotations other than
	/// None / FlipHorizontal / FlipVertical / Rotate180 match native behavior:
	/// the function refuses to write (rotation &gt; 3 leaves UVs untouched in Unity).
	/// The rasterizer entry point further restricts the first version to evidenced
	/// None and FlipHorizontal only.
	/// </summary>
	public static bool TryRecalculate(
		ReadOnlySpan<Vector2> localPositions,
		int atlasWidth,
		int atlasHeight,
		float rectWidth,
		float rectHeight,
		Vector2 pivotNormalized,
		float textureRectX,
		float textureRectY,
		float textureRectWidth,
		float textureRectHeight,
		Vector2 textureRectOffset,
		Vector4 uvTransform,
		float downscaleMultiplier,
		int packingRotation,
		Span<Vector2> destination,
		[NotNullWhen(false)] out string? reason)
	{
		if (localPositions.Length != destination.Length)
		{
			reason = "destination UV span length must match the vertex count";
			return false;
		}

		if (atlasWidth <= 0 || atlasHeight <= 0)
		{
			reason = $"atlas size is invalid ({atlasWidth}x{atlasHeight})";
			return false;
		}

		if (!IsFinite(rectWidth) || !IsFinite(rectHeight) || !IsFinite(pivotNormalized.X) || !IsFinite(pivotNormalized.Y)
			|| !IsFinite(textureRectX) || !IsFinite(textureRectY) || !IsFinite(textureRectWidth) || !IsFinite(textureRectHeight)
			|| !IsFinite(textureRectOffset.X) || !IsFinite(textureRectOffset.Y)
			|| !IsFinite(uvTransform.X) || !IsFinite(uvTransform.Y) || !IsFinite(uvTransform.Z) || !IsFinite(uvTransform.W)
			|| !IsFinite(downscaleMultiplier))
		{
			reason = "native UV inputs contain a non-finite value";
			return false;
		}

		for (int i = 0; i < localPositions.Length; i++)
		{
			if (!IsFinite(localPositions[i].X) || !IsFinite(localPositions[i].Y))
			{
				reason = $"vertex {i} is non-finite";
				return false;
			}
		}

		if (packingRotation is < 0 or > 3)
		{
			reason = $"packing rotation {packingRotation} is not written by native RecalculateUVs";
			return false;
		}

		float tx = ToF32(uvTransform.X);
		float ty = ToF32(uvTransform.Y);
		float tz = ToF32(uvTransform.Z);
		float tw = ToF32(uvTransform.W);
		float rw = ToF32(rectWidth);
		float rh = ToF32(rectHeight);
		float px = ToF32(pivotNormalized.X);
		float py = ToF32(pivotNormalized.Y);
		float trW = ToF32(textureRectWidth);
		float trH = ToF32(textureRectHeight);
		float ox = ToF32(textureRectOffset.X);
		float oy = ToF32(textureRectOffset.Y);
		float W = ToF32(atlasWidth);
		float H = ToF32(atlasHeight);
		float d = ToF32(downscaleMultiplier);

		// Instruction-order intermediates (libunity RecalculateUVs).
		float pivotPx = Mul(rw, px);
		float pivotPy = Mul(rh, py);
		float bX = Add(Add(Sub(ty, pivotPx), trW), ox);
		float bY = Add(Add(Sub(tw, pivotPy), trH), oy);
		float s4 = Div(bX, W);
		float s5 = Div(bY, H);
		float s6 = Div(Sub(bX, trW), W);
		float s7 = Div(Sub(bY, trH), H);

		for (int i = 0; i < localPositions.Length; i++)
		{
			float vx = ToF32(localPositions[i].X);
			float vy = ToF32(localPositions[i].Y);
			float u0 = Div(Add(Mul(vx, tx), ty), W);
			float v0 = Div(Add(Mul(vy, tz), tw), H);
			float u;
			float v;
			switch (packingRotation)
			{
				case RotationNone:
					u = Mul(u0, d);
					v = Mul(v0, d);
					break;
				case RotationFlipHorizontal:
					u = Mul(Add(Sub(s4, u0), s6), d);
					v = Mul(v0, d);
					break;
				case RotationFlipVertical:
					u = Mul(u0, d);
					v = Mul(Add(Sub(s5, v0), s7), d);
					break;
				case RotationRotate180:
					u = Mul(Add(Sub(s4, u0), s6), d);
					v = Mul(Add(Sub(s5, v0), s7), d);
					break;
				default:
					reason = $"packing rotation {packingRotation} is not written by native RecalculateUVs";
					return false;
			}

			if (!IsFinite(u) || !IsFinite(v))
			{
				reason = $"native UV for vertex {i} is non-finite";
				return false;
			}

			destination[i] = new Vector2(u, v);
		}

		reason = null;
		return true;
	}

	private static float ToF32(float value) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value));
	private static float Add(float left, float right) => ToF32(ToF32(left) + ToF32(right));
	private static float Sub(float left, float right) => ToF32(ToF32(left) - ToF32(right));
	private static float Mul(float left, float right) => ToF32(ToF32(left) * ToF32(right));
	private static float Div(float left, float right) => ToF32(ToF32(left) / ToF32(right));
	private static bool IsFinite(float value) => float.IsFinite(value);
}
