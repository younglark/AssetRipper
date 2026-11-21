using AssetRipper.TextureDecoder.Rgb;
using AssetRipper.TextureDecoder.Rgb.Formats;
using System.Numerics;

namespace AssetRipper.Export.Modules.Textures;

public readonly record struct SpriteUnpackRequest(
	float LogicalWidth,
	float LogicalHeight,
	float CropOffsetX,
	float CropOffsetY,
	float TextureRectX,
	float TextureRectY,
	float TextureRectWidth,
	float TextureRectHeight,
	int PackingRotation,
	bool UseTightMesh,
	IReadOnlyList<SpriteUnpackTriangle> LocalTriangles,
	float PivotX,
	float PivotY,
	float PixelsToUnits);

public readonly record struct SpriteUnpackTriangle(Vector2 A, Vector2 B, Vector2 C);

public readonly record struct SpriteUnpackPlan(
	int CanvasWidth,
	int CanvasHeight,
	float TextureRectX,
	float TextureRectY,
	float TextureRectWidth,
	float TextureRectHeight,
	float CropOffsetX,
	float CropOffsetY);

public static class SpriteUnpacker
{
	private const float IntegerTolerance = 0.001f;

	/// <summary>
	/// Official Unity packing rotations that can be inverted without a 90° size swap.
	/// Rotate90=4, Any_Old=5, and Any=15 are rejected by the caller.
	/// </summary>
	public const int RotationNone = 0;
	public const int RotationFlipHorizontal = 1;
	public const int RotationFlipVertical = 2;
	public const int RotationRotate180 = 3;

