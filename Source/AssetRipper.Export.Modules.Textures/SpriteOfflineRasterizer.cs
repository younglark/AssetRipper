using AssetRipper.TextureDecoder.Rgb.Formats;
using System.Numerics;

namespace AssetRipper.Export.Modules.Textures;

/// <summary>
/// Filter and transfer function used by <see cref="SpriteOfflineRasterizer"/>.
/// These are engine options, not a SpriteExportMode / CLI / UI setting.
/// </summary>
public enum SpriteOfflineFilter
{
	/// <summary>GL_NEAREST-style: <c>floor(uv * size)</c>, with uv=1 clamped to the last texel.</summary>
	Point,
	/// <summary>Standard half-texel bilinear, four taps clamped to the atlas edge.</summary>
	Bilinear,
}

/// <summary>
/// Color path for filtering. Point sampling does not mix texels, so Gamma and Linear
/// produce the same 8-bit result. Bilinear in <see cref="LinearSrgb"/> decodes sRGB,
/// filters in linear light, then encodes and quantizes (IEC 61966-2-1). Callers must
/// pass an explicit color space; this engine does not guess the project setting.
/// </summary>
public enum SpriteOfflineColorSpace
{
	/// <summary>Treat 8-bit channels as [0,1] values and filter in that space (Editor Gamma / UNorm).</summary>
	GammaUnorm,
	/// <summary>sRGB texture in a Linear project. Bilinear uses standard sRGB decode/encode, not Inspector blit.</summary>
	LinearSrgb,
}

/// <summary>
/// Inputs for contract-C logical-canvas rasterization. Callers should fill this from
/// product-available RD fields, typeless mesh positions/indices, and a decoded atlas
/// (<see cref="DirectBitmap"/>). Do not pass Unity Preview / GetSpriteUVs observations
/// as the UV source — UVs are computed by <see cref="SpriteNativeUv"/>.
/// </summary>
public readonly record struct SpriteOfflineRasterRequest(
	float LogicalWidth,
	float LogicalHeight,
	float PixelsToUnits,
	Vector2 PivotNormalized,
	float TextureRectX,
	float TextureRectY,
	float TextureRectWidth,
	float TextureRectHeight,
	Vector2 TextureRectOffset,
	Vector4 UvTransform,
	float DownscaleMultiplier,
	int PackingRotation,
	IReadOnlyList<Vector2> LocalVertices,
	IReadOnlyList<int> Indices,
	SpriteOfflineFilter Filter,
	SpriteOfflineColorSpace ColorSpace,
	int MipCount = 1,
	bool HasVertexColors = false);

