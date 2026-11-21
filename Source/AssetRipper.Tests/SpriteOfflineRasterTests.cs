using AssetRipper.Export.Modules.Textures;
using AssetRipper.TextureDecoder.Rgb.Formats;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AssetRipper.Tests;

/// <summary>
/// Oracle regressions for the offline logical-canvas rasterizer.
/// Ground truth is Unity 2022.3.57f1 Metal <c>BuildPreviewTexture</c> PNG fixtures
/// (decoded RGBA, not PNG file bytes). Observed Preview UVs are never generation input.
/// </summary>
internal sealed class SpriteOfflineRasterTests
{
	private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "OfflineSpriteRaster");

	[Test]
	public async Task TightTwoNativeUvsMatchObservedPackedUvsWithoutUsingThemAsInput()
	{
		FixtureSprite sprite = LoadSprite("TightTwo");
		Vector2[] uvs = Recalculate(sprite);
		Vector2[] observed =
		[
			new(0.42286252975463867f, 0.96875f),
			new(0.8291125297546387f, 0.4999999701976776f),
			new(0.40723752975463867f, 0.921875f),
			new(0.9384875297546387f, 0.96875f),
			new(0.9384875297546387f, 0.4999999701976776f),
			new(0.9697375297546387f, 0.9375f),
			new(0.9697375297546387f, 0.53125f),
		];
		await Assert.That(uvs).IsEquivalentTo(observed);
	}

	[Test]
	[Arguments("TightTwo", SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm, "TightTwo-Gamma-Point-official.png", "TightTwo-Gamma-Point-python.png")]
	[Arguments("TightTwo", SpriteOfflineFilter.Bilinear, SpriteOfflineColorSpace.GammaUnorm, "TightTwo-Gamma-Bilinear-official.png", "TightTwo-Gamma-Bilinear-python.png")]
	[Arguments("TightTwo", SpriteOfflineFilter.Point, SpriteOfflineColorSpace.LinearSrgb, "TightTwo-Linear-Point-official.png", "TightTwo-Linear-Point-python.png")]
	[Arguments("TightOne", SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm, "TightOne-Gamma-Point-official.png", "TightOne-Gamma-Point-python.png")]
	[Arguments("TightOne", SpriteOfflineFilter.Bilinear, SpriteOfflineColorSpace.GammaUnorm, "TightOne-Gamma-Bilinear-official.png", "TightOne-Gamma-Bilinear-python.png")]
	[Arguments("TightOne", SpriteOfflineFilter.Point, SpriteOfflineColorSpace.LinearSrgb, "TightOne-Linear-Point-official.png", "TightOne-Linear-Point-python.png")]
	public async Task SupportedCombinationsMatchUnityOracleAndPythonRgba(
		string spriteName,
		SpriteOfflineFilter filter,
		SpriteOfflineColorSpace colorSpace,
		string officialFile,
		string pythonFile)
	{
		FixtureSprite sprite = LoadSprite(spriteName);
		DirectBitmap atlas = LoadAtlas();
		await Assert.That(Sha256(File.ReadAllBytes(Path.Combine(FixtureDirectory, "atlas-TightAtlas-export.png"))))
			.IsEqualTo(ExpectedImage("atlas-TightAtlas-export.png").PngSha256);

		SpriteOfflineRasterRequest request = sprite.Request(filter, colorSpace);
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(atlas, request, out DirectBitmap? canvas, out string? reason)).IsTrue();
		await Assert.That(reason).IsNull();
		await Assert.That(canvas).IsNotNull();

		byte[] actual = ToRgba(canvas!.ConvertToRgba32());
		(int officialWidth, int officialHeight, byte[] official) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, officialFile)));
		(int pythonWidth, int pythonHeight, byte[] python) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, pythonFile)));

		FixtureImage officialMeta = ExpectedImage(officialFile);
		await Assert.That(officialWidth).IsEqualTo(officialMeta.Width);
		await Assert.That(officialHeight).IsEqualTo(officialMeta.Height);
		await Assert.That(Sha256(File.ReadAllBytes(Path.Combine(FixtureDirectory, officialFile)))).IsEqualTo(officialMeta.PngSha256);
		await Assert.That(Sha256(official)).IsEqualTo(officialMeta.RgbaSha256);

		await Assert.That(canvas.Width).IsEqualTo(officialWidth);
		await Assert.That(canvas.Height).IsEqualTo(officialHeight);
		await Assert.That(DiffCount(actual, official)).IsEqualTo(0);
		await Assert.That(pythonWidth).IsEqualTo(officialWidth);
		await Assert.That(pythonHeight).IsEqualTo(officialHeight);
		await Assert.That(DiffCount(python, official)).IsEqualTo(0);
		await Assert.That(DiffCount(actual, python)).IsEqualTo(0);
	}

	[Test]
	public async Task LinearBilinearMatchesPythonSrgbAndRecordsOfficialResidual()
	{
		await AssertLinearBilinearFixture(
			"TightTwo",
			"TightTwo-Linear-Bilinear-python-srgb.png",
			"TightTwo-Linear-Bilinear-official.png",
			"TightTwo-Linear-Bilinear-firstStage.png",
			"TightTwo_official_vs_pythonSrgb",
			"TightTwo_firstStage_vs_pythonSrgb");
		await AssertLinearBilinearFixture(
			"TightOne",
			"TightOne-Linear-Bilinear-python-srgb.png",
			"TightOne-Linear-Bilinear-official.png",
			null,
			"TightOne_official_vs_pythonSrgb",
			null);
	}

	private static async Task AssertLinearBilinearFixture(
		string spriteName,
		string pythonFile,
		string officialFile,
		string? firstStageFile,
		string officialResidualKey,
		string? firstStageResidualKey)
	{
		SpriteOfflineRasterRequest request = LoadSprite(spriteName).Request(SpriteOfflineFilter.Bilinear, SpriteOfflineColorSpace.LinearSrgb);
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsTrue();
		await Assert.That(reason).IsNull();
		await Assert.That(canvas).IsNotNull();
		byte[] actual = ToRgba((DirectBitmap<ColorRGBA<byte>, byte>)canvas!);
		(int pythonWidth, int pythonHeight, byte[] python) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, pythonFile)));
		(int officialWidth, int officialHeight, byte[] official) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, officialFile)));
		FixtureImage pythonMeta = ExpectedImage(pythonFile);
		FixtureImage officialMeta = ExpectedImage(officialFile);
		await Assert.That((pythonWidth, pythonHeight)).IsEqualTo((pythonMeta.Width, pythonMeta.Height));
		await Assert.That(Sha256(python)).IsEqualTo(pythonMeta.RgbaSha256);
		await Assert.That(Sha256(File.ReadAllBytes(Path.Combine(FixtureDirectory, officialFile)))).IsEqualTo(officialMeta.PngSha256);
		await Assert.That(Sha256(official)).IsEqualTo(officialMeta.RgbaSha256);
		await Assert.That((canvas!.Width, canvas.Height)).IsEqualTo((pythonWidth, pythonHeight));
		await Assert.That(DiffCount(actual, python)).IsEqualTo(0);

		ChannelDiff officialDiff = MeasureDiff(actual, official);
		ChannelDiff expectedOfficial = ExpectedResidual(officialResidualKey);
		await Assert.That(officialWidth).IsEqualTo(canvas.Width);
		await Assert.That(officialHeight).IsEqualTo(canvas.Height);
		await Assert.That(officialDiff.DifferentPixels).IsEqualTo(expectedOfficial.DifferentPixels);
		await Assert.That(officialDiff.MaxChannel).IsEqualTo(expectedOfficial.MaxChannel);
		await Assert.That(officialDiff.AlphaCoverageMismatches).IsEqualTo(0);
		await Assert.That(officialDiff.RgbOnlyDifferences).IsEqualTo(expectedOfficial.DifferentPixels);

		if (firstStageFile is not null && firstStageResidualKey is not null)
		{
			(_, _, byte[] firstStage) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, firstStageFile)));
			ChannelDiff firstDiff = MeasureDiff(actual, firstStage);
			ChannelDiff expectedFirst = ExpectedResidual(firstStageResidualKey);
			await Assert.That(firstDiff.DifferentPixels).IsEqualTo(expectedFirst.DifferentPixels);
			await Assert.That(firstDiff.MaxChannel).IsEqualTo(expectedFirst.MaxChannel);
			await Assert.That(firstDiff.AlphaCoverageMismatches).IsEqualTo(0);
		}
	}

	[Test]
	[Arguments(SpriteNativeUv.RotationFlipVertical, "not an evidenced raster mode")]
	[Arguments(SpriteNativeUv.RotationRotate180, "not an evidenced raster mode")]
	[Arguments(4, "is not supported")]
	[Arguments(15, "is not supported")]
	public async Task UnknownOrUnevidencedRotationsFailClosed(int rotation, string fragment)
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			PackingRotation = rotation,
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains(fragment);
	}

	[Test]
	public async Task MipCountOtherThanOneFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with { MipCount = 2 };
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("mips = 1");
	}

	[Test]
	public async Task DownscaleOtherThanOneFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with { DownscaleMultiplier = 0.5f };
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("downscaleMultiplier = 1");
	}

	[Test]
	public async Task VertexColorsFailClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with { HasVertexColors = true };
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("vertex colors");
	}

	[Test]
	public async Task NonFiniteVertexFailsClosed()
	{
		FixtureSprite sprite = LoadSprite("TightOne");
		List<Vector2> vertices = sprite.Vertices.ToList();
		vertices[0] = new Vector2(float.NaN, vertices[0].Y);
		SpriteOfflineRasterRequest request = sprite.Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with { LocalVertices = vertices };
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("non-finite");
	}

	[Test]
	public async Task EmptyMeshFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			LocalVertices = Array.Empty<Vector2>(),
			Indices = Array.Empty<int>(),
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("no vertices");
	}

	[Test]
	public async Task IncompleteTriangleListFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			Indices = [0, 1],
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("complete triangle list");
	}

	[Test]
	public async Task OutOfRangeIndexFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			Indices = [0, 1, 99],
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("outside the vertex range");
	}

	[Test]
	public async Task NonIntegerLogicalCanvasFailsClosed()
	{
		SpriteOfflineRasterRequest request = LoadSprite("TightTwo").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			LogicalWidth = 35.910965f,
			LogicalHeight = 29.84776f,
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("positive finite integer");
	}

	[Test]
	public async Task EmptyAtlasFailsClosed()
	{
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(
			DirectBitmap.Empty,
			LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm),
			out DirectBitmap? canvas,
			out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("empty");
	}

	[Test]
	public async Task CanvasOverBudgetFailsClosed()
	{
		int over = SpriteUnpackLimits.MaximumDimension + 1;
		SpriteOfflineRasterRequest request = LoadSprite("TightOne").Request(SpriteOfflineFilter.Point, SpriteOfflineColorSpace.GammaUnorm) with
		{
			LogicalWidth = over,
			LogicalHeight = 1,
		};
		await Assert.That(SpriteOfflineRasterizer.TryRasterize(LoadAtlas(), request, out DirectBitmap? canvas, out string? reason)).IsFalse();
		await Assert.That(canvas).IsNull();
		await Assert.That(reason!).Contains("unpacking budget");
	}

	private static Vector2[] Recalculate(FixtureSprite sprite)
	{
		Vector2[] uvs = new Vector2[sprite.Vertices.Count];
		if (!SpriteNativeUv.TryRecalculate(
			sprite.Vertices.ToArray(),
			64,
			64,
			sprite.LogicalWidth,
			sprite.LogicalHeight,
			sprite.PivotNormalized,
			sprite.TextureRectX,
			sprite.TextureRectY,
			sprite.TextureRectWidth,
			sprite.TextureRectHeight,
			sprite.TextureRectOffset,
			sprite.UvTransform,
			sprite.DownscaleMultiplier,
			sprite.PackingRotation,
			uvs,
			out string? reason))
		{
			throw new InvalidOperationException(reason);
		}

		return uvs;
	}

	private static DirectBitmap LoadAtlas()
	{
		(int width, int height, byte[] rgba) = TestPng.ReadRgba(File.ReadAllBytes(Path.Combine(FixtureDirectory, "atlas-TightAtlas-export.png")));
		return new DirectBitmap<ColorRGBA<byte>, byte>(width, height, 1, rgba);
	}

	private static FixtureSprite LoadSprite(string name)
	{
		JsonElement root = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "fixtures.json"))).RootElement;
		foreach (JsonElement sprite in root.GetProperty("sprites").EnumerateArray())
		{
			if (sprite.GetProperty("name").GetString() == name)
			{
				return FixtureSprite.Parse(sprite);
			}
		}

		throw new InvalidOperationException($"fixture sprite {name} is missing");
	}

	private static FixtureImage ExpectedImage(string fileName)
	{
		JsonElement images = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "fixtures.json"))).RootElement.GetProperty("images");
		JsonElement image = images.GetProperty(fileName);
		return new(
			image.GetProperty("pngSha256").GetString()!,
			image.GetProperty("rgbaSha256").GetString()!,
			image.GetProperty("width").GetInt32(),
			image.GetProperty("height").GetInt32());
	}

	private static byte[] ToRgba(DirectBitmap<ColorRGBA<byte>, byte> bitmap)
	{
		byte[] data = new byte[checked(bitmap.Width * bitmap.Height * 4)];
		Span<ColorRGBA<byte>> pixels = bitmap.Pixels;
		for (int i = 0; i < pixels.Length; i++)
		{
			int o = i * 4;
			data[o] = pixels[i].R;
			data[o + 1] = pixels[i].G;
			data[o + 2] = pixels[i].B;
			data[o + 3] = pixels[i].A;
		}

		return data;
	}

	private static int DiffCount(byte[] left, byte[] right) => MeasureDiff(left, right).DifferentPixels;

	private static ChannelDiff MeasureDiff(byte[] left, byte[] right)
	{
		if (left.Length != right.Length)
		{
			return new ChannelDiff(Math.Max(left.Length, right.Length) / 4, 255, Math.Max(left.Length, right.Length) / 4, 0);
		}

		int different = 0;
		int maxChannel = 0;
		int alphaCoverage = 0;
		int rgbOnly = 0;
		for (int i = 0; i < left.Length; i += 4)
		{
			int dr = Math.Abs(left[i] - right[i]);
			int dg = Math.Abs(left[i + 1] - right[i + 1]);
			int db = Math.Abs(left[i + 2] - right[i + 2]);
			int da = Math.Abs(left[i + 3] - right[i + 3]);
			int max = Math.Max(Math.Max(dr, dg), Math.Max(db, da));
			if (max == 0)
			{
				continue;
			}

			different++;
			maxChannel = Math.Max(maxChannel, max);
			if (da == 0)
			{
				rgbOnly++;
			}

			if ((left[i + 3] == 0) != (right[i + 3] == 0))
			{
				alphaCoverage++;
			}
		}

		return new ChannelDiff(different, maxChannel, alphaCoverage, rgbOnly);
	}

	private static ChannelDiff ExpectedResidual(string key)
	{
		JsonElement residuals = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "fixtures.json"))).RootElement.GetProperty("linearBilinearResiduals");
		JsonElement item = residuals.GetProperty(key);
		return new ChannelDiff(
			item.GetProperty("differentPixels").GetInt32(),
			item.GetProperty("maxChannel").GetInt32(),
			item.GetProperty("opaqueOnlyA").GetInt32() + item.GetProperty("opaqueOnlyB").GetInt32(),
			item.GetProperty("rgbOpaqueOrSameAlpha").GetInt32());
	}

	private readonly record struct ChannelDiff(int DifferentPixels, int MaxChannel, int AlphaCoverageMismatches, int RgbOnlyDifferences);

	private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

	private readonly record struct FixtureImage(string PngSha256, string RgbaSha256, int Width, int Height);

	private sealed class FixtureSprite
	{
		public required string Name { get; init; }
		public required float LogicalWidth { get; init; }
		public required float LogicalHeight { get; init; }
		public required Vector2 PivotNormalized { get; init; }
		public required float PixelsToUnits { get; init; }
		public required float TextureRectX { get; init; }
		public required float TextureRectY { get; init; }
		public required float TextureRectWidth { get; init; }
		public required float TextureRectHeight { get; init; }
		public required Vector2 TextureRectOffset { get; init; }
		public required Vector4 UvTransform { get; init; }
		public required float DownscaleMultiplier { get; init; }
		public required int PackingRotation { get; init; }
		public required IReadOnlyList<Vector2> Vertices { get; init; }
		public required IReadOnlyList<int> Indices { get; init; }

		public SpriteOfflineRasterRequest Request(SpriteOfflineFilter filter, SpriteOfflineColorSpace colorSpace)
		{
			return new(
				LogicalWidth,
				LogicalHeight,
				PixelsToUnits,
				PivotNormalized,
				TextureRectX,
				TextureRectY,
				TextureRectWidth,
				TextureRectHeight,
				TextureRectOffset,
				UvTransform,
				DownscaleMultiplier,
				PackingRotation,
				Vertices,
				Indices,
				filter,
				colorSpace);
		}

		public static FixtureSprite Parse(JsonElement json)
		{
			JsonElement rect = json.GetProperty("logicalRect");
			float[] textureRect = json.GetProperty("textureRect").EnumerateArray().Select(e => e.GetSingle()).ToArray();
			float[] offset = json.GetProperty("textureRectOffset").EnumerateArray().Select(e => e.GetSingle()).ToArray();
			float[] uv = json.GetProperty("uvTransform").EnumerateArray().Select(e => e.GetSingle()).ToArray();
			float[] pivot = json.GetProperty("pivotNorm").EnumerateArray().Select(e => e.GetSingle()).ToArray();
			List<Vector2> vertices = [];
			foreach (JsonElement vertex in json.GetProperty("vertices").EnumerateArray())
			{
				float[] xy = vertex.EnumerateArray().Select(e => e.GetSingle()).ToArray();
				vertices.Add(new Vector2(xy[0], xy[1]));
			}

			List<int> indices = json.GetProperty("indices").EnumerateArray().Select(e => e.GetInt32()).ToList();
			uint settings = json.GetProperty("settingsRaw").GetUInt32();
			return new FixtureSprite
			{
				Name = json.GetProperty("name").GetString()!,
				LogicalWidth = ReadRect(rect, "width"),
				LogicalHeight = ReadRect(rect, "height"),
				PivotNormalized = new Vector2(pivot[0], pivot[1]),
				PixelsToUnits = json.GetProperty("ppu").GetSingle(),
				TextureRectX = textureRect[0],
				TextureRectY = textureRect[1],
				TextureRectWidth = textureRect[2],
				TextureRectHeight = textureRect[3],
				TextureRectOffset = new Vector2(offset[0], offset[1]),
				UvTransform = new Vector4(uv[0], uv[1], uv[2], uv[3]),
				DownscaleMultiplier = json.GetProperty("downscaleMultiplier").GetSingle(),
				PackingRotation = SpriteNativeUv.PackingRotation(settings),
				Vertices = vertices,
				Indices = indices,
			};
		}

		private static float ReadRect(JsonElement rect, string name)
		{
			return rect.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : throw new InvalidOperationException(name);
		}
	}

	/// <summary>Minimal 8-bit RGB/RGBA PNG reader. Output is top-left row-major RGBA.</summary>
	private static class TestPng
	{
		public static (int Width, int Height, byte[] Rgba) ReadRgba(byte[] png)
		{
			if (png.Length < 8 || png[0] != 137 || png[1] != 80)
			{
				throw new InvalidDataException("Not a PNG file.");
			}

			int width = 0;
			int height = 0;
			byte bitDepth = 0;
			byte colorType = 0;
			using MemoryStream idat = new();
			int offset = 8;
			while (offset + 8 <= png.Length)
			{
				int length = ReadInt32BigEndian(png, offset);
				string type = Encoding.ASCII.GetString(png, offset + 4, 4);
				int dataStart = offset + 8;
				if (dataStart + length + 4 > png.Length)
				{
					break;
				}

				if (type == "IHDR")
				{
					width = ReadInt32BigEndian(png, dataStart);
					height = ReadInt32BigEndian(png, dataStart + 4);
					bitDepth = png[dataStart + 8];
					colorType = png[dataStart + 9];
				}
				else if (type == "IDAT")
				{
					idat.Write(png, dataStart, length);
				}
				else if (type == "IEND")
				{
					break;
				}

				offset = dataStart + length + 4;
			}

			if (width <= 0 || height <= 0 || bitDepth != 8 || colorType is not (2 or 6))
			{
				throw new InvalidDataException($"Unsupported PNG format {colorType}/{bitDepth} {width}x{height}.");
			}

			int channels = colorType == 6 ? 4 : 3;
			byte[] raw = InflateZlib(idat.ToArray());
			byte[] rgba = new byte[width * height * 4];
			int rowSize = width * channels;
			int src = 0;
			byte[] prev = new byte[rowSize];
			byte[] row = new byte[rowSize];
			for (int y = 0; y < height; y++)
			{
				byte filter = raw[src++];
				Buffer.BlockCopy(raw, src, row, 0, rowSize);
				src += rowSize;
				ApplyFilter(filter, row, prev, channels);
				for (int x = 0; x < width; x++)
				{
					int dest = (y * width + x) * 4;
					int source = x * channels;
					rgba[dest] = row[source];
					rgba[dest + 1] = row[source + 1];
					rgba[dest + 2] = row[source + 2];
					rgba[dest + 3] = channels == 4 ? row[source + 3] : (byte)255;
				}

				Buffer.BlockCopy(row, 0, prev, 0, rowSize);
			}

			return (width, height, rgba);
		}

		private static void ApplyFilter(byte filter, byte[] row, byte[] prev, int bpp)
		{
			for (int i = 0; i < row.Length; i++)
			{
				byte left = i >= bpp ? row[i - bpp] : (byte)0;
				byte up = prev[i];
				byte upLeft = i >= bpp ? prev[i - bpp] : (byte)0;
				row[i] = filter switch
				{
					0 => row[i],
					1 => unchecked((byte)(row[i] + left)),
					2 => unchecked((byte)(row[i] + up)),
					3 => unchecked((byte)(row[i] + (left + up) / 2)),
					4 => unchecked((byte)(row[i] + Paeth(left, up, upLeft))),
					_ => throw new InvalidDataException($"Unsupported PNG filter {filter}."),
				};
			}
		}

		private static byte Paeth(byte a, byte b, byte c)
		{
			int p = a + b - c;
			int pa = Math.Abs(p - a);
			int pb = Math.Abs(p - b);
			int pc = Math.Abs(p - c);
			if (pa <= pb && pa <= pc)
			{
				return a;
			}

			return pb <= pc ? b : c;
		}

		private static byte[] InflateZlib(byte[] zlib)
		{
			if (zlib.Length < 6)
			{
				throw new InvalidDataException("Invalid PNG zlib stream.");
			}

			using MemoryStream stream = new(zlib, 2, zlib.Length - 6);
			using DeflateStream deflate = new(stream, CompressionMode.Decompress);
			using MemoryStream output = new();
			deflate.CopyTo(output);
			return output.ToArray();
		}

		private static int ReadInt32BigEndian(byte[] data, int offset)
		{
			return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
		}
	}
}