	public static bool TryUnpack(
		DirectBitmap atlas,
		SpriteUnpackRequest request,
		[NotNullWhen(true)] out DirectBitmap? canvas,
		out SpriteUnpackPlan plan,
		[NotNullWhen(false)] out string? reason)
	{
		canvas = null;
		plan = default;
		if (!TryRoundExtent(request.LogicalWidth, request.LogicalHeight, out int logicalWidth, out int logicalHeight, out reason, "logical canvas"))
		{
			return false;
		}

		if (!SpriteUnpackLimits.IsSupportedSize(logicalWidth, logicalHeight))
		{
			reason = $"logical canvas size is invalid ({logicalWidth}x{logicalHeight})";
			return false;
		}

		int rectX, rectY, rectWidth, rectHeight, cropX, cropY;
		bool fractional = !IsNearlyInteger(request.TextureRectX) || !IsNearlyInteger(request.TextureRectY)
			|| !IsNearlyInteger(request.TextureRectWidth) || !IsNearlyInteger(request.TextureRectHeight)
			|| !IsNearlyInteger(request.CropOffsetX) || !IsNearlyInteger(request.CropOffsetY);
		if (fractional)
		{
			// A tight mesh bound is a continuous geometry bound, not a texel edge.
			// Only an unrotated integer TRANSLATION maps entire texel cells 1:1.
			// Enclose the bound in source texel cells, then translate those cells;
			// never round the tight width or the destination offset independently.
			if (request.PackingRotation != RotationNone
				|| !TryRoundOffset(request.TextureRectX - request.CropOffsetX,
					request.TextureRectY - request.CropOffsetY, out int dx, out int dy, out reason, "pixel-grid translation"))
			{
				reason = "fractional bounds require a verified unrotated integer pixel-grid translation";
				return false;
			}
			if (!float.IsFinite(request.TextureRectX) || !float.IsFinite(request.TextureRectY)
				|| !float.IsFinite(request.TextureRectWidth) || !float.IsFinite(request.TextureRectHeight)
				|| request.TextureRectWidth <= 0 || request.TextureRectHeight <= 0
				|| request.TextureRectX < 0 || request.TextureRectY < 0
				|| request.TextureRectX + request.TextureRectWidth > atlas.Width
				|| request.TextureRectY + request.TextureRectHeight > atlas.Height)
			{
				reason = "fractional textureRect is outside the decoded texture";
				return false;
			}
			rectX = (int)MathF.Floor(request.TextureRectX);
			rectY = (int)MathF.Floor(request.TextureRectY);
			rectWidth = (int)MathF.Ceiling(request.TextureRectX + request.TextureRectWidth) - rectX;
			rectHeight = (int)MathF.Ceiling(request.TextureRectY + request.TextureRectHeight) - rectY;
			cropX = rectX - dx;
			cropY = rectY - dy;
		}
		else if (!TryRoundOffset(request.TextureRectX, request.TextureRectY, out rectX, out rectY, out reason, "textureRect origin")
			|| !TryRoundExtent(request.TextureRectWidth, request.TextureRectHeight, out rectWidth, out rectHeight, out reason, "textureRect size")
			|| !TryRoundOffset(request.CropOffsetX, request.CropOffsetY, out cropX, out cropY, out reason, "textureRectOffset"))
		{
			return false;
		}

		if (rectWidth <= 0 || rectHeight <= 0)
		{
			reason = $"textureRect size is invalid ({rectWidth}x{rectHeight})";
			return false;
		}

		if (atlas.IsEmpty || atlas.Width <= 0 || atlas.Height <= 0)
		{
			reason = "source texture could not be decoded";
			return false;
		}

		// Unity textureRect is bottom-left. TextureConverter output is top-left after FlipY.
		int srcX = rectX;
		int srcY = atlas.Height - (rectY + rectHeight);
		if (srcX < 0 || srcY < 0 || srcX + rectWidth > atlas.Width || srcY + rectHeight > atlas.Height)
		{
			reason = $"textureRect ({rectX}, {rectY}, {rectWidth}, {rectHeight}) is outside the decoded texture {atlas.Width}x{atlas.Height}";
			return false;
		}

		if (request.PackingRotation is not (RotationNone or RotationFlipHorizontal or RotationFlipVertical or RotationRotate180))
		{
			reason = $"packing rotation {request.PackingRotation} is not supported";
			return false;
		}

		DirectBitmap cropped = atlas.Crop(srcX..(srcX + rectWidth), srcY..(srcY + rectHeight)).DeepClone();
		ApplyInverseRotation(cropped, request.PackingRotation);
		DirectBitmap<ColorRGBA<byte>, byte> croppedRgba = cropped.ConvertToRgba32();

		int effectiveWidth = croppedRgba.Width;
		int effectiveHeight = croppedRgba.Height;
		if (cropX < 0 || cropY < 0 || cropX + effectiveWidth > logicalWidth || cropY + effectiveHeight > logicalHeight)
		{
			reason = $"trimmed region ({cropX}, {cropY}, {effectiveWidth}, {effectiveHeight}) does not fit the logical canvas {logicalWidth}x{logicalHeight}";
			return false;
		}

		if (!float.IsFinite(request.PivotX) || !float.IsFinite(request.PivotY)
			|| !float.IsFinite(request.PixelsToUnits) || request.PixelsToUnits <= 0
			|| request.LocalTriangles.Any(t => !IsSafe(t.A) || !IsSafe(t.B) || !IsSafe(t.C)))
		{
			reason = "unsafe numeric geometry for tight masking";
			return false;
		}

		bool IsSafe(Vector2 local) => float.IsFinite(local.X) && float.IsFinite(local.Y)
			&& float.IsFinite(local.X * request.PixelsToUnits + request.PivotX * logicalWidth)
			&& float.IsFinite(local.Y * request.PixelsToUnits + request.PivotY * logicalHeight);

		if (request.UseTightMesh)
		{
			if (request.LocalTriangles is not { Count: > 0 })
			{
				reason = "tight packing requires a valid triangle mesh";
				return false;
			}

			if (!TryMaskTightPixels(croppedRgba, request, cropX, cropY, out reason))
			{
				return false;
			}
		}

		DirectBitmap<ColorRGBA<byte>, byte> output = new(logicalWidth, logicalHeight);
		if (!TryCopyOntoCanvas(croppedRgba, output, cropX, cropY, logicalHeight, out reason))
		{
			return false;
		}

		canvas = output;
		plan = new SpriteUnpackPlan(logicalWidth, logicalHeight,
			request.CropOffsetX, request.CropOffsetY, request.TextureRectWidth, request.TextureRectHeight,
			request.CropOffsetX, request.CropOffsetY);
		reason = null;
		return true;
	}

	private static void ApplyInverseRotation(DirectBitmap bitmap, int rotation)
	{
		switch (rotation)
		{
			case RotationFlipHorizontal:
				bitmap.FlipX();
				break;
			case RotationFlipVertical:
				bitmap.FlipY();
				break;
			case RotationRotate180:
				bitmap.Rotate180();
				break;
		}
	}

