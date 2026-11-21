using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.IO.Files;
using AssetRipper.Export.UnityProjects.Textures;
using AssetRipper.Import.Logging;
using AssetRipper.Primitives;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.ShaderChannel;
using System.Drawing;
using System.Numerics;
using System.Text;

namespace AssetRipper.Tests;

/// <summary>
/// TightTwo field and native float32 UV regressions.
/// Numbers are copied from assetripper-unity-roundtrip/ida-sprite-uv/tighttwo-replay/{observations,results}.json.
/// This does not claim a Unity render acceptance or allow TightTwo unpacking.
/// </summary>
internal partial class YamlUnpackedSpriteExportTests
{
	// Observed packed atlas size and serialized/API float32 fields.
	private const float TightTwoWidth = 64f;
	private const float TightTwoHeight = 64f;
	private const float TightTwoPpu = 64f;
	private const float TightTwoDownscale = 1f;

	[Test]
	public async Task TightTwoPositiveStoredScaleMatchesUnrotatedMappingAndIsAcceptedWithFlipHorizontal()
	{
		Vector2 pivot = new(0.23f, 0.71f);
		RectangleF rect = new(0f, 0f, 52f, 36f);
		RectangleF textureRect = new(26.07612f, 32.07612f, 35.910965f, 29.84776f);
		Vector2 textureRectOffset = new(3.0761204f, 2.0761204f);
		Vector4 stored = new(64f, 34.96f, 64f, 55.559998f);

		Vector4 expected = SpriteStoredUvTransform.UnrotatedPixelMapping(TightTwoPpu, pivot, rect, textureRect, textureRectOffset);
		await Assert.That(MathF.Abs(expected.X - stored.X) <= SpriteStoredUvTransform.Tolerance).IsTrue();
		await Assert.That(MathF.Abs(expected.Y - stored.Y) <= SpriteStoredUvTransform.Tolerance).IsTrue();
		await Assert.That(MathF.Abs(expected.Z - stored.Z) <= SpriteStoredUvTransform.Tolerance).IsTrue();
		await Assert.That(MathF.Abs(expected.W - stored.W) <= SpriteStoredUvTransform.Tolerance).IsTrue();

		await Assert.That(SpriteStoredUvTransform.IsAccepted(
			stored,
			1,
			TightTwoPpu,
			pivot,
			rect,
			textureRect,
			textureRectOffset)).IsTrue();

		// The old "FlipHorizontal must store a negative X scale" expectation does not match TightTwo.
		float x = pivot.X * rect.Width - textureRectOffset.X;
		Vector4 bakedNegativeScale = new(
			-TightTwoPpu,
			textureRect.X + textureRect.Width - x,
			TightTwoPpu,
			textureRect.Y + (pivot.Y * rect.Height - textureRectOffset.Y));
		await Assert.That(MathF.Abs(bakedNegativeScale.X - stored.X) > SpriteStoredUvTransform.Tolerance).IsTrue();

		// Existing integer-grid tests still write that baked-flip encoding; keep accepting it.
		await Assert.That(SpriteStoredUvTransform.IsAccepted(
			bakedNegativeScale,
			1,
			TightTwoPpu,
			pivot,
			rect,
			textureRect,
			textureRectOffset)).IsTrue();

		Vector4 wrongScale = stored;
		wrongScale.X = 123f;
		await Assert.That(SpriteStoredUvTransform.IsAccepted(
			wrongScale,
			1,
			TightTwoPpu,
			pivot,
			rect,
			textureRect,
			textureRectOffset)).IsFalse();
	}

	[Test]
	public async Task TightTwoNativeFloat32ReplayMatchesAllSevenObservedPackedUvs()
	{
		// Instruction order from tighttwo-replay/replay.py: separate scalar f32 ops, no algebraic simplification.
		Vector2 pivot = new(0.23f, 0.71f);
		Vector2 rectSize = new(52f, 36f);
		Vector4 textureRect = new(26.07612f, 32.07612f, 35.910965f, 29.84776f);
		Vector2 offset = new(3.0761204f, 2.0761204f);
		Vector4 transform = new(64f, 34.96f, 64f, 55.559998f);

		(Vector2 Vertex, Vector2 PackedUv)[] vertices =
		[
			(new(0.40687501430511475f, 0.10062500834465027f), new(0.42286252975463867f, 0.96875f)),
			(new(0.0006249994039535522f, -0.36812499165534973f), new(0.8291125297546387f, 0.4999999701976776f)),
			(new(0.42250001430511475f, 0.05375000834465027f), new(0.40723752975463867f, 0.921875f)),
			(new(-0.10875000059604645f, 0.10062500834465027f), new(0.9384875297546387f, 0.96875f)),
			(new(-0.10875000059604645f, -0.36812499165534973f), new(0.9384875297546387f, 0.4999999701976776f)),
			(new(-0.14000000059604645f, 0.06937500834465027f), new(0.9697375297546387f, 0.9375f)),
			(new(-0.14000000059604645f, -0.33687499165534973f), new(0.9697375297546387f, 0.53125f)),
		];

		await Assert.That(vertices.Length).IsEqualTo(7);
		Vector2[] positions = vertices.Select(v => v.Vertex).ToArray();
		Vector2[] predicted = new Vector2[positions.Length];
		await Assert.That(SpriteNativeUv.TryRecalculate(
			positions,
			(int)TightTwoWidth,
			(int)TightTwoHeight,
			rectSize.X,
			rectSize.Y,
			pivot,
			textureRect.X,
			textureRect.Y,
			textureRect.Z,
			textureRect.W,
			offset,
			transform,
			TightTwoDownscale,
			SpriteNativeUv.RotationFlipHorizontal,
			predicted,
			out string? reason)).IsTrue();
		await Assert.That(reason).IsNull();
		for (int i = 0; i < vertices.Length; i++)
		{
			await Assert.That(predicted[i]).IsEqualTo(vertices[i].PackedUv);
		}
	}

