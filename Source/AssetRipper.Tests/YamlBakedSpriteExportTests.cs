using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;
using AssetRipper.Primitives;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.TextureDecoder.Rgb.Formats;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Enums;
using AssetRipper.SourceGenerated.Extensions;
using System.Drawing;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;

namespace AssetRipper.Tests;

/// <summary>
/// Opt-in YamlBaked (yaml-baked) contract-C export. Does not change Yaml or YamlUnpacked semantics.
/// </summary>
internal partial class YamlUnpackedSpriteExportTests
{
	private static readonly UnityVersion Unity2022_3 = new(2022, 3);
	private static readonly string OfflineRasterDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "OfflineSpriteRaster");

	[Test]
	public async Task YamlBakedExportsIndependentFullRectSpritesForASheet()
	{
		(ProcessedAssetCollection collection, ISprite left, ISprite right, _) = CreateTwoSpriteSheet();
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);

		RectangleF processedRect = left.Rect.CastToStruct();
		Vector2 processedPivot = left.Pivot is null ? default : (Vector2)left.Pivot;
		Vector2 processedOffset = (Vector2)left.Offset;
		RectangleF processedTextureRect = left.RD.TextureRect.CastToStruct();
		uint processedSettings = left.RD.SettingsRaw;

		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
		AssertSpriteUnchanged(left, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);

		string leftAsset = FindAsset(fs, "Left.asset");
		string rightAsset = FindAsset(fs, "Right.asset");
		string leftImage = FindUnpackedImage(fs, "Left", left.PathID);
		string rightImage = FindUnpackedImage(fs, "Right", right.PathID);
		string sheetImage = FindImage(fs, "Sheet");
		string leftGuid = ReadMetaGuid(fs.File.ReadAllText(leftImage + ".meta"));
		string rightGuid = ReadMetaGuid(fs.File.ReadAllText(rightImage + ".meta"));
		string leftYaml = fs.File.ReadAllText(leftAsset);
		string rightYaml = fs.File.ReadAllText(rightAsset);

		await Assert.That(leftYaml).Contains($"guid: {leftGuid}");
		await Assert.That(rightYaml).Contains($"guid: {rightGuid}");
		await Assert.That(leftYaml.Contains($"guid: {ReadMetaGuid(fs.File.ReadAllText(sheetImage + ".meta"))}")).IsFalse();
		await Assert.That(ReadRect(leftYaml)).IsEqualTo((0f, 0f, 8f, 8f));
		await Assert.That(ReadTextureRect(leftYaml)).IsEqualTo((0f, 0f, 8f, 8f));
		await Assert.That(ReadVector(leftYaml, "m_Pivot")).IsEqualTo((0.25f, 0.75f));
		await Assert.That(ReadVector4(leftYaml, "m_Border")).IsEqualTo((1f, 2f, 3f, 4f));
		await Assert.That(ReadPixelsToUnits(leftYaml)).IsEqualTo(100f);
		await Assert.That(ReadSettingsRaw(leftYaml) & 0x7Fu).IsEqualTo(0u);
		await Assert.That(ReadVector4(leftYaml, "uvTransform")).IsEqualTo((100f, 2f, 100f, 6f));

		(int width, int height, byte[] pixels) = PngReader.ReadRgba(fs.File.ReadAllBytes(leftImage));
		await Assert.That((width, height)).IsEqualTo((8, 8));
		await Assert.That(GetPixel(pixels, width, 0, 0)).IsEqualTo(((byte)0, (byte)0, (byte)255, (byte)255));
		await Assert.That(GetPixel(pixels, width, 7, 7)).IsEqualTo(((byte)255, (byte)255, (byte)0, (byte)255));
	}

	[Test]
	public async Task YamlBakedExportsTightTwoFlipHorizontalWithoutMutatingTheSessionSprite()
	{
		(ProcessedAssetCollection collection, ISprite sprite) = CreateTightTwoBakeFixture(Unity2022_3);
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);

		RectangleF processedRect = sprite.Rect.CastToStruct();
		Vector2 processedPivot = sprite.Pivot is null ? default : (Vector2)sprite.Pivot;
		Vector2 processedOffset = (Vector2)sprite.Offset;
		RectangleF processedTextureRect = sprite.RD.TextureRect.CastToStruct();
		uint processedSettings = sprite.RD.SettingsRaw;

		VirtualFileSystem unpacked = Export(game, SpriteExportMode.YamlUnpacked);
		await Assert.That(TryFindUnpackedImage(unpacked, "TightTwo", sprite.PathID)).IsNull();
		AssertSpriteUnchanged(sprite, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);

		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
		AssertSpriteUnchanged(sprite, processedRect, processedPivot, processedOffset, processedTextureRect, processedSettings);

		string asset = FindAsset(fs, "TightTwo.asset");
		string image = FindUnpackedImage(fs, "TightTwo", sprite.PathID);
		string yaml = fs.File.ReadAllText(asset);
		string guid = ReadMetaGuid(fs.File.ReadAllText(image + ".meta"));
		await Assert.That(yaml).Contains($"guid: {guid}");
		await Assert.That(ReadRect(yaml)).IsEqualTo((0f, 0f, 52f, 36f));
		await Assert.That(ReadTextureRect(yaml)).IsEqualTo((0f, 0f, 52f, 36f));
		await Assert.That(ReadVector(yaml, "m_Pivot")).IsEqualTo((0.23f, 0.71f));
		await Assert.That(ReadVector4(yaml, "m_Border")).IsEqualTo((7f, 6f, 8f, 9f));
		await Assert.That(ReadPixelsToUnits(yaml)).IsEqualTo(64f);
		await Assert.That(ReadSettingsRaw(yaml) & 0x7Fu).IsEqualTo(0u);
		await Assert.That(ReadVector4(yaml, "uvTransform")).IsEqualTo((64f, 0.23f * 52f, 64f, 0.71f * 36f));

		(int width, int height, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(image));
		(_, _, byte[] official) = PngReader.ReadRgba(File.ReadAllBytes(Path.Combine(OfflineRasterDirectory, "TightTwo-Gamma-Point-official.png")));
		await Assert.That((width, height)).IsEqualTo((52, 36));
		await Assert.That(actual.SequenceEqual(official)).IsTrue();
	}

	[Test]
	public async Task YamlBakedExportsTightOneAsIndependentFullRectPng()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(Unity2022_3);
		ITexture2D atlas = CreateRgbaTexture(collection, "TightAtlas", 64, 64, LoadOfflineAtlasTopLeft());
		atlas.TextureSettings_C28.FilterMode = 0;
		ISprite sprite = CreateSprite(
			collection,
			atlas,
			"TightOne",
			new RectangleF(0f, 0f, 40f, 48f),
			new Vector2(0.23f, 0.71f),
			default,
			new RectangleF(0.07612038f, 0.07612038f, 34.847755f, 41.847755f),
			new Vector2(3.0761204f, 2.0761204f),
			65u);
		sprite.PixelsToUnits = 64f;
		sprite.RD.UvTransform!.CopyValues(new Vector4(64f, 6.2f, 64f, 32.079998f));
		sprite.RD.DownscaleMultiplier = 1f;
		AddTightOneVertexMesh(sprite);

		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
		string image = FindUnpackedImage(fs, "TightOne", sprite.PathID);
		string yaml = fs.File.ReadAllText(FindAsset(fs, "TightOne.asset"));
		await Assert.That(yaml).Contains($"guid: {ReadMetaGuid(fs.File.ReadAllText(image + ".meta"))}");
		await Assert.That(ReadRect(yaml)).IsEqualTo((0f, 0f, 40f, 48f));
		await Assert.That(ReadSettingsRaw(yaml) & 0x7Fu).IsEqualTo(0u);
		(int width, int height, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(image));
		(_, _, byte[] official) = PngReader.ReadRgba(File.ReadAllBytes(Path.Combine(OfflineRasterDirectory, "TightOne-Gamma-Point-official.png")));
		await Assert.That((width, height)).IsEqualTo((40, 48));
		await Assert.That(actual.SequenceEqual(official)).IsTrue();
	}

	[Test]
	public async Task YamlBakedFallsBackForUnevidencedAtlasRectOffsetVersions()
	{
		(ProcessedAssetCollection collection, ISprite sprite) = CreateTightTwoBakeFixture(UnityVersion.V_2022);
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			await Assert.That(TryFindUnpackedImage(fs, "TightTwo", sprite.PathID)).IsNull();
			await Assert.That(warnings.Any(w => w.Contains("atlasRectOffset", StringComparison.Ordinal) && w.Contains("2022.3", StringComparison.Ordinal))).IsTrue();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "TightTwo.asset"));
			string atlasGuid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "TightAtlas") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {atlasGuid}");
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedFallsBackForUnevidencedFlipVertical()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(Unity2022_3);
		byte[] pixels = new byte[8 * 8 * 4];
		FillRect(pixels, 8, 0, 0, 8, 8, 1, 2, 3, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "Sheet", 8, 8, pixels);
		uint settings = 1u | (1u << 1) | (2u << 2); // packed, rectangle, FlipVertical
		ISprite sprite = CreateSprite(collection, sheet, "FlippedV", new RectangleF(0, 0, 8, 8), new Vector2(0.5f, 0.5f), default, settingsRaw: settings);
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			await Assert.That(TryFindUnpackedImage(fs, "FlippedV", sprite.PathID)).IsNull();
			await Assert.That(warnings.Any(w => w.Contains("packing rotation 2", StringComparison.Ordinal))).IsTrue();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "FlippedV.asset"));
			string sheetGuid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "Sheet") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {sheetGuid}");
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedFallsBackForMipmappedSources()
	{
		(ProcessedAssetCollection collection, ISprite sprite, _, ITexture2D texture) = CreateTwoSpriteSheet();
		texture.MipCount_C28 = 4;
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			await Assert.That(TryFindUnpackedImage(fs, "Left", sprite.PathID)).IsNull();
			await Assert.That(warnings.Any(w => w.Contains("mip", StringComparison.OrdinalIgnoreCase))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedMissingColorSpaceDefaultsToGammaAndLogsADiagnostic()
	{
		(ProcessedAssetCollection collection, ISprite left, _, _) = CreateTwoSpriteSheet();
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			await Assert.That(TryFindUnpackedImage(fs, "Left", left.PathID)).IsNotNull();
			await Assert.That(warnings.Any(w => w.Contains("m_ActiveColorSpace was not found", StringComparison.Ordinal) && w.Contains("GammaUnorm", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedUsesLinearPlayerSettingsFromRootWhenSpriteIsInAChildBundle()
	{
		(GameBundle root, GameData game, ISprite sprite, SpriteSourceSnapshot snapshot, DirectBitmap atlas) = CreateChildBundleSheet(colorSpaces: [1]);
		await Assert.That(root.FetchAssets().Any(asset => asset.ClassID == 129)).IsTrue();
		await Assert.That(sprite.Collection.Bundle.FetchAssets().Any(asset => asset.ClassID == 129)).IsFalse();
		await Assert.That(sprite.Collection.Bundle.GetRoot()).IsEqualTo(root);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			string image = FindUnpackedImage(fs, "Left", sprite.PathID);
			(_, _, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(image));
			byte[] linear = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.LinearSrgb);
			byte[] gamma = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.GammaUnorm);
			await Assert.That(DiffCount(linear, gamma)).IsGreaterThan(0);
			await Assert.That(DiffCount(actual, linear)).IsEqualTo(0);
			await Assert.That(DiffCount(actual, gamma)).IsGreaterThan(0);
			await Assert.That(warnings.Any(w => w.Contains("was not found", StringComparison.Ordinal) || w.Contains("conflicting", StringComparison.OrdinalIgnoreCase))).IsFalse();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedMissingPlayerSettingsInAChildBundleStillDefaultsToGamma()
	{
		(GameBundle root, GameData game, ISprite sprite, SpriteSourceSnapshot snapshot, DirectBitmap atlas) = CreateChildBundleSheet(colorSpaces: []);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			string image = FindUnpackedImage(fs, "Left", sprite.PathID);
			(_, _, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(image));
			byte[] gamma = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.GammaUnorm);
			await Assert.That(DiffCount(actual, gamma)).IsEqualTo(0);
			await Assert.That(warnings.Any(w => w.Contains("m_ActiveColorSpace was not found", StringComparison.Ordinal) && w.Contains("GammaUnorm", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedAcceptsMultipleConsistentLinearPlayerSettings()
	{
		(GameBundle root, GameData game, ISprite sprite, SpriteSourceSnapshot snapshot, DirectBitmap atlas) = CreateChildBundleSheet(colorSpaces: [1, 1]);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			(_, _, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Left", sprite.PathID)));
			byte[] linear = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.LinearSrgb);
			await Assert.That(DiffCount(actual, linear)).IsEqualTo(0);
			await Assert.That(warnings.Any(w => w.Contains("conflicting", StringComparison.OrdinalIgnoreCase) || w.Contains("was not found", StringComparison.Ordinal))).IsFalse();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedFallsBackWhenPlayerSettingsColorSpacesConflict()
	{
		(GameBundle root, GameData game, ISprite sprite, SpriteSourceSnapshot snapshot, DirectBitmap atlas) = CreateChildBundleSheet(colorSpaces: [0, 1]);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			(_, _, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Left", sprite.PathID)));
			byte[] gamma = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.GammaUnorm);
			byte[] linear = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.LinearSrgb);
			await Assert.That(DiffCount(actual, gamma)).IsEqualTo(0);
			await Assert.That(DiffCount(actual, linear)).IsGreaterThan(0);
			await Assert.That(warnings.Any(w => w.Contains("conflicting PlayerSettings m_ActiveColorSpace", StringComparison.Ordinal) && w.Contains("GammaUnorm", StringComparison.Ordinal))).IsTrue();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedDoesNotPickAKnownValueWhenAnotherPlayerSettingsValueIsUnknown()
	{
		(GameBundle root, GameData game, ISprite sprite, SpriteSourceSnapshot snapshot, DirectBitmap atlas) = CreateChildBundleSheet(colorSpaces: [1, 2]);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			(_, _, byte[] actual) = PngReader.ReadRgba(fs.File.ReadAllBytes(FindUnpackedImage(fs, "Left", sprite.PathID)));
			byte[] gamma = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.GammaUnorm);
			byte[] linear = RasterSnapshot(atlas, snapshot, SpriteOfflineColorSpace.LinearSrgb);
			await Assert.That(DiffCount(actual, gamma)).IsEqualTo(0);
			await Assert.That(DiffCount(actual, linear)).IsGreaterThan(0);
			await Assert.That(warnings.Any(w => w.Contains("could not be determined", StringComparison.Ordinal) || w.Contains("unknown", StringComparison.OrdinalIgnoreCase))).IsTrue();
			await Assert.That(warnings.Any(w => w.Contains("m_ActiveColorSpace is Linear", StringComparison.Ordinal))).IsFalse();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	[Test]
	public async Task YamlBakedFallsBackWhenVertexColorChannelsCannotBeInspected()
	{
		(ProcessedAssetCollection collection, ISprite sprite, _, _) = CreateTwoSpriteSheet();
		var vertexData = sprite.RD.VertexData!;
		vertexData.Channels!.Clear();
		vertexData.Channels.AddNew();
		// A non-zero vertex count with a truncated channel list makes GetChannel throw.
		// VertexCount == 0 is the ordinary empty-mesh FullRect case and must still bake.
		vertexData.VertexCount = 4;
		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fs = Export(game, SpriteExportMode.YamlBaked);
			await Assert.That(TryFindUnpackedImage(fs, "Left", sprite.PathID)).IsNull();
			await Assert.That(warnings.Any(w => w.Contains("vertex color channels could not be inspected", StringComparison.Ordinal))).IsTrue();
			string yaml = fs.File.ReadAllText(FindAsset(fs, "Left.asset"));
			string sheetGuid = ReadMetaGuid(fs.File.ReadAllText(FindImage(fs, "Sheet") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {sheetGuid}");
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	private static (ProcessedAssetCollection Collection, ISprite Sprite) CreateTightTwoBakeFixture(UnityVersion version)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(version);
		ITexture2D atlasTexture = CreateRgbaTexture(collection, "TightAtlas", 64, 64, LoadOfflineAtlasTopLeft());
		atlasTexture.TextureSettings_C28.FilterMode = 0;
		ISprite sprite = CreateSprite(
			collection,
			atlasTexture,
			"TightTwo",
			new RectangleF(0f, 0f, 52f, 36f),
			new Vector2(0.23f, 0.71f),
			new Vector4(7f, 6f, 8f, 9f),
			new RectangleF(26.07612f, 32.07612f, 35.910965f, 29.84776f),
			new Vector2(3.0761204f, 2.0761204f),
			69u);
		sprite.PixelsToUnits = TightTwoPpu;
		sprite.RD.UvTransform!.CopyValues(new Vector4(64f, 34.96f, 64f, 55.559998f));
		sprite.RD.DownscaleMultiplier = TightTwoDownscale;
		AddTightTwoVertexMesh(sprite);
		var mapping = AddAtlasMapping(collection, sprite, atlasTexture);
		mapping.TextureRect.CopyValues(sprite.RD.TextureRect);
		mapping.TextureRectOffset.CopyValues(sprite.RD.TextureRectOffset);
		mapping.SettingsRaw = 69;
		mapping.DownscaleMultiplier = TightTwoDownscale;
		mapping.UvTransform.CopyValues(sprite.RD.UvTransform);
		mapping.AtlasRectOffset!.CopyValues(new Vector2(8.959999f, 37.559998f));
		return (collection, sprite);
	}

	private static void AddTightOneVertexMesh(ISprite sprite)
	{
		// TightOne typeless positions from the official offline fixture (fixtures.json).
		Vector2[] positions = LoadOfflineVertices("TightOne");
		var vertexData = sprite.RD.VertexData!;
		for (int i = 0; i < ShaderChannelExtensions.GetChannelCount(sprite.Collection.Version); i++)
		{
			vertexData.Channels!.AddNew();
		}

		vertexData.Channels![0].Dimension = 3;
		int uvIndex = ShaderChannel.UV0.ToChannel(sprite.Collection.Version);
		vertexData.Channels[uvIndex].Dimension = 2;
		vertexData.Channels[uvIndex].Offset = 12;
		vertexData.VertexCount = (uint)positions.Length;
		using MemoryStream data = new();
		using (BinaryWriter writer = new(data, Encoding.UTF8, leaveOpen: true))
		{
			for (int i = 0; i < positions.Length; i++)
			{
				writer.Write(positions[i].X);
				writer.Write(positions[i].Y);
				writer.Write(0f);
				writer.Write(0f);
				writer.Write(0f);
			}
		}

		vertexData.Data = data.ToArray();
		int[] indices = LoadOfflineIndices("TightOne");
		var submesh = sprite.RD.SubMeshes!.AddNew();
		submesh.VertexCount = (uint)positions.Length;
		submesh.IndexCount = (uint)indices.Length;
		using MemoryStream indexStream = new();
		using (BinaryWriter writer = new(indexStream, Encoding.UTF8, leaveOpen: true))
		{
			foreach (int index in indices)
			{
				writer.Write((ushort)index);
			}
		}

		sprite.RD.IndexBuffer = indexStream.ToArray();
	}

	private static byte[] LoadOfflineAtlasTopLeft()
	{
		(_, _, byte[] rgba) = PngReader.ReadRgba(File.ReadAllBytes(Path.Combine(OfflineRasterDirectory, "atlas-TightAtlas-export.png")));
		return rgba;
	}

	private static Vector2[] LoadOfflineVertices(string spriteName)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(OfflineRasterDirectory, "fixtures.json")));
		foreach (JsonElement sprite in document.RootElement.GetProperty("sprites").EnumerateArray())
		{
			if (sprite.GetProperty("name").GetString() != spriteName)
			{
				continue;
			}

			return [.. sprite.GetProperty("vertices").EnumerateArray().Select(vertex =>
			{
				float[] xy = vertex.EnumerateArray().Select(e => e.GetSingle()).ToArray();
				return new Vector2(xy[0], xy[1]);
			})];
		}

		throw new InvalidOperationException(spriteName);
	}

	private static int[] LoadOfflineIndices(string spriteName)
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(OfflineRasterDirectory, "fixtures.json")));
		foreach (JsonElement sprite in document.RootElement.GetProperty("sprites").EnumerateArray())
		{
			if (sprite.GetProperty("name").GetString() == spriteName)
			{
				return [.. sprite.GetProperty("indices").EnumerateArray().Select(e => e.GetInt32())];
			}
		}

		throw new InvalidOperationException(spriteName);
	}

	private static float ReadPixelsToUnits(string yaml)
	{
		Match match = Regex.Match(yaml, @"(?:m_)?PixelsToUnits:\s*([^\r\n]+)");
		if (!match.Success)
		{
			throw new InvalidOperationException("Could not read pixelsToUnits from the YAML asset.\n" + yaml);
		}

		return Parse(match.Groups[1].Value);
	}

	private static uint ReadSettingsRaw(string yaml)
	{
		Match match = Regex.Match(yaml, @"settingsRaw:\s*(\d+)");
		if (!match.Success)
		{
			throw new InvalidOperationException("Could not read settingsRaw from the YAML asset.\n" + yaml);
		}

		return uint.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
	}

	private static (GameBundle Root, GameData Game, ISprite Sprite, SpriteSourceSnapshot Snapshot, DirectBitmap Atlas) CreateChildBundleSheet(int[] colorSpaces)
	{
		GameBundle root = new();
		ProcessedAssetCollection managers = root.AddNewProcessedCollection("globalgamemanagers", UnityVersion.V_2022);
		for (int i = 0; i < colorSpaces.Length; i++)
		{
			AddPlayerSettings(managers, colorSpaces[i]);
		}

		ProcessedBundle child = root.AddNewProcessedBundle("fixtureassets");
		ProcessedAssetCollection collection = child.AddNewProcessedCollection("sprites", UnityVersion.V_2022);
		byte[] pixels = new byte[16 * 8 * 4];
		FillRect(pixels, 16, 0, 0, 8, 8, 255, 0, 0, 255);
		FillRect(pixels, 16, 8, 0, 8, 8, 0, 0, 255, 255);
		SetPixel(pixels, 16, 0, 0, 0, 0, 0, 255);
		SetPixel(pixels, 16, 7, 0, 255, 255, 255, 255);
		ITexture2D sheet = CreateRgbaTexture(collection, "Sheet", 16, 8, pixels);
		sheet.TextureSettings_C28.FilterMode = 1;
		// textureRect is half a texel off-grid so bilinear actually mixes neighbors.
		// A 1:1 mapping would sample texel centers, making Linear and Gamma 0-diff.
		ISprite left = CreateSprite(
			collection,
			sheet,
			"Left",
			new RectangleF(0, 0, 8, 8),
			new Vector2(0.25f, 0.75f),
			new Vector4(1, 2, 3, 4),
			textureRect: new RectangleF(0.5f, 0.5f, 8f, 8f));
		GameData game = CreateGameData(root, collection.Version);
		new SpriteProcessor().Process(game);
		if (!SpriteSourceSnapshotStore.TryGet(left, out SpriteSourceSnapshot? snapshot))
		{
			throw new InvalidOperationException("snapshot missing");
		}

		if (!TextureConverter.TryConvertToBitmap(sheet, out DirectBitmap atlas) || atlas.IsEmpty)
		{
			throw new InvalidOperationException("atlas decode failed");
		}

		return (root, game, left, snapshot, atlas);
	}

	private static void AddPlayerSettings(ProcessedAssetCollection collection, int activeColorSpace)
	{
		TypeTree tree = new();
		tree.Nodes.Add(new TypeTreeNode("PlayerSettings", "Base", 0, true));
		tree.Nodes.Add(new TypeTreeNode("int", "m_ActiveColorSpace", 1, false));
		if (!TypeTreeNodeStruct.TryMakeFromTypeTree(tree, out TypeTreeNodeStruct root))
		{
			throw new InvalidOperationException("could not build PlayerSettings type tree");
		}

		TypeTreeObject settings = collection.CreateAsset(129, info => TypeTreeObject.Create(info, root, ITypeResolver.Null));
		settings.EditorFields["m_ActiveColorSpace"].AsInt32 = activeColorSpace;
	}

	private static GameData CreateGameData(GameBundle bundle, UnityVersion version)
	{
		return new(bundle, version, new BaseManager(_ => { }), null);
	}

	private static byte[] RasterSnapshot(DirectBitmap atlas, SpriteSourceSnapshot snapshot, SpriteOfflineColorSpace colorSpace)
	{
		float width = snapshot.Rect.Width;
		float height = snapshot.Rect.Height;
		float pivotPx = snapshot.Pivot.X * width;
		float pivotPy = snapshot.Pivot.Y * height;
		float ppu = snapshot.PixelsToUnits;
		Vector2[] vertices =
		[
			new((0f - pivotPx) / ppu, (0f - pivotPy) / ppu),
			new((width - pivotPx) / ppu, (0f - pivotPy) / ppu),
			new((width - pivotPx) / ppu, (height - pivotPy) / ppu),
			new((0f - pivotPx) / ppu, (height - pivotPy) / ppu),
		];
		int[] indices = [0, 1, 2, 0, 2, 3];
		SpriteOfflineRasterRequest request = new(
			snapshot.Rect.Width,
			snapshot.Rect.Height,
			snapshot.PixelsToUnits,
			snapshot.Pivot,
			snapshot.TextureRect.X,
			snapshot.TextureRect.Y,
			snapshot.TextureRect.Width,
			snapshot.TextureRect.Height,
			snapshot.TextureRectOffset,
			snapshot.UvTransform!.Value,
			snapshot.DownscaleMultiplier ?? 1f,
			snapshot.PackingRotation,
			vertices,
			indices,
			SpriteOfflineFilter.Bilinear,
			colorSpace);
		if (!SpriteOfflineRasterizer.TryRasterize(atlas, request, out DirectBitmap? canvas, out string? reason) || canvas is null)
		{
			throw new InvalidOperationException(reason);
		}

		return ToExportRgba((DirectBitmap<ColorRGBA<byte>, byte>)canvas);
	}

	private static byte[] ToExportRgba(DirectBitmap<ColorRGBA<byte>, byte> bitmap)
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

	private static int DiffCount(byte[] left, byte[] right)
	{
		if (left.Length != right.Length)
		{
			return Math.Max(left.Length, right.Length) / 4;
		}

		int different = 0;
		for (int i = 0; i < left.Length; i += 4)
		{
			if (left[i] != right[i] || left[i + 1] != right[i + 1] || left[i + 2] != right[i + 2] || left[i + 3] != right[i + 3])
			{
				different++;
			}
		}

		return different;
	}

}