	private static bool TryCopyOntoCanvas(
		DirectBitmap<ColorRGBA<byte>, byte> source,
		DirectBitmap<ColorRGBA<byte>, byte> canvas,
		int cropX,
		int cropY,
		int logicalHeight,
		[NotNullWhen(false)] out string? reason)
	{
		reason = null;
		int destY = logicalHeight - (cropY + source.Height);
		if (destY < 0 || cropX + source.Width > canvas.Width || destY + source.Height > canvas.Height)
		{
			reason = "restored pixels do not fit the transparent canvas";
			return false;
		}

		for (int y = 0; y < source.Height; y++)
		{
			int sourceRow = y * source.Width;
			int destRow = (destY + y) * canvas.Width + cropX;
			source.Pixels.Slice(sourceRow, source.Width).CopyTo(canvas.Pixels.Slice(destRow, source.Width));
		}

		return true;
	}

	private static bool TryMaskTightPixels(
		DirectBitmap<ColorRGBA<byte>, byte> cropped,
		SpriteUnpackRequest request,
		int cropX,
		int cropY,
		[NotNullWhen(false)] out string? reason)
	{
		reason = null;
		Span<ColorRGBA<byte>> pixels = cropped.Pixels;
		for (int y = 0; y < cropped.Height; y++)
		{
			for (int x = 0; x < cropped.Width; x++)
			{
				// Pixel (x,y) in the unrotated cropped image corresponds to Unity pixels
				// (cropX + x, cropY + (height - 1 - y)) on the logical canvas.
				float unityX = cropX + x + 0.5f;
				float unityY = cropY + (cropped.Height - 1 - y) + 0.5f;
				if (!PixelOverlapsAnyTriangle(unityX, unityY, request))
				{
					pixels[y * cropped.Width + x] = default;
				}
			}
		}

		return true;
	}

	private static bool PixelOverlapsAnyTriangle(float pixelCenterX, float pixelCenterY, SpriteUnpackRequest request)
	{
		foreach (SpriteUnpackTriangle triangle in request.LocalTriangles)
		{
			var a = RelativePixel(triangle.A);
			var b = RelativePixel(triangle.B);
			var c = RelativePixel(triangle.C);
			// Separating-axis theorem for the closed triangle and pixel square. The
			// square axes plus all triangle-edge normals also handle segments/points.
			if (!Separated(1, 0) && !Separated(0, 1)
				&& !Separated(a.Y - b.Y, b.X - a.X)
				&& !Separated(b.Y - c.Y, c.X - b.X)
				&& !Separated(c.Y - a.Y, a.X - c.X))
			{
				return true;
			}

			(double X, double Y) RelativePixel(Vector2 local) => (
				(double)local.X * request.PixelsToUnits + (double)request.PivotX * request.LogicalWidth - pixelCenterX,
				(double)local.Y * request.PixelsToUnits + (double)request.PivotY * request.LogicalHeight - pixelCenterY);

			bool Separated(double x, double y)
			{
				double pa = a.X * x + a.Y * y;
				double pb = b.X * x + b.Y * y;
				double pc = c.X * x + c.Y * y;
				// Expand by a tiny pixel-space tolerance, never erode boundary coverage.
				double radius = (.5 + .000001) * (Math.Abs(x) + Math.Abs(y));
				return Math.Min(pa, Math.Min(pb, pc)) > radius || Math.Max(pa, Math.Max(pb, pc)) < -radius;
			}
		}
		return false;
	}

	private static bool TryRoundExtent(float width, float height, out int roundedWidth, out int roundedHeight, [NotNullWhen(false)] out string? reason, string name)
	{
		roundedWidth = 0;
		roundedHeight = 0;
		reason = null;
		if (!SpriteUnpackLimits.IsSupportedSize(width, height) || !IsNearlyInteger(width) || !IsNearlyInteger(height))
		{
			reason = $"{name} ({width}, {height}) is not a supported integer size";
			return false;
		}

		roundedWidth = (int)MathF.Round(width);
		roundedHeight = (int)MathF.Round(height);
		return true;
	}

	private static bool TryRoundOffset(float x, float y, out int roundedX, out int roundedY, [NotNullWhen(false)] out string? reason, string name)
	{
		roundedX = 0;
		roundedY = 0;
		reason = null;
		if (!float.IsFinite(x) || !float.IsFinite(y) || MathF.Abs(x) > SpriteUnpackLimits.MaximumDimension || MathF.Abs(y) > SpriteUnpackLimits.MaximumDimension || !IsNearlyInteger(x) || !IsNearlyInteger(y))
		{
			reason = $"{name} ({x}, {y}) is not a supported integer offset";
			return false;
		}

		roundedX = (int)MathF.Round(x);
		roundedY = (int)MathF.Round(y);
		return true;
	}

	private static bool IsNearlyInteger(float value) => MathF.Abs(value - MathF.Round(value)) <= IntegerTolerance;
}
