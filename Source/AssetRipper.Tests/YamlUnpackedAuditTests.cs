using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Primitives;
using AssetRipper.Processing.Textures;
using AssetRipper.Processing;
using AssetRipper.Import.Logging;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.IO.Files;
using System.Drawing;
using System.Numerics;
using System.Text.RegularExpressions;

namespace AssetRipper.Tests;

internal partial class YamlUnpackedSpriteExportTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task YamlRetainsLogicalCanvasForUnrotatedTrimmedMesh(bool atlasBinding)
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		var texture = CreateRgbaTexture(collection, "CanvasSheet", 16, 8, new byte[16 * 8 * 4]);
		var sprite = CreateSprite(collection, texture, "LogicalCanvas", new RectangleF(0, 0, 10, 10),
			new Vector2(.5f), new Vector4(1, 2, 3, 4), new RectangleF(8, 0, 8, 8), new Vector2(1), 64);
		AddFloatUvQuad(sprite, 0, true);
		if (atlasBinding)
		{
			var mapping = AddAtlasMapping(collection, sprite, texture);
			mapping.SettingsRaw = 67;
			mapping.UvTransform.CopyValues(sprite.RD.UvTransform);
			mapping.AtlasRectOffset.CopyValues(new Vector2(17, 29));
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		RectangleF processedRect = sprite.Rect.CastToStruct();
		var fs = Export(game, SpriteExportMode.Yaml);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "LogicalCanvas.asset"));
		await Assert.That(ReadRect(yaml)).IsEqualTo((0f, 0f, 10f, 10f));
		await Assert.That(ReadVector4(yaml, "m_Border")).IsEqualTo((1f, 2f, 3f, 4f));
		await Assert.That(ReadVector(yaml, "m_Pivot")).IsEqualTo((.5f, .5f));
		await Assert.That(sprite.Rect.CastToStruct()).IsEqualTo(processedRect);
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(true, false, false)]
	[Arguments(false, true, false)]
	[Arguments(false, true, true)]
	public async Task FractionalTightBoundsPreservePixelGridAndContinuousUv(bool fractionalTranslation, bool implicitUv, bool atlasBinding)
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 9, 2, 6, 6, 55, 66, 77, 255);
		var texture = CreateRgbaTexture(collection, "FractionalSheet", 16, 8, pixels);
		var sprite = CreateSprite(collection, texture, "Fractional", new RectangleF(0, 0, 10, 10), new Vector2(.5f), default,
			new RectangleF(9.07612f, .07612f, 5.84776f, 5.84776f), new Vector2(1.07612f, 1.07612f), 64);
		if (fractionalTranslation) sprite.RD.TextureRect.X += .25f;
		AddFloatUvQuad(sprite, 0, true);
		using (MemoryStream stream = new())
		{
			using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
			{
				foreach (Vector2 pixel in new Vector2[] { new(9, 0), new(15, 0), new(15, 6), new(9, 6) })
				{
					Vector2 local = (pixel - new Vector2(8, -1) - new Vector2(5)) / 100;
					writer.Write(local.X); writer.Write(local.Y); writer.Write(0f);
					writer.Write(implicitUv ? 0 : pixel.X / 16); writer.Write(implicitUv ? 0 : pixel.Y / 8);
				}
			}
			sprite.RD.VertexData.Data = stream.ToArray();
		}
		sprite.RD.SubMeshes[0].IndexCount = 6;
		sprite.RD.IndexBuffer = [0, 0, 1, 0, 2, 0, 0, 0, 2, 0, 3, 0];
		if (atlasBinding)
		{
			var mapping = AddAtlasMapping(collection, sprite, texture);
			mapping.TextureRect.CopyValues(sprite.RD.TextureRect);
			mapping.TextureRectOffset.CopyValues(sprite.RD.TextureRectOffset);
			mapping.SettingsRaw = 67;
			mapping.UvTransform.CopyValues(sprite.RD.UvTransform);
			mapping.AtlasRectOffset.CopyValues(new Vector2(17, 29));
		}

		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		string? image = TryFindUnpackedImage(fs, sprite.Name, sprite.PathID);
		if (fractionalTranslation)
		{
			await Assert.That(image).IsNull();
			return;
		}
		await Assert.That(image).IsNotNull();
		var (width, _, png) = PngReader.ReadRgba(fs.File.ReadAllBytes(image!));
		await Assert.That(GetPixel(png, width, 1, 3)).IsEqualTo(((byte)55, (byte)66, (byte)77, (byte)255));
		await Assert.That(GetPixel(png, width, 0, 3).A).IsEqualTo((byte)0);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "Fractional.asset"));
		byte[] vertexBytes = Convert.FromHexString(Regex.Match(yaml, @"_typelessdata: ([0-9a-fA-F]+)").Groups[1].Value);
		await Assert.That(MathF.Abs(BitConverter.ToSingle(vertexBytes, 12) - .1f) < .000001f).IsTrue();
		await Assert.That(MathF.Abs(BitConverter.ToSingle(vertexBytes, 16) - .1f) < .000001f).IsTrue();
	}

	[Test]
	[Arguments(false, -1f)]
	[Arguments(true, -1f)]
	[Arguments(false, 2f)]
	public async Task NonAtlasUnpackedSentinelDoesNotBlockIndependentTexture(bool packed, float offset)
	{
		var (collection, sprite, _, _) = CreateTwoSpriteSheet();
		sprite.RD.AtlasRectOffset!.CopyValues(new Vector2(offset));
		if (packed) sprite.RD.SettingsRaw |= 1;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		SpriteSourceSnapshotStore.TryGet(sprite, out var snapshot);
		await Assert.That(snapshot!.AtlasRectOffset).IsEqualTo((Vector2?)new Vector2(offset));
		await Assert.That(snapshot.HasAtlasBinding).IsFalse();
		await Assert.That(snapshot.IsPacked).IsEqualTo(packed);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		if (!packed && offset == -1)
			await Assert.That(TryFindUnpackedImage(fs, sprite.Name, sprite.PathID)).IsNotNull();
		else
			await Assert.That(TryFindUnpackedImage(fs, sprite.Name, sprite.PathID)).IsNull();
	}

	[Test]
	public async Task UnpackedImporterInheritsSourceSamplingSettings()
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		texture.ColorSpace_C28E = ColorSpace.Linear;
		texture.TextureSettings_C28.FilterMode = 1;
		texture.TextureSettings_C28.WrapU = 1;
		texture.TextureSettings_C28.WrapV = 1;
		texture.TextureSettings_C28.WrapW = 1;
		texture.TextureSettings_C28.Aniso = 4;
		texture.TextureSettings_C28.MipBias = 1.5f;
		texture.IsReadable_C28 = true;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		string original = fs.File.ReadAllText(FindImage(fs, "Sheet") + ".meta");
		string unpacked = fs.File.ReadAllText(FindUnpackedImage(fs, "Left", sprite.PathID) + ".meta");
		foreach (string key in new[] { "sRGBTexture", "filterMode", "wrapU", "wrapV", "wrapW", "aniso", "mipBias", "isReadable" })
		{
			await Assert.That(MetaValue(unpacked, key)).IsEqualTo(MetaValue(original, key));
		}
		await Assert.That(MetaValue(unpacked, "alphaIsTransparency")).IsEqualTo("0");
	}

	[Test]
	[Arguments("mips")]
	[Arguments("normal")]
	[Arguments("hdr")]
	[Arguments("encodedHdr")]
	[Arguments("streamingMips")]
	public async Task UnsupportedSourceSamplingFallsBack(string kind)
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		if (kind == "mips") texture.MipCount_C28 = 4;
		if (kind == "normal") texture.LightmapFormat_C28E = TextureUsageMode.NormalmapDXT5nm;
		if (kind == "encodedHdr") texture.LightmapFormat_C28E = TextureUsageMode.RGBMEncoded;
		if (kind == "streamingMips") texture.StreamingMipmaps_C28 = true;
		if (kind == "hdr")
		{
			texture.Format_C28E = TextureFormat.RGBAFloat;
			texture.ImageData_C28 = new byte[16 * 8 * 16];
			texture.CompleteImageSize_C28_Int32 = texture.ImageData_C28.Length;
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "source texture");
	}

	[Test]
	public async Task AlwaysPaddedTextureWithMatchingLogicalSizeUnpacks()
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		texture.LightmapFormat_C28E = TextureUsageMode.AlwaysPadded;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		await Assert.That(TryFindUnpackedImage(fs, sprite.Name, sprite.PathID)).IsNotNull();
	}

	[Test]
	public async Task AlwaysPaddedTextureWithMismatchedImageSizeFallsBack()
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		texture.LightmapFormat_C28E = TextureUsageMode.AlwaysPadded;
		byte[] padded = new byte[texture.ImageData_C28.Length + 16 * 8 * 4];
		texture.ImageData_C28.CopyTo(padded, 0);
		texture.ImageData_C28 = padded;
		if (texture.Has_CompleteImageSize_C28_Int32())
		{
			texture.CompleteImageSize_C28_Int32 = padded.Length;
		}
		if (texture.Has_CompleteImageSize_C28_UInt32())
		{
			texture.CompleteImageSize_C28_UInt32 = (uint)padded.Length;
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "AlwaysPadded");
	}

	[Test]
	[Arguments("mips")]
	[Arguments("normal")]
	[Arguments("hdr")]
	public async Task SemanticFallbackReasonsAreSpecific(string kind)
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		string expected = kind switch
		{
			"mips" => "has mipmaps",
			"normal" => "usage mode",
			_ => "format",
		};
		if (kind == "mips") texture.MipCount_C28 = 4;
		if (kind == "normal") texture.LightmapFormat_C28E = TextureUsageMode.NormalmapDXT5nm;
		if (kind == "hdr")
		{
			texture.Format_C28E = TextureFormat.RGBAFloat;
			texture.ImageData_C28 = new byte[16 * 8 * 16];
			texture.CompleteImageSize_C28_Int32 = texture.ImageData_C28.Length;
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, expected);
		if (kind == "mips")
		{
			// A mip chain must not be reported as a usage or format problem.
			List<string> warnings = [];
			CollectingLogger logger = new(warnings);
			Logger.Add(logger);
			try { Export(game, SpriteExportMode.YamlUnpacked); }
			finally { Logger.Remove(logger); }
			await Assert.That(warnings.Any(w => w.Contains("has mipmaps", StringComparison.Ordinal))).IsTrue();
			await Assert.That(warnings.Any(w => w.Contains("non-default usage semantics, or an unverified", StringComparison.Ordinal))).IsFalse();
		}
	}

	[Test]
	public async Task UnpackedTextureIsRgbaPngEvenWhenAtlasExportIsJpeg()
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		sprite.Rect.Width = 10;
		sprite.Rect.Height = 10;
		sprite.RD.TextureRectOffset.CopyValues(new Vector2(1));
		sprite.RD.UvTransform!.CopyValues(new Vector4(100, 1.5f, 100, 6.5f));
		for (int i = 3; i < texture.ImageData_C28.Length; i += 4) texture.ImageData_C28[i] = 128;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		FullConfiguration configuration = new();
		configuration.ExportSettings.SpriteExportMode = SpriteExportMode.YamlUnpacked;
		configuration.ExportSettings.ImageExportFormat = ImageExportFormat.Jpeg;
		VirtualFileSystem fs = new();
		new ExportHandler(configuration).Export(game, "output", fs);
		await Assert.That(fs.Directory.EnumerateFiles("/output", "*.jpeg", System.IO.SearchOption.AllDirectories)
			.Any(p => Path.GetFileNameWithoutExtension(p) == $"Left_{sprite.PathID}")).IsFalse();
		string path = FindUnpackedImage(fs, "Left", sprite.PathID);
		await Assert.That(path.EndsWith(".png", StringComparison.Ordinal)).IsTrue();
		var (width, _, png) = PngReader.ReadRgba(fs.File.ReadAllBytes(path));
		await Assert.That(GetPixel(png, width, 0, 0).A).IsEqualTo((byte)0);
		await Assert.That(GetPixel(png, width, 1, 1).A).IsEqualTo((byte)128);
		await Assert.That(fs.Directory.EnumerateFiles("/output", "*.jpeg", System.IO.SearchOption.AllDirectories).Any(p => Path.GetFileName(p) == "Sheet.jpeg")).IsTrue();
		string yaml = fs.File.ReadAllText(FindAsset(fs, "Left.asset"));
		string guid = MetaValue(fs.File.ReadAllText(path + ".meta"), "guid");
		await Assert.That(yaml).Contains($"texture: {{fileID: 2800000, guid: {guid}, type: 3}}");
	}

	[Test]
	[Arguments("tiny")]
	[Arguments("crossing")]
	[Arguments("degenerate")]
	[Arguments("boundary")]
	public async Task TightMaskConservativelyRetainsTrianglePixelIntersection(string shape)
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.Parse("4.3.0f1"));
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 8, 0, 8, 8, 55, 66, 77, 128);
		var texture = CreateRgbaTexture(collection, "TinySheet", 16, 8, pixels);
		var sprite = CreateSprite(collection, texture, "Tiny", new RectangleF(0, 0, 10, 10), new Vector2(.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1), 1u);
		Vector2[] points = shape switch
		{
			"crossing" => [new(1, 1.2f), new(2.1f, 1.21f), new(2.1f, 1.22f)],
			"degenerate" => [new(1.1f, 1.1f), new(1.3f, 1.3f), new(1.4f, 1.4f)],
			"boundary" => [new(2, 1.1f), new(2, 1.3f), new(2, 1.4f)],
			_ => [new(1.1f, 1.1f), new(1.4f, 1.1f), new(1.1f, 1.4f)],
		};
		for (ushort i = 0; i < 3; i++)
		{
			var vertex = sprite.RD.Vertices!.AddNew();
			vertex.Pos.CopyValues(new Vector3((points[i] - new Vector2(5)) / 100, 0));
			vertex.Uv!.CopyValues((points[i] - new Vector2(1) + new Vector2(8, 0)) / new Vector2(16, 8));
			sprite.RD.Indices!.Add(i);
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		var (width, _, png) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Tiny", sprite.PathID)));
		await Assert.That(GetPixel(png, width, 1, 8)).IsEqualTo(((byte)55, (byte)66, (byte)77, (byte)128));
		await Assert.That(GetPixel(png, width, 8, 1).A).IsEqualTo((byte)0);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "Tiny.asset"));
		var uv = ReadVector(yaml, "uv");
		(float X, float Y) expected = shape switch
		{
			"crossing" => (.1f, .12f),
			"boundary" => (.2f, .11f),
			_ => (.11f, .11f),
		};
		await Assert.That(MathF.Abs(uv.Item1 - expected.X) < .000001f).IsTrue();
		await Assert.That(MathF.Abs(uv.Item2 - expected.Y) < .000001f).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	public async Task TightSecondSubmeshUsesAbsoluteVertexRangeAndBaseVertex(int baseVertex)
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 8, 0, 8, 8, 55, 66, 77, 128);
		var texture = CreateRgbaTexture(collection, "MultiSheet", 16, 8, pixels);
		var sprite = CreateSprite(collection, texture, "Multi", new RectangleF(0, 0, 10, 10), new Vector2(.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1), 1u);
		AddFloatUvQuad(sprite, 0, true);
		var mesh = sprite.RD.SubMeshes!.AddNew();
		mesh.FirstVertex = 1;
		mesh.VertexCount = 3;
		mesh.BaseVertex = (uint)baseVertex;
		mesh.FirstByte = 6;
		mesh.IndexCount = 3;
		sprite.RD.IndexBuffer = [0, 0, 1, 0, 2, 0, (byte)(1 - baseVertex), 0, (byte)(2 - baseVertex), 0, (byte)(3 - baseVertex), 0];
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		var (width, _, png) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Multi", sprite.PathID)));
		await Assert.That(GetPixel(png, width, 1, 1)).IsEqualTo(((byte)55, (byte)66, (byte)77, (byte)128));
		string yaml = fs.File.ReadAllText(FindAsset(fs, "Multi.asset"));
		byte[] data = Convert.FromHexString(Regex.Match(yaml, @"_typelessdata: ([0-9a-fA-F]+)").Groups[1].Value);
		await Assert.That(BitConverter.ToSingle(data, 72)).IsEqualTo(.1f);
		await Assert.That(BitConverter.ToSingle(data, 76)).IsEqualTo(.9f);
	}

	[Test]
	[Arguments("unresolved")]
	[Arguments("unmatched")]
	[Arguments("wrongKey")]
	[Arguments("alternate")]
	public async Task ExplicitAtlasBindingCannotFallBackToRawRenderData(string kind)
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		if (kind == "unresolved")
			sprite.SpriteAtlasP = AssetRipper.SourceGenerated.Classes.ClassID_687078895.SpriteAtlas.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654324, 687078895));
		else if (kind is "unmatched" or "wrongKey")
		{
			var atlas = collection.CreateSpriteAtlas();
			sprite.SpriteAtlasP = atlas;
			if (kind == "wrongKey")
			{
				var pair = atlas.RenderDataMap.AddNew();
				pair.Key.Key.CopyValues(sprite.RenderDataKey!.Key);
				pair.Key.Value = sprite.RenderDataKey!.Value ^ 1;
				pair.Value.Texture.SetAsset(collection, texture);
			}
		}
		else
			sprite.AtlasRD!.Texture.SetAsset(collection, Texture2D.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654325, 28)));
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, kind == "alternate" ? "AtlasRD" : "atlas binding");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NewSpriteInstanceDataBranchFallsBackWithoutReadingLegacySkeleton(bool hasInstanceData)
	{
		var version = UnityVersion.Parse("6000.6.0a3");
		var collection = AssetCreator.CreateCollection(version);
		var texture = CreateRgbaTexture(collection, "InstanceSheet", 8, 8, new byte[256]);
		var sprite = CreateSprite(collection, texture, "Instance", new RectangleF(0, 0, 8, 8), new Vector2(.5f), default);
		// Direct external atlas binding exercises the real processor/export chain,
		// without enumerating the baseline's absent PackedSprites list in 6000.6.
		var atlasCollection = AssetCreator.CreateCollection(version);
		var atlas = atlasCollection.CreateSpriteAtlas();
		sprite.SpriteAtlasP = atlas;
		var pair = atlas.RenderDataMap.AddNew();
		pair.Key.Key.CopyValues(sprite.RenderDataKey!.Key);
		pair.Key.Value = sprite.RenderDataKey!.Value;
		var map = pair.Value;
		map.Texture.SetAsset(atlasCollection, texture);
		map.TextureRect.CopyValues(sprite.RD.TextureRect);
		map.TextureRectOffset.CopyValues(sprite.RD.TextureRectOffset);
		map.UvTransform!.CopyValues(sprite.RD.UvTransform!);
		map.DownscaleMultiplier = 1;
		map.SettingsRaw = 3;
		if (hasInstanceData)
		{
			map._002AspriteInstanceData!.PixelsToUnits = 999;
			map._002AspriteInstanceData!.SpriteBones.AddNew().Name_R = "UnseenBone";
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "SpriteInstanceData");
	}

	[Test]
	[Arguments("nanDownscale")]
	[Arguments("nanAtlasOffset")]
	[Arguments("infiniteBorder")]
	[Arguments("nanOffset")]
	[Arguments("infiniteRectX")]
	[Arguments("hugeCanvas")]
	[Arguments("zeroCanvas")]
	[Arguments("negativeCanvas")]
	[Arguments("maxDimension")]
	[Arguments("pixelBudget")]
	[Arguments("nanUv")]
	[Arguments("nanVertexZ")]
	[Arguments("nanPhysics")]
	[Arguments("nanMipBias")]
	[Arguments("nanPositionWithoutUv")]
	[Arguments("nanNormal")]
	[Arguments("floatMaxPivot")]
	[Arguments("imageCount")]
	public async Task UnsafeNumericSpriteParametersWarnAndFallBack(string field)
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		switch (field)
		{
			case "nanDownscale": sprite.RD.DownscaleMultiplier = float.NaN; break;
			case "nanAtlasOffset": sprite.RD.AtlasRectOffset!.X = float.NaN; break;
			case "infiniteBorder": sprite.Border!.X = float.PositiveInfinity; break;
			case "nanOffset": sprite.Offset.X = float.NaN; break;
			case "infiniteRectX": sprite.Rect.X = float.PositiveInfinity; break;
			case "hugeCanvas": sprite.Rect.Width = 2147483648f; break;
			case "zeroCanvas": sprite.Rect.Width = 0; break;
			case "negativeCanvas": sprite.Rect.Width = -1; break;
			case "maxDimension": sprite.Rect.Width = 20000; break;
			case "pixelBudget": sprite.Rect.Width = 8192; sprite.Rect.Height = 8192; break;
			case "nanUv": sprite.RD.UvTransform!.X = float.NaN; break;
			case "nanVertexZ": AddFloatUvQuad(sprite, 0); BitConverter.GetBytes(float.NaN).CopyTo(sprite.RD.VertexData!.Data, 8); break;
			case "nanPhysics": sprite.PhysicsShape!.AddNew().AddNew().X = float.NaN; break;
			case "nanMipBias": texture.TextureSettings_C28.MipBias = float.NaN; break;
			case "floatMaxPivot": sprite.Pivot!.X = float.MaxValue; break;
			case "imageCount": texture.ImageCount_C28 = 2; texture.ImageData_C28 = new byte[1024]; break;
			case "nanPositionWithoutUv":
				AddFloatUvQuad(sprite, 0);
				sprite.RD.VertexData!.Channels![4].Dimension = 0;
				BitConverter.GetBytes(float.NaN).CopyTo(sprite.RD.VertexData!.Data, 0);
				break;
			case "nanNormal":
				AddFloatUvQuad(sprite, 0);
				var data = sprite.RD.VertexData!;
				byte[] bytes = new byte[128];
				for (int v = 0; v < 4; v++)
				{
					data.Data.AsSpan(v * 20, 12).CopyTo(bytes.AsSpan(v * 32));
					data.Data.AsSpan(v * 20 + 12, 8).CopyTo(bytes.AsSpan(v * 32 + 24));
				}
				data.Channels![1].Dimension = 3;
				data.Channels![1].Offset = 12;
				data.Channels![4].Offset = 24;
				data.Data = bytes;
				BitConverter.GetBytes(float.NaN).CopyTo(bytes, 12);
				break;
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "numeric");
	}

	[Test]
	[Arguments("scriptable")]
	[Arguments("corresponding")]
	public async Task UnresolvedNonTextureReferencesRetainOrdinaryYamlIdentity(string field)
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.Parse("2023.1.0f1"));
		var texture = CreateRgbaTexture(collection, "ReferencedSheet", 8, 8, new byte[256]);
		var sprite = CreateSprite(collection, texture, "Referenced", new RectangleF(0, 0, 8, 8), new Vector2(.5f), default);
		var missing = AssetRipper.SourceGenerated.Classes.ClassID_114.MonoBehaviour.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654326, 114));
		if (field == "scriptable") sprite.ScriptableObjects!.AddNew().SetAsset(collection, missing);
		else sprite.CorrespondingSourceObject.SetAsset(collection, missing);
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var regular = Export(game, SpriteExportMode.Yaml);
		await Assert.That(regular.File.ReadAllText(FindAsset(regular, "Referenced.asset"))).Contains("deadbeef");
		await AssertAuditFallback(game, sprite, "unresolved reference");
	}

	[Test]
	[Arguments(7)]
	[Arguments(25)]
	[Arguments(31)]
	public async Task UnknownPackingSettingsBitsAreNotSilentlyRetained(int bit)
	{
		var (collection, sprite, _, _) = CreateTwoSpriteSheet();
		sprite.RD.SettingsRaw |= 1u << bit;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "SettingsRaw");
	}

	[Test]
	[Arguments("truncated")]
	[Arguments("badIndex")]
	[Arguments("topology")]
	[Arguments("incomplete")]
	[Arguments("unaligned")]
	[Arguments("range")]
	public async Task InvalidSecondSubmeshFallsBackInsteadOfKeepingPartialMask(string layout)
	{
		var (collection, sprite, _, _) = CreateTwoSpriteSheet();
		sprite.Pivot!.CopyValues(new Vector2(.5f));
		sprite.RD.UvTransform!.CopyValues(new Vector4(100, 4, 100, 4));
		sprite.RD.SettingsRaw = 1;
		AddFloatUvQuad(sprite, 0, true);
		var mesh = sprite.RD.SubMeshes!.AddNew();
		mesh.FirstVertex = 1;
		mesh.VertexCount = 3;
		mesh.FirstByte = 6;
		mesh.IndexCount = 3;
		sprite.RD.IndexBuffer = [0, 0, 1, 0, 2, 0, 1, 0, 2, 0, 3, 0];
		switch (layout)
		{
			case "truncated": sprite.RD.IndexBuffer = sprite.RD.IndexBuffer[..10]; break;
			case "badIndex": sprite.RD.IndexBuffer[10] = 4; break;
			case "topology": mesh.TopologyE = MeshTopology.Lines; break;
			case "incomplete": mesh.IndexCount = 2; break;
			case "unaligned": mesh.FirstByte = 5; break;
			case "range": mesh.VertexCount = 4; break;
		}
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await AssertAuditFallback(game, sprite, "mesh");
	}

	[Test]
	public async Task ResolvableCrossCollectionAndSelfReferencesSurviveExportLocalCopy()
	{
		var collection = AssetCreator.CreateCollection(UnityVersion.Parse("2023.1.0f1"));
		var other = ((AssetRipper.Assets.Bundles.GameBundle)collection.Bundle).AddNewProcessedCollection("Other", collection.Version);
		var scriptable = other.CreateMonoBehaviour();
		scriptable.Name = "ReferencedScriptable";
		var texture = CreateRgbaTexture(collection, "ReferenceSheet", 8, 8, new byte[256]);
		var sprite = CreateSprite(collection, texture, "References", new RectangleF(0, 0, 8, 8), new Vector2(.5f), default);
		sprite.ScriptableObjects!.AddNew().SetAsset(collection, scriptable);
		sprite.CorrespondingSourceObjectP = sprite;
		scriptable.CorrespondingSourceObjectP = sprite;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		await Assert.That(FindUnpackedImage(fs, "References", sprite.PathID)).IsNotNull();
		string yaml = fs.File.ReadAllText(FindAsset(fs, "References.asset"));
		string scriptableGuid = ReadMetaGuid(fs.File.ReadAllText(FindAsset(fs, "ReferencedScriptable.asset") + ".meta"));
		string spriteGuid = ReadMetaGuid(fs.File.ReadAllText(FindAsset(fs, "References.asset") + ".meta"));
		await Assert.That(yaml).Contains($"- {{fileID: 11400000, guid: {scriptableGuid}, type: 2}}");
		await Assert.That(yaml).Contains("m_CorrespondingSourceObject: {fileID: 21300000}");
		await Assert.That(fs.File.ReadAllText(FindAsset(fs, "ReferencedScriptable.asset")))
			.Contains($"guid: {spriteGuid}");
	}

	[Test]
	public async Task LdrCompressedSourceKeepsDecodedColorWithoutRecompression()
	{
		var (collection, sprite, _, texture) = CreateTwoSpriteSheet();
		texture.Format_C28E = TextureFormat.DXT1;
		texture.ImageData_C28 = new byte[64];
		texture.CompleteImageSize_C28_Int32 = 64;
		// Eight solid-red DXT1 blocks: color0=RGB565 F800, all indices=0.
		for (int block = 0; block < 8; block++) texture.ImageData_C28[block * 8 + 1] = 0xF8;
		var game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		var fs = Export(game, SpriteExportMode.YamlUnpacked);
		string path = FindUnpackedImage(fs, "Left", sprite.PathID);
		var (width, _, png) = PngReader.ReadRgba(fs.File.ReadAllBytes(path));
		await Assert.That(GetPixel(png, width, 0, 0)).IsEqualTo(((byte)255, (byte)0, (byte)0, (byte)255));
		await Assert.That(MetaValue(fs.File.ReadAllText(path + ".meta"), "textureFormat")).IsEqualTo("4");
	}

	private static async Task AssertAuditFallback(GameData game, ISprite sprite, string reason)
	{
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			var fs = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fs, sprite.Name, sprite.PathID)).IsNull();
			string yaml = fs.File.ReadAllText(FindAsset(fs, sprite.Name + ".asset"));
			var regular = Export(game, SpriteExportMode.Yaml);
			string original = regular.File.ReadAllText(FindAsset(regular, sprite.Name + ".asset"));
			await Assert.That(Regex.Replace(yaml, @"guid: [0-9a-f]{32}", "guid: <export-guid>"))
				.IsEqualTo(Regex.Replace(original, @"guid: [0-9a-f]{32}", "guid: <export-guid>"));
			await Assert.That(warnings.Any(w => w.Contains(reason, StringComparison.OrdinalIgnoreCase) && w.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally { Logger.Remove(logger); }
	}

	private static string MetaValue(string yaml, string key) => Regex.Match(yaml, $@"(?m)^\s*{key}: ([^\r\n]+)").Groups[1].Value;
}
