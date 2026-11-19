using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.Modules.Audio;
using AssetRipper.Export.Modules.Models;
using AssetRipper.Export.Modules.Textures;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Export.UnityProjects.Scripts;
using AssetRipper.Export.UnityProjects.Shaders;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.Structure.Assembly;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Processing;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_156;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.Yaml;
using SharpGLTF.Scenes;
using System.Globalization;
using System.Text;

namespace AssetRipper.CLI;

internal static class GetAssetFormat
{
	public static IReadOnlyList<string> SupportedFormats(IUnityObjectBase asset)
	{
		List<string> formats = ["json", "yaml"];
		if (HasText(asset))
		{
			formats.Add("text");
		}
		if (HasBinary(asset))
		{
			formats.Add("binary");
		}
		if (HasImage(asset))
		{
			formats.Add("image");
		}
		if (HasAudio(asset))
		{
			formats.Add("audio");
		}
		if (HasModel(asset))
		{
			formats.Add("model");
		}
		if (HasFont(asset))
		{
			formats.Add("font");
		}
		if (HasVideo(asset))
		{
			formats.Add("video");
		}
		return formats;
	}

	public static bool TryEncode(IUnityObjectBase asset, string format, GameData gameData, [NotNullWhen(true)] out byte[]? payload)
	{
		payload = format switch
		{
			"json" => EncodeText(WriteJson(asset)),
			"yaml" => EncodeText(WriteYaml(asset)),
			"text" => HasText(asset) ? EncodeText(WriteText(asset, gameData.AssemblyManager)) : null,
			"binary" => HasBinary(asset) ? ((RawDataObject)asset).RawData : null,
			"image" => TryEncodeImage(asset),
			"audio" => TryEncodeAudio(asset),
			"model" => TryEncodeModel(asset),
			"font" => TryEncodeFont(asset),
			"video" => TryEncodeVideo(asset),
			_ => null,
		};
		return payload is not null;
	}

	private static byte[] EncodeText(string text) => Encoding.UTF8.GetBytes(text);

	private static string WriteJson(IUnityObjectBase asset)
	{
		StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
		asset.WalkStandard(new DefaultJsonWalker(writer));
		return writer.ToString();
	}

	private static string WriteYaml(IUnityObjectBase asset)
	{
		using StringWriter writer = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
		YamlWriter yamlWriter = new();
		yamlWriter.WriteHead(writer);
		YamlDocument document = new YamlWalker().ExportYamlDocument(asset, ExportIdHandler.GetMainExportID(asset));
		yamlWriter.WriteDocument(document);
		yamlWriter.WriteTail(writer);
		return writer.ToString();
	}

	private static bool HasText(IUnityObjectBase asset)
	{
		return asset is IShader or IMonoScript or ITextAsset { Script_C49.IsEmpty: false };
	}

	private static string WriteText(IUnityObjectBase asset, IAssemblyManager assemblyManager)
	{
		return asset switch
		{
			IShader shader => shader.Has_Script() && !shader.Script.IsEmpty ? shader.Script.String : DumpShader(shader),
			IMonoScript monoScript => Decompile(monoScript, assemblyManager),
			ITextAsset textAsset => textAsset.Script_C49.String,
			_ => "",
		};
	}

	private static string DumpShader(IShader shader)
	{
		InvariantStringWriter writer = new();
		DummyShaderTextExporter.ExportShader(shader, writer);
		return writer.ToString();
	}

	private static string Decompile(IMonoScript monoScript, IAssemblyManager assemblyManager)
	{
		if (!monoScript.IsScriptPresents(assemblyManager))
		{
			return EmptyScript.GetContent(monoScript);
		}

		try
		{
			_ = monoScript.GetTypeDefinition(assemblyManager);
			return EmptyScript.GetContent(monoScript);
		}
		catch (Exception exception)
		{
			return exception.ToString();
		}
	}

	private static bool HasBinary(IUnityObjectBase asset)
	{
		return asset is RawDataObject { RawData.Length: > 0 };
	}

	private static bool HasImage(IUnityObjectBase asset) => asset switch
	{
		IImageTexture texture => texture.CheckAssetIntegrity(),
		SpriteInformationObject spriteInformationObject => spriteInformationObject.Texture.CheckAssetIntegrity(),
		ISprite sprite => SpriteConverter.Supported(sprite),
		ITerrainData terrainData => terrainData.Heightmap.Heights.Count > 0,
		_ => false,
	};

	private static byte[]? TryEncodeImage(IUnityObjectBase asset)
	{
		DirectBitmap image = asset switch
		{
			IImageTexture texture => TextureToBitmap(texture),
			SpriteInformationObject spriteInformationObject => TextureToBitmap(spriteInformationObject.Texture),
			ISprite sprite => SpriteToBitmap(sprite),
			ITerrainData terrainData => TerrainHeatmap.GetBitmap(terrainData),
			_ => DirectBitmap.Empty,
		};
		if (image.IsEmpty)
		{
			return null;
		}

		MemoryStream stream = new();
		image.Save(stream, ImageExportFormat.Png);
		return stream.ToArray();
	}

	private static DirectBitmap TextureToBitmap(IImageTexture texture)
	{
		return TextureConverter.TryConvertToBitmap(texture, out DirectBitmap bitmap) ? bitmap : DirectBitmap.Empty;
	}

	private static DirectBitmap SpriteToBitmap(ISprite sprite)
	{
		return SpriteConverter.TryConvertToBitmap(sprite, out DirectBitmap bitmap) ? bitmap : DirectBitmap.Empty;
	}

	private static bool HasAudio(IUnityObjectBase asset) => asset is IAudioClip;

	private static byte[]? TryEncodeAudio(IUnityObjectBase asset)
	{
		return asset is IAudioClip clip && AudioClipDecoder.TryDecode(clip, out byte[]? data, out _, out _)
			? data
			: null;
	}

	private static bool HasModel(IUnityObjectBase asset) => asset is IMesh;

	private static byte[]? TryEncodeModel(IUnityObjectBase asset)
	{
		if (asset is not IMesh mesh)
		{
			return null;
		}

		try
		{
			SceneBuilder sceneBuilder = GlbMeshBuilder.Build(mesh);
			MemoryStream stream = new();
			return GlbWriter.TryWrite(sceneBuilder, stream, out _) ? stream.ToArray() : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool HasFont(IUnityObjectBase asset)
	{
		return asset is IFont font && font.FontData is { Length: >= 4 };
	}

	private static byte[]? TryEncodeFont(IUnityObjectBase asset)
	{
		return HasFont(asset) ? ((IFont)asset).FontData : null;
	}

	private static bool HasVideo(IUnityObjectBase asset)
	{
		return asset is IVideoClip clip && clip.CheckIntegrity();
	}

	private static byte[]? TryEncodeVideo(IUnityObjectBase asset)
	{
		return asset is IVideoClip clip && clip.TryGetContent(out byte[]? content) ? content : null;
	}

	private sealed class InvariantStringWriter : StringWriter
	{
		public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;
	}
}