	[Test]
	public async Task TightTwoStillFallsBackForUnverifiedAtlasRectOffset()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(UnityVersion.V_2022);
		ITexture2D atlasTexture = CreateRgbaTexture(collection, "TightAtlas", 64, 64, new byte[64 * 64 * 4]);
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

		GameData game = CreateGameData(collection);
		new SpriteProcessor().Process(game);
		await Assert.That(SpriteSourceSnapshotStore.TryGet(sprite, out SpriteSourceSnapshot? snapshot)).IsTrue();
		await Assert.That(snapshot!.PackingRotation).IsEqualTo(1);
		await Assert.That(snapshot.UvTransform).IsEqualTo((Vector4?)new Vector4(64f, 34.96f, 64f, 55.559998f));
		await Assert.That(SpriteStoredUvTransform.IsAccepted(
			snapshot.UvTransform!.Value,
			snapshot.PackingRotation,
			snapshot.PixelsToUnits,
			snapshot.Pivot,
			snapshot.Rect,
			snapshot.TextureRect,
			snapshot.TextureRectOffset)).IsTrue();

		List<string> warnings = [];
		CollectingLogger logger = new(warnings);
		Logger.Add(logger);
		try
		{
			VirtualFileSystem fileSystem = Export(game, SpriteExportMode.YamlUnpacked);
			await Assert.That(TryFindUnpackedImage(fileSystem, "TightTwo", sprite.PathID)).IsNull();
			string yaml = fileSystem.File.ReadAllText(FindAsset(fileSystem, "TightTwo.asset"));
			string atlasGuid = ReadMetaGuid(fileSystem.File.ReadAllText(FindImage(fileSystem, "TightAtlas") + ".meta"));
			await Assert.That(yaml).Contains($"guid: {atlasGuid}");
			await Assert.That(warnings.Any(message =>
				message.Contains("atlasRectOffset", StringComparison.Ordinal)
				&& message.Contains("unverified version semantics", StringComparison.Ordinal)
				&& message.Contains("Falling back", StringComparison.Ordinal))).IsTrue();
			await Assert.That(warnings.Any(message => message.Contains("uvTransform does not match", StringComparison.Ordinal))).IsFalse();
		}
		finally
		{
			Logger.Remove(logger);
		}
	}

	private static void AddTightTwoVertexMesh(ISprite sprite)
	{
		Vector2[] positions =
		[
			new(0.40687501430511475f, 0.10062500834465027f),
			new(0.0006249994039535522f, -0.36812499165534973f),
			new(0.42250001430511475f, 0.05375000834465027f),
			new(-0.10875000059604645f, 0.10062500834465027f),
			new(-0.10875000059604645f, -0.36812499165534973f),
			new(-0.14000000059604645f, 0.06937500834465027f),
			new(-0.14000000059604645f, -0.33687499165534973f),
		];
		Vector2[] uvs =
		[
			new(0.42286252975463867f, 0.96875f),
			new(0.8291125297546387f, 0.4999999701976776f),
			new(0.40723752975463867f, 0.921875f),
			new(0.9384875297546387f, 0.96875f),
			new(0.9384875297546387f, 0.4999999701976776f),
			new(0.9697375297546387f, 0.9375f),
			new(0.9697375297546387f, 0.53125f),
		];
		ushort[] indices = [6, 5, 4, 3, 4, 5, 1, 4, 3, 0, 1, 3, 2, 1, 0];

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
				writer.Write(uvs[i].X);
				writer.Write(uvs[i].Y);
			}
		}
		vertexData.Data = data.ToArray();
		var submesh = sprite.RD.SubMeshes!.AddNew();
		submesh.VertexCount = (uint)positions.Length;
		submesh.IndexCount = (uint)indices.Length;
		using MemoryStream indexStream = new();
		using (BinaryWriter writer = new(indexStream, Encoding.UTF8, leaveOpen: true))
		{
			foreach (ushort index in indices)
			{
				writer.Write(index);
			}
		}
		sprite.RD.IndexBuffer = indexStream.ToArray();
	}
}
