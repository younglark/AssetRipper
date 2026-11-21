using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.TextureDecoder.Rgb;
using AssetRipper.TextureDecoder.Rgb.Formats;
using AssetRipper.Primitives;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.Processing.Prefabs;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using System.Drawing;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetRipper.Tests;

[NotInParallel]
internal partial class YamlUnpackedSpriteExportTests
{
	[Test]
	public async Task YamlUnpackedExportsIndependentTexturesAndRewritesSpriteAssets()
	{
		(ProcessedAssetCollection collection, ISprite left, ISprite right, ITexture2D sheet) = CreateTwoSpriteSheet();
		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);

		VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);
		string leftAsset = FindAsset(fileSystem, "Left.asset");
		string rightAsset = FindAsset(fileSystem, "Right.asset");
		string sheetImage = FindImage(fileSystem, "Sheet");
		string leftImage = FindUnpackedImage(fileSystem, "Left", left.PathID);
		string rightImage = FindUnpackedImage(fileSystem, "Right", right.PathID);

		await Assert.That(fileSystem.File.Exists(leftAsset)).IsTrue();
		await Assert.That(fileSystem.File.Exists(rightAsset)).IsTrue();
		await Assert.That(fileSystem.File.Exists(sheetImage)).IsTrue();
		await Assert.That(fileSystem.File.Exists(leftImage)).IsTrue();
		await Assert.That(fileSystem.File.Exists(rightImage)).IsTrue();
		await Assert.That(fileSystem.File.Exists(leftImage + ".meta")).IsTrue();
		await Assert.That(fileSystem.File.Exists(rightImage + ".meta")).IsTrue();

		string leftYaml = fileSystem.File.ReadAllText(leftAsset);
		string leftGuid = ReadMetaGuid(fileSystem.File.ReadAllText(leftImage + ".meta"));
		await Assert.That(leftYaml).Contains($"guid: {leftGuid}");
		await Assert.That(leftYaml).Contains("type: 3");
		await Assert.That(ReadRect(leftYaml)).IsEqualTo((0f, 0f, 8f, 8f));
		await Assert.That(ReadVector(leftYaml, "m_Pivot")).IsEqualTo((0.25f, 0.75f));
		await Assert.That(ReadVector4(leftYaml, "m_Border")).IsEqualTo((1f, 2f, 3f, 4f));
		await Assert.That(ReadTextureRect(leftYaml)).IsEqualTo((0f, 0f, 8f, 8f));

		string sheetGuid = ReadMetaGuid(fileSystem.File.ReadAllText(sheetImage + ".meta"));
		await Assert.That(leftYaml.Contains($"guid: {sheetGuid}")).IsFalse();

		byte[] leftPng = fileSystem.File.ReadAllBytes(leftImage);
		(int width, int height, byte[] pixels) = PngReader.ReadRgba(leftPng);
		await Assert.That((width, height)).IsEqualTo((8, 8));
		await Assert.That(GetPixel(pixels, width, 0, 0)).IsEqualTo(((byte)0, (byte)0, (byte)255, (byte)255)); // blue, top-left
		await Assert.That(GetPixel(pixels, width, 7, 7)).IsEqualTo(((byte)255, (byte)255, (byte)0, (byte)255)); // yellow, bottom-right
		await Assert.That(GetPixel(pixels, width, 4, 3)).IsEqualTo(((byte)255, (byte)0, (byte)0, (byte)255));

		byte[] rightPng = fileSystem.File.ReadAllBytes(rightImage);
		(_, _, byte[] rightPixels) = PngReader.ReadRgba(rightPng);
		await Assert.That(GetPixel(rightPixels, 8, 0, 0)).IsEqualTo(((byte)0, (byte)255, (byte)0, (byte)255));
	}

	[Test]
	public async Task DefaultYamlKeepsTheSharedSheetReference()
	{
		(ProcessedAssetCollection collection, ISprite left, _, ITexture2D _) = CreateTwoSpriteSheet();
		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);

		VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.Yaml);
		string leftAsset = FindAsset(fileSystem, "Left.asset");
		string leftYaml = fileSystem.File.ReadAllText(leftAsset);
		string? unpacked = TryFindUnpackedImage(fileSystem, "Left", left.PathID);
		await Assert.That(unpacked).IsNull();
		string sheetImage = FindImage(fileSystem, "Sheet");
		string sheetGuid = ReadMetaGuid(fileSystem.File.ReadAllText(sheetImage + ".meta"));
		await Assert.That(leftYaml).Contains($"guid: {sheetGuid}");
	}

	[Test]
	public async Task TrimmedSpriteIsPaddedBackToTheLogicalCanvas()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 10, 20, 30, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "TrimSheet", 8, 8, pixels);
		ISprite sprite = CreateSprite(collection, sheet, "Trimmed", new RectangleF(0, 0, 10, 10), new Vector2(0.5f, 0.5f), default, textureRect: new RectangleF(0, 0, 8, 8), crop: new Vector2(1, 1));

		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);

		string asset = FindAsset(fileSystem, "Trimmed.asset");
		string yaml = fileSystem.File.ReadAllText(asset);
		await Assert.That(ReadRect(yaml)).IsEqualTo((0f, 0f, 10f, 10f));
		await Assert.That(ReadVector(yaml, "m_Pivot")).IsEqualTo((0.5f, 0.5f));
		await Assert.That(ReadTextureRect(yaml)).IsEqualTo((1f, 1f, 8f, 8f));

		string image = FindUnpackedImage(fileSystem, "Trimmed", sprite.PathID);
		(_, _, byte[] pngPixels) = PngReader.ReadRgba(fileSystem.File.ReadAllBytes(image));
		await Assert.That(GetPixel(pngPixels, 10, 0, 0)).IsEqualTo(((byte)0, (byte)0, (byte)0, (byte)0));
		await Assert.That(GetPixel(pngPixels, 10, 1, 1)).IsEqualTo(((byte)10, (byte)20, (byte)30, (byte)255));
		await Assert.That(GetPixel(pngPixels, 10, 8, 8)).IsEqualTo(((byte)10, (byte)20, (byte)30, (byte)255));
		await Assert.That(GetPixel(pngPixels, 10, 9, 9)).IsEqualTo(((byte)0, (byte)0, (byte)0, (byte)0));
	}

	[Test]
	public async Task HorizontalFlipIsUndoneOnTheUnpackedTexture()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 1, 2, 3, 255);
		SetPixel(pixels, 8, 0, 0, 9, 8, 7, 255); // top-left of packed (flipped) image
		SetPixel(pixels, 8, 7, 0, 11, 12, 13, 255); // top-right of packed image
		ITexture2D sheet = CreateRgbaTexture(collection, "FlipSheet", 8, 8, pixels);
		uint settings = 1u | (1u << 1) | ((uint)SpritePackingRotation.FlipHorizontal << 2); // packed, rectangle, flip horizontal
		ISprite sprite = CreateSprite(collection, sheet, "Flipped", new RectangleF(0, 0, 8, 8), new Vector2(0.5f, 0.5f), default, settingsRaw: settings);

		AddFloatUvQuad(sprite, 1);
		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);
		string image = FindUnpackedImage(fileSystem, "Flipped", sprite.PathID);
		(_, _, byte[] pngPixels) = PngReader.ReadRgba(fileSystem.File.ReadAllBytes(image));
		// Inverse of FlipHorizontal moves the packed top-left pixel to top-right.
		await Assert.That(GetPixel(pngPixels, 8, 7, 0)).IsEqualTo(((byte)9, (byte)8, (byte)7, (byte)255));
		await Assert.That(GetPixel(pngPixels, 8, 0, 0)).IsEqualTo(((byte)11, (byte)12, (byte)13, (byte)255));
	}

	[Test]
	public async Task Rotate90FallsBackToTheYamlSheetReference()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 1, 2, 3, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "RotateSheet", 8, 8, pixels);
		uint settings = 1u | (4u << 2); // injected Rotate90
		ISprite sprite = CreateSprite(collection, sheet, "Rotated", new RectangleF(0, 0, 8, 8), new Vector2(0.5f, 0.5f), default, settingsRaw: settings);

		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);
			string asset = FindAsset(fileSystem, "Rotated.asset");
			string yaml = fileSystem.File.ReadAllText(asset);
			string sheetImage = FindImage(fileSystem, "RotateSheet");
			string sheetGuid = ReadMetaGuid(fileSystem.File.ReadAllText(sheetImage + ".meta"));
			await Assert.That(TryFindUnpackedImage(fileSystem, "Rotated", sprite.PathID)).IsNull();
			await Assert.That(yaml).Contains($"guid: {sheetGuid}");
			await Assert.That(warnings.Any(message => message.Contains("packing rotation 4", StringComparison.Ordinal) && message.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task ReusableSessionSpritesAreNotMutatedByYamlUnpackedExport()
	{
		(ProcessedAssetCollection collection, ISprite left, _, _) = CreateTwoSpriteSheet();
		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);

		RectangleF processedRect = left.Rect.CastToStruct();
		Vector2 processedPivot = left.Pivot is null ? default : (Vector2)left.Pivot;
		Vector2 processedOffset = (Vector2)left.Offset;
		RectangleF processedTextureRect = left.RD.TextureRect.CastToStruct();
		uint processedSettings = left.RD.SettingsRaw;

		Export(gameData, SpriteExportMode.Yaml);
		AssertSpriteUnchanged(left, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);

		Export(gameData, SpriteExportMode.YamlUnpacked);
		AssertSpriteUnchanged(left, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);

		Export(gameData, SpriteExportMode.Yaml);
		AssertSpriteUnchanged(left, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);
	}

	[Test]
	public async Task DifferentSpritesSharingARegionAreNotDeduplicated()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 4, 5, 6, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "Shared", 8, 8, pixels);
		ISprite first = CreateSprite(collection, sheet, "First", new RectangleF(0, 0, 8, 8), new Vector2(0.25f, 0.25f), default);
		ISprite second = CreateSprite(collection, sheet, "Second", new RectangleF(0, 0, 8, 8), new Vector2(0.75f, 0.75f), default);

		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);

		string firstImage = FindUnpackedImage(fileSystem, "First", first.PathID);
		string secondImage = FindUnpackedImage(fileSystem, "Second", second.PathID);
		await Assert.That(firstImage).IsNotEqualTo(secondImage);
		string firstGuid = ReadMetaGuid(fileSystem.File.ReadAllText(firstImage + ".meta"));
		string secondGuid = ReadMetaGuid(fileSystem.File.ReadAllText(secondImage + ".meta"));
		await Assert.That(firstGuid).IsNotEqualTo(secondGuid);
		await Assert.That(fileSystem.File.ReadAllText(FindAsset(fileSystem, "First.asset"))).Contains($"guid: {firstGuid}");
		await Assert.That(fileSystem.File.ReadAllText(FindAsset(fileSystem, "Second.asset"))).Contains($"guid: {secondGuid}");
	}


	[Test]
	public async Task TightPackingWithoutAMeshFallsBackToTheYamlSheetReference()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 1, 2, 3, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "TightSheet", 8, 8, pixels);
		uint settings = 1u; // packed + Tight packingMode
		ISprite sprite = CreateSprite(collection, sheet, "Tight", new RectangleF(0, 0, 8, 8), new Vector2(0.5f, 0.5f), default, settingsRaw: settings);

		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fileSystem = Export(gameData, SpriteExportMode.YamlUnpacked);
			string yaml = fileSystem.File.ReadAllText(FindAsset(fileSystem, "Tight.asset"));
			string sheetGuid = ReadMetaGuid(fileSystem.File.ReadAllText(FindImage(fileSystem, "TightSheet") + ".meta"));
			await Assert.That(TryFindUnpackedImage(fileSystem, "Tight", sprite.PathID)).IsNull();
			await Assert.That(yaml).Contains($"guid: {sheetGuid}");
			await Assert.That(warnings.Any(message => message.Contains("tight packing requires a valid triangle mesh", StringComparison.Ordinal) && message.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TightMeshMaskIsolatesNeighborPixels(bool useBgra)
	{
		DirectBitmap atlas = useBgra
			? CreateSolidAtlas<ColorBGRA<byte>>(4, 2)
			: CreateSolidAtlas<ColorRGBA<byte>>(4, 2);

		// Left 2x2 of the 4x2 logical canvas, expressed in pivot-relative units (PPU=1, pivot=0.5).
		SpriteUnpackTriangle first = new(new Vector2(-2f, -1f), new Vector2(0f, -1f), new Vector2(0f, 1f));
		SpriteUnpackTriangle second = new(new Vector2(-2f, -1f), new Vector2(0f, 1f), new Vector2(-2f, 1f));
		SpriteUnpackRequest request = new(4, 2, 0, 0, 0, 0, 4, 2, SpriteUnpacker.RotationNone, true, [first, second], 0.5f, 0.5f, 1f);

		await Assert.That(SpriteUnpacker.TryUnpack(atlas, request, out DirectBitmap? canvas, out _, out string? reason)).IsTrue();
		await Assert.That(reason).IsNull();
		DirectBitmap<ColorRGBA<byte>, byte> rgba = canvas!.ConvertToRgba32();
		await Assert.That((rgba.Width, rgba.Height)).IsEqualTo((4, 2));
		await Assert.That(RgbaPixel(rgba, 0, 0)).IsEqualTo(((byte)255, (byte)0, (byte)0, (byte)255));
		await Assert.That(RgbaPixel(rgba, 1, 1)).IsEqualTo(((byte)255, (byte)0, (byte)0, (byte)255));
		// Column 2 shares the mesh edge, so conservative coverage may keep it. Column 3 is fully outside.
		await Assert.That(RgbaPixel(rgba, 3, 0)).IsEqualTo(((byte)0, (byte)0, (byte)0, (byte)0));
		await Assert.That(RgbaPixel(rgba, 3, 1)).IsEqualTo(((byte)0, (byte)0, (byte)0, (byte)0));
	}

	[Test]
	[Arguments(0, "2022.1.0f1", false)]
	[Arguments(1, "2022.1.0f1", false)]
	[Arguments(2, "2022.1.0f1", false)]
	[Arguments(3, "2022.1.0f1", false)]
	[Arguments(0, "5.6.0f3", false)]
	[Arguments(0, "2017.4.0f1", false)]
	[Arguments(0, "2022.1.0f1", true)]
	[Arguments(1, "2022.1.0f1", true)]
	[Arguments(2, "2022.1.0f1", true)]
	[Arguments(3, "2022.1.0f1", true)]
	public async Task ExplicitFloatUvsMatchUnpackedPaddedPng(int rotation, string version, bool tight)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.Parse(version));
		byte[] pixels = new byte[16 * 16 * 4];
		FillRect(pixels, 16, 8, 4, 8, 8, 7, 19, 31, 255);
		SetPixel(pixels, 16, 8, 11, 210, 20, 30, 255); // packed bottom-left
		ITexture2D sheet = CreateRgbaTexture(collection, "UvSheet", 16, 16, pixels);
		ISprite sprite = CreateSprite(collection, sheet, "UvSprite", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 4, 8, 8), new Vector2(1), 3u | ((uint)rotation << 2));
		if (tight) sprite.RD.SettingsRaw &= ~2u;
		bool split = version.StartsWith("2017", StringComparison.Ordinal);
		AddFloatUvQuad(sprite, rotation, tight, split);
		var vertexData = sprite.RD.VertexData!;
		byte[] originalData = (byte[])vertexData.Data.Clone();
		GameData gameData = CreateGameData(collection);
		new SpriteProcessor().Process(gameData);
		VirtualFileSystem fs = Export(gameData, SpriteExportMode.YamlUnpacked);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "UvSprite.asset"));
		Match bytes = Regex.Match(yaml, @"_typelessdata: ([0-9a-fA-F]+)");
		if (!bytes.Success) throw new InvalidOperationException(yaml);
		byte[] exported = Convert.FromHexString(bytes.Groups[1].Value);
		Vector2[] expected = rotation switch
		{
			0 => [new(0.1f, 0.1f), new(0.9f, 0.1f), new(0.9f, 0.9f), new(0.1f, 0.9f)],
			1 => [new(0.9f, 0.1f), new(0.1f, 0.1f), new(0.1f, 0.9f), new(0.9f, 0.9f)],
			2 => [new(0.1f, 0.9f), new(0.9f, 0.9f), new(0.9f, 0.1f), new(0.1f, 0.1f)],
			_ => [new(0.9f, 0.9f), new(0.1f, 0.9f), new(0.1f, 0.1f), new(0.9f, 0.1f)],
		};
		for (int i = 0; i < 4; i++)
		{
			int uvOffset = split ? 48 + i * 8 : i * 20 + 12;
			int posOffset = split ? i * 12 : i * 20;
			await Assert.That(BitConverter.ToSingle(exported, uvOffset)).IsEqualTo(expected[i].X);
			await Assert.That(BitConverter.ToSingle(exported, uvOffset + 4)).IsEqualTo(expected[i].Y);
			await Assert.That(exported.AsSpan(posOffset, 12).SequenceEqual(originalData.AsSpan(posOffset, 12))).IsTrue();
		}
		await Assert.That(ReadVector4(yaml, "uvTransform")).IsEqualTo((100f, 5f, 100f, 5f));
		(_, _, byte[] png) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "UvSprite", sprite.PathID)));
		int pixelX = rotation is 1 or 3 ? 8 : 1;
		int pixelY = rotation is 2 or 3 ? 1 : 8;
		await Assert.That(GetPixel(png, 10, pixelX, pixelY)).IsEqualTo(((byte)210, (byte)20, (byte)30, (byte)255));
		await Assert.That(vertexData.Data.SequenceEqual(originalData)).IsTrue();
	}

	[Test]
	[Arguments(0)]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(3)]
	public async Task LegacyExplicitUvsMatchTheUnpackedPng(int rotation)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.Parse("4.3.0f1"));
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 8, 0, 8, 8, 11, 22, 33, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "LegacySheet", 16, 8, pixels);
		ISprite sprite = CreateSprite(collection, sheet, "LegacyUv", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1), 3u | ((uint)rotation << 2));
		var vertex = sprite.RD.Vertices!.AddNew();
		vertex.Pos.X = rotation is 1 or 3 ? 0.04f : -0.04f;
		vertex.Pos.Y = rotation is 2 or 3 ? 0.04f : -0.04f;
		vertex.Uv!.X = 0.5f;
		vertex.Uv!.Y = 0;
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "LegacyUv.asset"));
		string[] expected = ["uv: {x: 0.1, y: 0.1}", "uv: {x: 0.9, y: 0.1}", "uv: {x: 0.1, y: 0.9}", "uv: {x: 0.9, y: 0.9}"];
		await Assert.That(yaml).Contains(expected[rotation]);
		(_, _, byte[] png) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "LegacyUv", sprite.PathID)));
		await Assert.That(GetPixel(png, 10, rotation is 1 or 3 ? 8 : 1, rotation is 2 or 3 ? 1 : 8)).IsEqualTo(((byte)11, (byte)22, (byte)33, (byte)255));
		await Assert.That(vertex.Uv!.X).IsEqualTo(0.5f);
	}

	[Test]
	public async Task AtlasRenderDataOverridesPlaceholderAndRetainsLogicalMetadata()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		ITexture2D placeholder = CreateRgbaTexture(collection, "Placeholder", 2, 2, new byte[16]);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 8, 0, 8, 8, 41, 52, 63, 255);
		ITexture2D atlasTexture = CreateRgbaTexture(collection, "RealAtlasTexture", 16, 8, pixels);
		ISprite sprite = CreateSprite(collection, placeholder, "AtlasMapped", new RectangleF(0, 0, 10, 10), new Vector2(0.25f, 0.75f), new Vector4(1, 2, 3, 4), new RectangleF(0, 0, 2, 2));
		var atlas = collection.CreateSpriteAtlas();
		atlas.Name = "RealAtlas";
		sprite.SpriteAtlasP = atlas;
		atlas.PackedSprites!.AddNew().SetAsset(collection, sprite);
		var pair = atlas.RenderDataMap.AddNew();
		pair.Key.Key.CopyValues(sprite.RenderDataKey!.Key);
		pair.Key.Value = sprite.RenderDataKey!.Value;
		var mapping = pair.Value;
		mapping.Texture.SetAsset(collection, atlasTexture);
		mapping.TextureRect.CopyValues(new RectangleF(8, 0, 8, 8));
		mapping.TextureRectOffset.CopyValues(new Vector2(1));
		mapping.DownscaleMultiplier = 1;
		mapping.SettingsRaw = 3;
		mapping.UvTransform.X = 100;
		mapping.UvTransform.Y = 9.5f;
		mapping.UvTransform.Z = 100;
		mapping.UvTransform.W = 6.5f;
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
		string asset = FindAsset(fs, "AtlasMapped.asset");
		string image = FindUnpackedImage(fs, "AtlasMapped", sprite.PathID);
		string yaml = fs.File.ReadAllText(asset);
		await Assert.That(yaml).Contains($"guid: {ReadMetaGuid(fs.File.ReadAllText(image + ".meta"))}");
		await Assert.That(ReadRect(yaml)).IsEqualTo((0f, 0f, 10f, 10f));
		await Assert.That(ReadVector(yaml, "m_Pivot")).IsEqualTo((0.25f, 0.75f));
		await Assert.That(ReadVector4(yaml, "m_Border")).IsEqualTo((1f, 2f, 3f, 4f));
		await Assert.That(ReadTextureRect(yaml)).IsEqualTo((1f, 1f, 8f, 8f));
		(_, _, byte[] png) = PngReader.ReadRgba(fs.File.ReadAllBytes(image));
		await Assert.That(GetPixel(png, 10, 1, 1)).IsEqualTo(((byte)41, (byte)52, (byte)63, (byte)255));
		await Assert.That(GetPixel(png, 10, 0, 0)).IsEqualTo(((byte)0, (byte)0, (byte)0, (byte)0));
	}

	[Test]
	public async Task MissingAtlasTextureDoesNotUseTheRawPlaceholder()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 0, 0, 16, 8, 255, 0, 255, 255);
		ITexture2D placeholder = CreateRgbaTexture(collection, "LargePlaceholder", 16, 8, pixels);
		ISprite sprite = CreateSprite(collection, placeholder, "MissingAtlasTexture", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1));
		var mapping = AddAtlasMapping(collection, sprite, placeholder);
		mapping.Texture.SetAsset(collection, Texture2D.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654321, 28)));
		await Assert.That(mapping.Texture.IsNull()).IsFalse();
		await Assert.That(mapping.Texture.TryGetAsset(collection)).IsNull();
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fs, "MissingAtlasTexture", sprite.PathID)).IsNull();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "MissingAtlasTexture.asset"));
			await Assert.That(yaml).Contains("texture: {fileID: 0}");
			await Assert.That(warnings.Any(w => w.Contains("source texture", StringComparison.Ordinal) && w.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
			VirtualFileSystem regular = Export(game, SpriteExportMode.Yaml);
			await Assert.That(yaml).IsEqualTo(regular.File.ReadAllText(FindAsset(regular, "MissingAtlasTexture.asset")));
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingSplitAlphaDependencyDoesNotBecomeNoAlpha(bool useAtlas)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		ITexture2D texture = CreateRgbaTexture(collection, "ValidMain", 16, 8, new byte[16 * 8 * 4]);
		ISprite sprite = CreateSprite(collection, texture, "MissingAlpha", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1));
		var alphaPointer = useAtlas ? AddAtlasMapping(collection, sprite, texture).AlphaTexture : sprite.RD.AlphaTexture!;
		alphaPointer.SetAsset(collection, Texture2D.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654322, 28)));
		await Assert.That(alphaPointer.IsNull()).IsFalse();
		await Assert.That(alphaPointer.TryGetAsset(collection)).IsNull();
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fs, "MissingAlpha", sprite.PathID)).IsNull();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "MissingAlpha.asset"));
			string guid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "ValidMain") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {guid}");
			await Assert.That(warnings.Any(w => w.Contains("alpha", StringComparison.Ordinal) && w.Contains("could not be resolved", StringComparison.Ordinal) && w.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingSecondaryDependencyDoesNotBecomeNoSecondary(bool useAtlas)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		ITexture2D texture = CreateRgbaTexture(collection, "SecondaryMain", 16, 8, new byte[16 * 8 * 4]);
		ISprite sprite = CreateSprite(collection, texture, "MissingSecondary", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1));
		var secondary = useAtlas
			? AddAtlasMapping(collection, sprite, texture).SecondaryTextures!.AddNew()
			: sprite.RD.SecondaryTextures!.AddNew();
		secondary.Name = "_NormalMap";
		secondary.Texture.SetAsset(collection, Texture2D.Create(new AssetRipper.Assets.Metadata.AssetInfo(collection, 987654323, 28)));
		await Assert.That(secondary.Texture.IsNull()).IsFalse();
		await Assert.That(secondary.Texture.TryGetAsset(collection)).IsNull();
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fs, "MissingSecondary", sprite.PathID)).IsNull();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "MissingSecondary.asset"));
			string guid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "SecondaryMain") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {guid}");
			await Assert.That(warnings.Any(w => w.Contains("secondary textures are not supported", StringComparison.Ordinal) && w.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	private static AssetRipper.SourceGenerated.Subclasses.SpriteAtlasData.ISpriteAtlasData AddAtlasMapping(
		ProcessedAssetCollection collection, ISprite sprite, ITexture2D texture)
	{
		var atlas = collection.CreateSpriteAtlas();
		atlas.Name = "DependencyAtlas";
		sprite.SpriteAtlasP = atlas;
		atlas.PackedSprites!.AddNew().SetAsset(collection, sprite);
		var pair = atlas.RenderDataMap.AddNew();
		pair.Key.Key.CopyValues(sprite.RenderDataKey!.Key);
		pair.Key.Value = sprite.RenderDataKey.Value;
		var mapping = pair.Value;
		mapping.Texture.SetAsset(collection, texture);
		mapping.TextureRect.CopyValues(new RectangleF(8, 0, 8, 8));
		mapping.TextureRectOffset.CopyValues(new Vector2(1));
		mapping.DownscaleMultiplier = 1;
		mapping.SettingsRaw = 3;
		mapping.UvTransform.CopyValues(new Vector4(100, 12, 100, 4));
		return mapping;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SceneAndPrefabReferencesKeepTheOriginalSpriteIdentity(bool scene)
	{
		(ProcessedAssetCollection collection, ISprite sprite, _, _) = CreateTwoSpriteSheet();
		GameData game = CreateGameData(collection);
		ProcessedAssetCollection objects = game.GameBundle.AddNewProcessedCollection("SpriteUsers", collection.Version);
		if (scene) SceneDefinition.FromName("SpriteScene").AddCollection(objects);
		var root = objects.CreateGameObject();
		root.Name = "SpriteUsers";
		var transform = objects.CreateTransform();
		transform.InitializeDefault();
		transform.GameObject_C4P = root;
		root.AddComponent(AssetRipper.SourceGenerated.ClassIDType.Transform, transform);
		for (int i = 0; i < 2; i++)
		{
			var renderer = objects.CreateSpriteRenderer();
			renderer.GameObject_C25P = root;
			renderer.SpriteP = sprite;
			root.AddComponent(AssetRipper.SourceGenerated.ClassIDType.SpriteRenderer, renderer);
		}
		new SpriteProcessor().Process(game);
		new PrefabProcessor().Process(game);
		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
		string spriteAsset = FindAsset(fs, "Left.asset");
		string guid = ReadMetaGuid(fs.File.ReadAllText(spriteAsset + ".meta"));
		string yaml = fs.File.ReadAllText(spriteAsset);
		string id = Regex.Match(yaml, @"--- !u!213 &([0-9]+)").Groups[1].Value;
		string hierarchy = EnumerateFiles(fs).Single(p => p.EndsWith(scene ? ".unity" : ".prefab", StringComparison.Ordinal));
		string references = fs.File.ReadAllText(hierarchy);
		await Assert.That(Regex.Matches(references, $@"m_Sprite: \{{fileID: {id}, guid: {guid}, type: 2\}}").Count).IsEqualTo(2);
		await Assert.That(EnumerateFiles(fs).Count(p => p.EndsWith($"Left_{sprite.PathID}.png", StringComparison.Ordinal))).IsEqualTo(1);
	}

	[Test]
	[Arguments("half")]
	[Arguments("uv1")]
	[Arguments("uvTransform")]
	[Arguments("placeholder")]
	[Arguments("staleUv")]
	[Arguments("rotatedImplicit")]
	public async Task UnverifiedUvLayoutsWarnAndKeepOriginalYaml(string unsupported)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		ITexture2D sheet = CreateRgbaTexture(collection, "GuardedSheet", 16, 8, new byte[16 * 8 * 4]);
		ISprite sprite = CreateSprite(collection, sheet, "Guarded", new RectangleF(0, 0, 10, 10), new Vector2(0.5f), default,
			new RectangleF(8, 0, 8, 8), new Vector2(1));
		AddFloatUvQuad(sprite, 0);
		if (unsupported == "half") sprite.RD.VertexData!.Channels![4].Format = 1;
		if (unsupported == "uv1") sprite.RD.VertexData!.Channels![5].Dimension = 2;
		if (unsupported == "uvTransform") sprite.RD.UvTransform!.X = 123;
		if (unsupported == "staleUv") BitConverter.GetBytes(0.75f).CopyTo(sprite.RD.VertexData!.Data, 12);
		if (unsupported == "rotatedImplicit")
		{
			sprite.RD.VertexData!.VertexCount = 0;
			sprite.RD.SettingsRaw = 7;
			sprite.RD.UvTransform!.X = -100;
		}
		if (unsupported == "placeholder")
		{
			for (int i = 0; i < 4; i++) System.Array.Clear(sprite.RD.VertexData!.Data, i * 20 + 12, 8);
		}
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fs, "Guarded", sprite.PathID)).IsNull();
			string guid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "GuardedSheet") + ".meta"));
			await Assert.That(fs.File.ReadAllText(FindAsset(fs, "Guarded.asset"))).Contains($"guid: {guid}");
			await Assert.That(warnings.Any(w => w.Contains("UV", StringComparison.OrdinalIgnoreCase) && w.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task Golden525AxisAlignedUvTransformIsRebasedWithoutClampingPivot()
	{
		// Serialized golden values from AssetRipper/AssetRipper#525:
		// PPU=32, rect=(234,259,30,28), pivot=(.5,-1/28), uvTransform=(32,249,32,258).
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.Parse("2017.4.0f1"));
		byte[] pixels = new byte[512 * 512 * 4];
		FillRect(pixels, 512, 234, 225, 30, 28, 32, 64, 96, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "GoldenSheet", 512, 512, pixels);
		ISprite sprite = CreateSprite(collection, sheet, "Golden525", new RectangleF(234, 259, 30, 28), new Vector2(0.5f, -1f / 28), default);
		sprite.PixelsToUnits = 32;
		sprite.RD.UvTransform!.CopyValues(new Vector4(32, 249, 32, 258));
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlUnpacked);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "Golden525.asset"));
		await Assert.That(ReadVector4(yaml, "uvTransform")).IsEqualTo((32f, 15f, 32f, -1f));
		await Assert.That(ReadVector(yaml, "m_Pivot")).IsEqualTo((0.5f, -1f / 28));
		(int w, int h, byte[] png) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Golden525", sprite.PathID)));
		await Assert.That((w, h)).IsEqualTo((30, 28));
		await Assert.That(GetPixel(png, w, 0, 0)).IsEqualTo(((byte)32, (byte)64, (byte)96, (byte)255));
	}

	private static void AddFloatUvQuad(ISprite sprite, int rotation, bool tight = false, bool split = false)
	{
		var vertexData = sprite.RD.VertexData!;
		for (int i = 0; i < ShaderChannelExtensions.GetChannelCount(sprite.Collection.Version); i++)
		{
			vertexData.Channels!.AddNew();
		}
		vertexData.Channels![0].Dimension = 3;
		int uvIndex = ShaderChannel.UV0.ToChannel(sprite.Collection.Version);
		vertexData.Channels[uvIndex].Dimension = 2;
		vertexData.Channels[uvIndex].Offset = 12;
		vertexData.VertexCount = 4;
		using MemoryStream data = new();
		using (BinaryWriter writer = new(data, Encoding.UTF8, true))
		{
			var texture = sprite.RD.Texture.TryGetAsset(sprite.Collection)!;
			var rect = sprite.RD.TextureRect;
			Vector2 min = new(rect.X / texture.Width_C28, rect.Y / texture.Height_C28);
			Vector2 max = new((rect.X + rect.Width) / texture.Width_C28, (rect.Y + rect.Height) / texture.Height_C28);
			foreach (Vector2 uv in new Vector2[] { min, new(max.X, min.Y), max, new(min.X, max.Y) })
			{
				float x = uv.X * texture.Width_C28 - rect.X;
				float y = uv.Y * texture.Height_C28 - rect.Y;
				if (rotation is 1 or 3) x = 8 - x;
				if (rotation is 2 or 3) y = 8 - y;
				writer.Write((x - 4) / 100);
				writer.Write((y - 4) / 100);
				writer.Write(0f);
				writer.Write(uv.X);
				writer.Write(uv.Y);
			}
		}
		vertexData.Data = data.ToArray();
		if (split)
		{
			byte[] streams = new byte[80];
			for (int i = 0; i < 4; i++)
			{
				vertexData.Data.AsSpan(i * 20, 12).CopyTo(streams.AsSpan(i * 12));
				vertexData.Data.AsSpan(i * 20 + 12, 8).CopyTo(streams.AsSpan(48 + i * 8));
			}
			vertexData.Channels[uvIndex].Stream = 1;
			vertexData.Channels[uvIndex].Offset = 0;
			vertexData.Data = streams;
		}
		if (tight)
		{
			var submesh = sprite.RD.SubMeshes!.AddNew();
			submesh.VertexCount = 4;
			submesh.IndexCount = 3;
			sprite.RD.IndexBuffer = [0, 0, 1, 0, 2, 0];
		}
	}

	private static void AssertSpriteUnchanged(ISprite sprite, RectangleF rect, Vector2 pivot, Vector2 offset, RectangleF textureRect, uint settings)
	{
		if (sprite.Rect.CastToStruct() != rect
			|| (sprite.Pivot is null ? default : (Vector2)sprite.Pivot) != pivot
			|| (Vector2)sprite.Offset != offset
			|| sprite.RD.TextureRect.CastToStruct() != textureRect
			|| sprite.RD.SettingsRaw != settings)
		{
			throw new InvalidOperationException("The reusable session sprite was mutated during export.");
		}
	}

	private static (ProcessedAssetCollection Collection, ISprite Left, ISprite Right, ITexture2D Sheet) CreateTwoSpriteSheet()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 0, 0, 8, 8, 255, 0, 0, 255);
		FillRect(pixels, 16, 8, 0, 8, 8, 0, 255, 0, 255);
		SetPixel(pixels, 16, 0, 0, 0, 0, 255, 255);
		SetPixel(pixels, 16, 7, 7, 255, 255, 0, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "Sheet", 16, 8, pixels);
		ISprite left = CreateSprite(collection, sheet, "Left", new RectangleF(0, 0, 8, 8), new Vector2(0.25f, 0.75f), new Vector4(1, 2, 3, 4));
		ISprite right = CreateSprite(collection, sheet, "Right", new RectangleF(8, 0, 8, 8), new Vector2(0.5f, 0.5f), default);
		return (collection, left, right, sheet);
	}

	private static ITexture2D CreateRgbaTexture(ProcessedAssetCollection collection, string name, int width, int height, byte[] topLeftRgba)
	{
		ITexture2D texture = collection.CreateTexture2D();
		texture.Name = name;
		texture.Width_C28 = width;
		texture.Height_C28 = height;
		texture.ImageCount_C28 = 1;
		texture.Format_C28E = TextureFormat.RGBA32;
		byte[] imageData = FlipY(topLeftRgba, width, height);
		texture.ImageData_C28 = imageData;
		if (texture.Has_CompleteImageSize_C28_Int32())
		{
			texture.CompleteImageSize_C28_Int32 = imageData.Length;
		}

		if (texture.Has_CompleteImageSize_C28_UInt32())
		{
			texture.CompleteImageSize_C28_UInt32 = (uint)imageData.Length;
		}

		if (texture.Has_MipCount_C28())
		{
			texture.MipCount_C28 = 1;
		}

		return texture;
	}

	private static ISprite CreateSprite(
		ProcessedAssetCollection collection,
		ITexture2D texture,
		string name,
		RectangleF rect,
		Vector2 pivot,
		Vector4 border,
		RectangleF? textureRect = null,
		Vector2? crop = null,
		uint settingsRaw = 0)
	{
		ISprite sprite = collection.CreateSprite();
		sprite.Name = name;
		sprite.Rect.CopyValues(rect);
		if (sprite.Has_Pivot())
		{
			sprite.Pivot.CopyValues(pivot);
		}

		if (sprite.Has_Border())
		{
			sprite.Border.CopyValues(border);
		}

		sprite.PixelsToUnits = 100;
		sprite.Offset.X = (pivot.X - 0.5f) * rect.Width;
		sprite.Offset.Y = (pivot.Y - 0.5f) * rect.Height;
		sprite.RD.Texture.SetAsset(collection, texture);
		RectangleF packedRect = textureRect ?? rect;
		sprite.RD.TextureRect.CopyValues(packedRect);
		Vector2 textureRectOffset = crop ?? Vector2.Zero;
		sprite.RD.TextureRectOffset.X = textureRectOffset.X;
		sprite.RD.TextureRectOffset.Y = textureRectOffset.Y;
		sprite.RD.SettingsRaw = settingsRaw;
		if (sprite.RD.Has_UvTransform())
		{
			int rotation = (int)(settingsRaw >> 2 & 15);
			float x = pivot.X * rect.Width - textureRectOffset.X;
			float y = pivot.Y * rect.Height - textureRectOffset.Y;
			sprite.RD.UvTransform.X = rotation is 1 or 3 ? -100 : 100;
			sprite.RD.UvTransform.Y = packedRect.X + (rotation is 1 or 3 ? packedRect.Width - x : x);
			sprite.RD.UvTransform.Z = rotation is 2 or 3 ? -100 : 100;
			sprite.RD.UvTransform.W = packedRect.Y + (rotation is 2 or 3 ? packedRect.Height - y : y);
		}

		if (sprite.RD.Has_DownscaleMultiplier())
		{
			sprite.RD.DownscaleMultiplier = 1;
		}

		return sprite;
	}


	private static DirectBitmap CreateSolidAtlas<TColor>(int width, int height)
		where TColor : unmanaged, AssetRipper.TextureDecoder.Rgb.IColor<byte>
	{
		DirectBitmap<TColor, byte> bitmap = new(width, height);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				TColor pixel = default;
				if (x < width / 2)
				{
					pixel.SetChannels(255, 0, 0, 255);
				}
				else
				{
					pixel.SetChannels(0, 0, 255, 255);
				}

				bitmap.Pixels[y * width + x] = pixel;
			}
		}

		return bitmap;
	}

	private static (byte R, byte G, byte B, byte A) RgbaPixel(DirectBitmap<ColorRGBA<byte>, byte> bitmap, int x, int y)
	{
		ColorRGBA<byte> pixel = bitmap.Pixels[y * bitmap.Width + x];
		return (pixel.R, pixel.G, pixel.B, pixel.A);
	}

	private static VirtualFileSystem Export(GameData gameData, SpriteExportMode mode)
	{
		FullConfiguration settings = new();
		settings.ExportSettings.SpriteExportMode = mode;
		VirtualFileSystem fileSystem = new();
		new ExportHandler(settings).Export(gameData, "output", fileSystem);
		return fileSystem;
	}

	private static GameData CreateGameData(ProcessedAssetCollection collection)
	{
		return new((GameBundle)collection.Bundle, collection.Version, new BaseManager((s) => { }), null);
	}

	private static string FindAsset(VirtualFileSystem fileSystem, string fileName) => FindPath(fileSystem, fileName);
	private static string FindImage(VirtualFileSystem fileSystem, string textureName) => FindPath(fileSystem, textureName + ".png");

	private static string FindUnpackedImage(VirtualFileSystem fileSystem, string spriteName, long pathId)
	{
		return TryFindUnpackedImage(fileSystem, spriteName, pathId)
			?? throw new FileNotFoundException($"Unpacked image for {spriteName}_{pathId} was not exported.");
	}

	private static string? TryFindUnpackedImage(VirtualFileSystem fileSystem, string spriteName, long pathId)
	{
		string suffix = $"{spriteName}_{pathId}.png";
		foreach (string path in EnumerateFiles(fileSystem))
		{
			if (path.Replace('\\', '/').EndsWith(suffix, StringComparison.Ordinal))
			{
				return path;
			}
		}

		return null;
	}

	private static string FindPath(VirtualFileSystem fileSystem, string fileName)
	{
		foreach (string path in EnumerateFiles(fileSystem))
		{
			if (fileSystem.Path.GetFileName(path).Equals(fileName, StringComparison.Ordinal))
			{
				return path;
			}
		}

		throw new FileNotFoundException(fileName);
	}

	private static IEnumerable<string> EnumerateFiles(VirtualFileSystem fileSystem)
	{
		if (!fileSystem.Directory.Exists("/output"))
		{
			yield break;
		}

		foreach (string path in fileSystem.Directory.EnumerateFiles("/output", "*", System.IO.SearchOption.AllDirectories))
		{
			yield return path;
		}
	}

	private static string ReadMetaGuid(string meta)
	{
		Match match = Regex.Match(meta, @"^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Multiline);
		if (!match.Success)
		{
			throw new InvalidOperationException("The .meta file does not contain a guid.");
		}

		return match.Groups[1].Value;
	}

	private static (float X, float Y, float Width, float Height) ReadTextureRect(string yaml)
	{
		try
		{
			return ReadRect(yaml, "textureRect");
		}
		catch (InvalidOperationException)
		{
			return ReadRect(yaml, "m_TextureRect");
		}
	}

	private static (float X, float Y, float Width, float Height) ReadRect(string yaml, string name = "m_Rect")
	{
		if (TryReadRect(yaml, name, out (float X, float Y, float Width, float Height) rect))
		{
			return rect;
		}

		throw new InvalidOperationException($"Could not read {name} from the YAML asset.");
	}

	private static bool TryReadRect(string yaml, string name, out (float X, float Y, float Width, float Height) rect)
	{
		Match flow = Regex.Match(yaml, name + @":\s*\{x:\s*([^,}]+),\s*y:\s*([^,}]+),\s*width:\s*([^,}]+),\s*height:\s*([^}]+)\}");
		if (flow.Success)
		{
			rect = (Parse(flow.Groups[1].Value), Parse(flow.Groups[2].Value), Parse(flow.Groups[3].Value), Parse(flow.Groups[4].Value));
			return true;
		}

		Match block = Regex.Match(
			yaml,
			name + @":(?:\s*serializedVersion:\s*\d+)?\s*x:\s*([^\r\n]+)\s*y:\s*([^\r\n]+)\s*width:\s*([^\r\n]+)\s*height:\s*([^\r\n]+)");
		if (block.Success)
		{
			rect = (Parse(block.Groups[1].Value), Parse(block.Groups[2].Value), Parse(block.Groups[3].Value), Parse(block.Groups[4].Value));
			return true;
		}

		rect = default;
		return false;
	}

	private static (float X, float Y) ReadVector(string yaml, string name)
	{
		Match match = Regex.Match(yaml, name + @":\s*\{x:\s*([^,}]+),\s*y:\s*([^}]+)\}");
		if (!match.Success)
		{
			throw new InvalidOperationException($"Could not read {name} from the YAML asset.\n" + yaml);
		}

		return (Parse(match.Groups[1].Value), Parse(match.Groups[2].Value));
	}

	private static (float X, float Y, float Z, float W) ReadVector4(string yaml, string name)
	{
		Match match = Regex.Match(yaml, name + @":\s*\{x:\s*([^,}]+),\s*y:\s*([^,}]+),\s*z:\s*([^,}]+),\s*w:\s*([^}]+)\}");
		if (!match.Success)
		{
			throw new InvalidOperationException($"Could not read {name} from the YAML asset.\n" + yaml);
		}

		return (Parse(match.Groups[1].Value), Parse(match.Groups[2].Value), Parse(match.Groups[3].Value), Parse(match.Groups[4].Value));
	}

	private static float Parse(string value) => float.Parse(value.Trim(), CultureInfo.InvariantCulture);

	private static void FillRect(byte[] pixels, int width, int x, int y, int w, int h, byte r, byte g, byte b, byte a)
	{
		for (int row = 0; row < h; row++)
		{
			for (int column = 0; column < w; column++)
			{
				SetPixel(pixels, width, x + column, y + row, r, g, b, a);
			}
		}
	}

	private static void SetPixel(byte[] pixels, int width, int x, int y, byte r, byte g, byte b, byte a)
	{
		int index = (y * width + x) * 4;
		pixels[index] = r;
		pixels[index + 1] = g;
		pixels[index + 2] = b;
		pixels[index + 3] = a;
	}

	private static (byte R, byte G, byte B, byte A) GetPixel(byte[] pixels, int width, int x, int y)
	{
		int index = (y * width + x) * 4;
		return (pixels[index], pixels[index + 1], pixels[index + 2], pixels[index + 3]);
	}

	private static byte[] FlipY(byte[] topLeft, int width, int height)
	{
		byte[] bottomUp = new byte[topLeft.Length];
		int stride = width * 4;
		for (int y = 0; y < height; y++)
		{
			int source = y * stride;
			int dest = (height - 1 - y) * stride;
			Buffer.BlockCopy(topLeft, source, bottomUp, dest, stride);
		}

		return bottomUp;
	}

	private sealed class CollectingLogger(List<string> warnings) : ILogger
	{
		public void Log(LogType type, LogCategory category, string message)
		{
			if (type is LogType.Warning)
			{
				warnings.Add(message);
			}
		}

		public void BlankLine(int numLines)
		{
		}
	}

	private static class PngReader
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