/// <summary>
/// Offline logical-canvas rasterizer. Samples a decoded atlas with native RecalculateUVs
/// and pixel-center coverage. This is the minimal hook an exporter can call later; it is
/// not wired into the default YamlUnpacked path.
/// </summary>
/// <remarks>
/// Coverage is a point-in-triangle test at the <b>pixel center</b>
/// <c>(ix + 0.5, iy + 0.5)</c> in y-up canvas pixels. That is not a GPU top-left
/// fill convention and must not be confused with a previously named "top_left"
/// sample-position experiment.
/// First triangle in index-buffer order wins. Uncovered pixels stay (0,0,0,0).
/// UV and texel coordinates are clamped. Inspector GUITextureBlit2SRGB is not applied.
/// </remarks>
public static class SpriteOfflineRasterizer
{
	/// <summary>
	/// Rasterize <paramref name="request"/> onto a new RGBA32 canvas. The atlas may be any
	/// <see cref="DirectBitmap"/>; it is converted to RGBA8. Row 0 of both the atlas and the
	/// output is the top of the image (TextureConverter / PNG convention). Unity UVs still
	/// treat v=0 as the bottom.
	/// </summary>
	public static bool TryRasterize(
		DirectBitmap atlas,
		SpriteOfflineRasterRequest request,
		[NotNullWhen(true)] out DirectBitmap? canvas,
		[NotNullWhen(false)] out string? reason)
	{
		canvas = null;
		if (!TryValidate(atlas, request, out int canvasWidth, out int canvasHeight, out reason))
		{
			return false;
		}

		DirectBitmap<ColorRGBA<byte>, byte> source = atlas.ConvertToRgba32();
		Vector2[] vertices = request.LocalVertices.ToArray();
		Vector2[] uvs = new Vector2[vertices.Length];
		if (!SpriteNativeUv.TryRecalculate(
			vertices,
			source.Width,
			source.Height,
			request.LogicalWidth,
			request.LogicalHeight,
			request.PivotNormalized,
			request.TextureRectX,
			request.TextureRectY,
			request.TextureRectWidth,
			request.TextureRectHeight,
			request.TextureRectOffset,
			request.UvTransform,
			request.DownscaleMultiplier,
			request.PackingRotation,
			uvs,
			out reason))
		{
			return false;
		}

		Vector2 pivotPixels = SpriteNativeUv.PivotPixels(request.LogicalWidth, request.LogicalHeight, request.PivotNormalized);
		Vector2[] canvasPositions = new Vector2[vertices.Length];
		for (int i = 0; i < vertices.Length; i++)
		{
			canvasPositions[i] = SpriteNativeUv.ToCanvasPixel(vertices[i], request.PixelsToUnits, pivotPixels);
		}

		DirectBitmap<ColorRGBA<byte>, byte> output = new(canvasWidth, canvasHeight);
		// Transparent clear is the default zeroed buffer (0,0,0,0).
		bool[] covered = new bool[canvasWidth * canvasHeight];
		IReadOnlyList<int> indices = request.Indices;
		for (int t = 0; t < indices.Count; t += 3)
		{
			int ia = indices[t];
			int ib = indices[t + 1];
			int ic = indices[t + 2];
			Vector2 a = canvasPositions[ia];
			Vector2 b = canvasPositions[ib];
			Vector2 c = canvasPositions[ic];
			if (!TryBarycentric((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0, a, b, c, out _, out _, out _))
			{
				continue; // degenerate
			}

			for (int iy = 0; iy < canvasHeight; iy++)
			{
				for (int ix = 0; ix < canvasWidth; ix++)
				{
					int pixelIndex = (canvasHeight - 1 - iy) * canvasWidth + ix; // DirectBitmap / PNG y-down
					if (covered[pixelIndex])
					{
						continue;
					}

					// Pixel-center sample in y-up canvas space. Not a top-left edge rule.
					double sx = ix + 0.5;
					double sy = iy + 0.5;
					if (!TryBarycentric(sx, sy, a, b, c, out double wa, out double wb, out double wc))
					{
						continue;
					}

					if (wa < 0.0 || wb < 0.0 || wc < 0.0)
					{
						continue;
					}

					double u = wa * uvs[ia].X + wb * uvs[ib].X + wc * uvs[ic].X;
					double v = wa * uvs[ia].Y + wb * uvs[ib].Y + wc * uvs[ic].Y;
					output.Pixels[pixelIndex] = Sample(source, u, v, request.Filter, request.ColorSpace);
					covered[pixelIndex] = true;
				}
			}
		}

		canvas = output;
		reason = null;
		return true;
	}

	/// <summary>
	/// Flatten already-decoded snapshot triangles into a vertex/index mesh. UV generation
	/// still uses <see cref="SpriteNativeUv"/> from the positions; do not supply observed UVs.
	/// </summary>
	public static (Vector2[] Vertices, int[] Indices) FlattenTriangles(IReadOnlyList<(Vector2 A, Vector2 B, Vector2 C)> triangles)
	{
		Vector2[] vertices = new Vector2[checked(triangles.Count * 3)];
		int[] indices = new int[vertices.Length];
		for (int i = 0; i < triangles.Count; i++)
		{
			int b = i * 3;
			vertices[b] = triangles[i].A;
			vertices[b + 1] = triangles[i].B;
			vertices[b + 2] = triangles[i].C;
			indices[b] = b;
			indices[b + 1] = b + 1;
			indices[b + 2] = b + 2;
		}

		return (vertices, indices);
	}

	private static bool TryValidate(
		DirectBitmap atlas,
		SpriteOfflineRasterRequest request,
		out int canvasWidth,
		out int canvasHeight,
		[NotNullWhen(false)] out string? reason)
	{
		canvasWidth = 0;
		canvasHeight = 0;
		if (atlas is null || atlas.IsEmpty || atlas.Width <= 0 || atlas.Height <= 0)
		{
			reason = "the source atlas is missing or empty";
			return false;
		}

		if (request.HasVertexColors)
		{
			reason = "vertex colors are not supported";
			return false;
		}

		if (request.MipCount != 1)
		{
			reason = $"mips == {request.MipCount} is not supported; this path requires mips = 1";
			return false;
		}

		if (!float.IsFinite(request.DownscaleMultiplier) || request.DownscaleMultiplier != 1f)
		{
			reason = $"downscaleMultiplier == {request.DownscaleMultiplier} is not supported; this path requires downscaleMultiplier = 1";
			return false;
		}

		if (request.PackingRotation is not (SpriteNativeUv.RotationNone or SpriteNativeUv.RotationFlipHorizontal))
		{
			reason = request.PackingRotation is SpriteNativeUv.RotationFlipVertical or SpriteNativeUv.RotationRotate180
				? $"packing rotation {request.PackingRotation} is implemented in native UV math but is not an evidenced raster mode in this version"
				: $"packing rotation {request.PackingRotation} is not supported";
			return false;
		}

		if (request.Filter is not (SpriteOfflineFilter.Point or SpriteOfflineFilter.Bilinear))
		{
			reason = $"unknown filter {request.Filter}";
			return false;
		}

		if (request.ColorSpace is not (SpriteOfflineColorSpace.GammaUnorm or SpriteOfflineColorSpace.LinearSrgb))
		{
			reason = $"unknown color space {request.ColorSpace}";
			return false;
		}

		if (!TryPositiveInteger(request.LogicalWidth, out canvasWidth, "logical width", out string? widthReason))
		{
			reason = widthReason;
			return false;
		}

		if (!TryPositiveInteger(request.LogicalHeight, out canvasHeight, "logical height", out string? heightReason))
		{
			reason = heightReason;
			return false;
		}

		if (!SpriteUnpackLimits.IsSupportedSize(canvasWidth, canvasHeight))
		{
			reason = $"logical canvas size is outside the unpacking budget ({canvasWidth}x{canvasHeight})";
			return false;
		}

		if (!float.IsFinite(request.PixelsToUnits) || request.PixelsToUnits == 0f)
		{
			reason = "pixelsToUnits must be a finite non-zero value";
			return false;
		}

		if (!float.IsFinite(request.PivotNormalized.X) || !float.IsFinite(request.PivotNormalized.Y)
			|| !float.IsFinite(request.TextureRectX) || !float.IsFinite(request.TextureRectY)
			|| !float.IsFinite(request.TextureRectWidth) || !float.IsFinite(request.TextureRectHeight)
			|| !float.IsFinite(request.TextureRectOffset.X) || !float.IsFinite(request.TextureRectOffset.Y)
			|| !float.IsFinite(request.UvTransform.X) || !float.IsFinite(request.UvTransform.Y)
			|| !float.IsFinite(request.UvTransform.Z) || !float.IsFinite(request.UvTransform.W))
		{
			reason = "raster request contains a non-finite field";
			return false;
		}

		if (request.LocalVertices is null || request.LocalVertices.Count == 0)
		{
			reason = "the mesh has no vertices";
			return false;
		}

		if (request.Indices is null || request.Indices.Count == 0)
		{
			reason = "the mesh has no indices";
			return false;
		}

		if (request.Indices.Count % 3 != 0)
		{
			reason = "the index buffer is not a complete triangle list";
			return false;
		}

		int vertexCount = request.LocalVertices.Count;
		for (int i = 0; i < request.LocalVertices.Count; i++)
		{
			if (!float.IsFinite(request.LocalVertices[i].X) || !float.IsFinite(request.LocalVertices[i].Y))
			{
				reason = $"vertex {i} is non-finite";
				return false;
			}
		}

		for (int i = 0; i < request.Indices.Count; i++)
		{
			int index = request.Indices[i];
			if ((uint)index >= (uint)vertexCount)
			{
				reason = $"index {index} is outside the vertex range [0, {vertexCount})";
				return false;
			}
		}

		reason = null;
		return true;
	}

	private static bool TryPositiveInteger(float value, out int integer, string name)
	{
		return TryPositiveInteger(value, out integer, name, out _);
	}

	private static bool TryPositiveInteger(float value, out int integer, string name, [NotNullWhen(false)] out string? reason)
	{
		integer = 0;
		if (!float.IsFinite(value) || value <= 0)
		{
			reason = $"{name} must be a positive finite integer (got {value})";
			return false;
		}

		int truncated = (int)value;
		if (value != truncated)
		{
			reason = $"{name} must be a positive finite integer (got {value})";
			return false;
		}

		integer = truncated;
		reason = null;
		return true;
	}

	private static bool TryBarycentric(
		double px,
		double py,
		Vector2 a,
		Vector2 b,
		Vector2 c,
		out double wa,
		out double wb,
		out double wc)
	{
		double den = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
		if (den == 0.0)
		{
			wa = wb = wc = 0;
			return false;
		}

		wa = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / den;
		wb = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / den;
		wc = 1.0 - wa - wb;
		return true;
	}

	private static ColorRGBA<byte> Sample(
		DirectBitmap<ColorRGBA<byte>, byte> atlas,
		double u,
		double v,
		SpriteOfflineFilter filter,
		SpriteOfflineColorSpace colorSpace)
	{
		u = u < 0.0 ? 0.0 : (u > 1.0 ? 1.0 : u);
		v = v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
		int width = atlas.Width;
		int height = atlas.Height;
		if (filter is SpriteOfflineFilter.Point)
		{
			int sx = (int)Math.Floor(u * width);
			int syUp = (int)Math.Floor(v * height);
			if (sx >= width)
			{
				sx = width - 1;
			}

			if (syUp >= height)
			{
				syUp = height - 1;
			}

			if (sx < 0)
			{
				sx = 0;
			}

			if (syUp < 0)
			{
				syUp = 0;
			}

			return Fetch(atlas, sx, syUp);
		}

		double x = u * width - 0.5;
		double y = v * height - 0.5;
		int x0 = (int)Math.Floor(x);
		int y0 = (int)Math.Floor(y);
		double fx = x - x0;
		double fy = y - y0;
		bool linear = colorSpace is SpriteOfflineColorSpace.LinearSrgb;
		double r = 0, g = 0, b = 0, a = 0;
		for (int dy = 0; dy <= 1; dy++)
		{
			double wy = dy == 0 ? 1.0 - fy : fy;
			for (int dx = 0; dx <= 1; dx++)
			{
				double wx = dx == 0 ? 1.0 - fx : fx;
				double w = wx * wy;
				ColorRGBA<byte> texel = Fetch(atlas, x0 + dx, y0 + dy);
				r += (linear ? SrgbToLinear(texel.R / 255.0) : texel.R / 255.0) * w;
				g += (linear ? SrgbToLinear(texel.G / 255.0) : texel.G / 255.0) * w;
				b += (linear ? SrgbToLinear(texel.B / 255.0) : texel.B / 255.0) * w;
				a += texel.A / 255.0 * w;
			}
		}

		ColorRGBA<byte> result = default;
		result.SetChannels(
			Quantize(linear ? LinearToSrgb(r) : r),
			Quantize(linear ? LinearToSrgb(g) : g),
			Quantize(linear ? LinearToSrgb(b) : b),
			Quantize(a));
		return result;
	}

	/// <summary>
	/// IEC 61966-2-1 sRGB electro-optical transfer. This is the runtime-style CPU path;
	/// Inspector <c>GUITextureBlit2SRGB</c> is not applied.
	/// </summary>
	private static double SrgbToLinear(double c)
	{
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}

	private static double LinearToSrgb(double linear)
	{
		double c = linear < 0.0 ? 0.0 : (linear > 1.0 ? 1.0 : linear);
		return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
	}

	private static ColorRGBA<byte> Fetch(DirectBitmap<ColorRGBA<byte>, byte> atlas, int x, int yUp)
	{
		if (x < 0)
		{
			x = 0;
		}
		else if (x >= atlas.Width)
		{
			x = atlas.Width - 1;
		}

		if (yUp < 0)
		{
			yUp = 0;
		}
		else if (yUp >= atlas.Height)
		{
			yUp = atlas.Height - 1;
		}

		// Atlas DirectBitmap row 0 is the top; Unity UV y=0 is the bottom.
		int row = atlas.Height - 1 - yUp;
		return atlas.Pixels[row * atlas.Width + x];
	}

	private static byte Quantize(double value)
	{
		if (value <= 0.0)
		{
			return 0;
		}

		if (value >= 1.0)
		{
			return 255;
		}

		return (byte)Math.Floor(value * 255.0 + 0.5);
	}
}
