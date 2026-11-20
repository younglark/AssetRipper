using AssetRipper.Assets.IO.Writing;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.NativeEnums.Fmod;
using System.Text;
using System.Text.Json;

namespace AssetRipper.CLI.Tests;

internal class GetAudioTests
{
	private const string ClipName = "SyntheticImaTone";
	private const int SampleCount = 64;
	private const int Frequency = 44100;
	private const int PcmByteCount = SampleCount * 2;
	private const int WavFileLength = 44 + PcmByteCount;
	private const uint ExpectedRiffSize = WavFileLength - 8;
	private const uint ExpectedDataSize = PcmByteCount;

	[Test]
	public async Task GetAudioForImaAdpcmFsbWritesWavWithCorrectChunkSizesAndUnchangedPcm()
	{
		byte[] fsb = CreateMinimalMonoImaAdpcmFsb(SampleCount, frequencyId: 8);
		VirtualFileSystem fileSystem = CreateAudioClipFixture(
			UnityVersion.Parse("2022.3.0f1"),
			ClipName,
			fsb,
			resourceName: "ima-clip.resource",
			configure: clip =>
			{
				clip.CompressionFormat = 2; // Unity AudioCompressionFormat.ADPCM
				if (clip.Has_Frequency())
				{
					clip.Frequency = Frequency;
				}
				if (clip.Has_Channels())
				{
					clip.Channels = 1;
				}
				if (clip.Has_Length())
				{
					clip.Length = SampleCount / (float)Frequency;
				}
			});

		Rewind(fileSystem, "/game/ima-clip.assets");
		Rewind(fileSystem, "/game/ima-clip.resource");
		string reference = ListClipRef(fileSystem, ClipName);
		Rewind(fileSystem, "/game/ima-clip.assets");
		Rewind(fileSystem, "/game/ima-clip.resource");
		fileSystem.Directory.Create("/out");

		CliResult result = Cli.Run(
			["get", "asset", reference, "--input", "/game", "--format", "audio", "--output", "/out/clip.wav"],
			fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		byte[] wav = fileSystem.File.ReadAllBytes("/out/clip.wav");
		using JsonDocument metadata = JsonDocument.Parse(result.Stdout);
		await Assert.That(metadata.RootElement.GetProperty("format").GetString()).IsEqualTo("audio");
		await Assert.That(metadata.RootElement.GetProperty("size").GetInt64()).IsEqualTo(wav.Length);

		await Assert.That(wav.Length).IsEqualTo(WavFileLength);
		await Assert.That(Encoding.ASCII.GetString(wav, 0, 4)).IsEqualTo("RIFF");
		await Assert.That(Encoding.ASCII.GetString(wav, 8, 4)).IsEqualTo("WAVE");
		await Assert.That(BitConverter.ToUInt32(wav, 4)).IsEqualTo(ExpectedRiffSize);
		await Assert.That(BitConverter.ToUInt16(wav, 20)).IsEqualTo((ushort)1); // PCM format tag
		await Assert.That(BitConverter.ToUInt16(wav, 22)).IsEqualTo((ushort)1); // mono
		await Assert.That(BitConverter.ToUInt32(wav, 24)).IsEqualTo((uint)Frequency);
		await Assert.That(BitConverter.ToUInt16(wav, 34)).IsEqualTo((ushort)16); // bits per sample
		await Assert.That(Encoding.ASCII.GetString(wav, 36, 4)).IsEqualTo("data");
		await Assert.That(BitConverter.ToUInt32(wav, 40)).IsEqualTo(ExpectedDataSize);
		// Independent expected PCM: a silent 64-sample Xbox IMA frame (zero nibbles) decodes to 128 zero bytes.
		await Assert.That(wav.AsSpan(44).ToArray()).IsEquivalentTo(new byte[PcmByteCount]);
	}


	[Test]
	public async Task GetAudioForMultiFrameImaFsbCorrectsChunkSizesLargerThanOneByte()
	{
		const int sampleCount = 192; // 3 Xbox IMA frames; PCM 384 bytes so RIFF size 420 does not fit in one byte
		const int pcmByteCount = sampleCount * 2;
		const int wavFileLength = 44 + pcmByteCount;
		byte[] fsb = CreateMinimalMonoImaAdpcmFsb(sampleCount, frequencyId: 8);
		VirtualFileSystem fileSystem = CreateAudioClipFixture(
			UnityVersion.Parse("2022.3.0f1"),
			"SyntheticImaMultiFrame",
			fsb,
			resourceName: "ima-multi.resource",
			configure: clip =>
			{
				clip.CompressionFormat = 2;
				clip.Frequency = Frequency;
				clip.Channels = 1;
				clip.Length = sampleCount / (float)Frequency;
			},
			assetFileName: "ima-multi.assets");
		Rewind(fileSystem, "/game/ima-multi.assets");
		Rewind(fileSystem, "/game/ima-multi.resource");
		string reference = ListClipRef(fileSystem, "SyntheticImaMultiFrame");
		Rewind(fileSystem, "/game/ima-multi.assets");
		Rewind(fileSystem, "/game/ima-multi.resource");
		fileSystem.Directory.Create("/out");
		CliResult result = Cli.Run(
			["get", "asset", reference, "--input", "/game", "--format", "audio", "--output", "/out/multi.wav"],
			fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		byte[] wav = fileSystem.File.ReadAllBytes("/out/multi.wav");
		await Assert.That(wav.Length).IsEqualTo(wavFileLength);
		await Assert.That(BitConverter.ToUInt32(wav, 4)).IsEqualTo((uint)(wavFileLength - 8));
		await Assert.That(BitConverter.ToUInt32(wav, 40)).IsEqualTo((uint)pcmByteCount);
		await Assert.That(wav.AsSpan(44).ToArray()).IsEquivalentTo(new byte[pcmByteCount]);
	}


	[Test]
	public async Task GetAudioLeavesAValidWavUnchanged()
	{
		byte[] wav = CreatePcmWav(pcm: Repeat(0xAB, 16), formatChunkLength: 16, riffSize: 16 + 8 + 8 + 16, dataSize: 16);
		byte[] exported = await ExportLegacyClipAudio("ValidWav", wav, FmodSoundType.Wav);
		await Assert.That(exported).IsEquivalentTo(wav);
	}

	[Test]
	public async Task GetAudioLeavesOggBytesUnchanged()
	{
		// Minimal original Ogg capture prefix, not a game asset. The Has_Type path returns raw bytes.
		byte[] ogg = "OggS"u8.ToArray().Concat(Repeat(0x11, 24)).ToArray();
		byte[] exported = await ExportLegacyClipAudio("ValidOgg", ogg, FmodSoundType.Oggvorbis);
		await Assert.That(exported).IsEquivalentTo(ogg);
	}

	[Test]
	public async Task GetAudioLeavesZeroSizesOnAn18ByteFmtChunkUnchanged()
	{
		// Legacy/raw WAV is passed through. An 18-byte fmt is not the Fmod5Sharp IMA
		// rebuild layout, even when both size fields happen to be zero.
		byte[] pcm = Repeat(0xCD, 8);
		byte[] wav = CreatePcmWav(pcm, formatChunkLength: 18, riffSize: 0, dataSize: 0);
		await Assert.That(BitConverter.ToUInt32(wav, 4)).IsEqualTo(0u);
		int dataAt = 12 + 8 + 18;
		await Assert.That(Encoding.ASCII.GetString(wav, dataAt, 4)).IsEqualTo("data");
		await Assert.That(BitConverter.ToUInt32(wav, dataAt + 4)).IsEqualTo(0u);

		byte[] exported = await ExportLegacyClipAudio("Fmt18ZeroSizes", wav, FmodSoundType.Wav);
		await Assert.That(exported).IsEquivalentTo(wav);
	}

	[Test]
	public async Task GetAudioDoesNotRewriteNonZeroInconsistentWavSizes()
	{
		byte[] pcm = Repeat(0xEF, 8);
		byte[] wav = CreatePcmWav(pcm, formatChunkLength: 16, riffSize: 99, dataSize: 3);
		byte[] exported = await ExportLegacyClipAudio("InconsistentSizes", wav, FmodSoundType.Wav);
		await Assert.That(exported).IsEquivalentTo(wav);
	}

	[Test]
	public async Task GetAudioLeavesEmptyDataChunkFollowedByListUnchanged()
	{
		// Legal WAV: empty data chunk (size 0) followed by a LIST chunk. A fixer that
		// treats "data size 0 means payload runs to EOF" would swallow the LIST.
		byte[] wav = CreateWavWithEmptyDataAndTrailingList();
		await Assert.That(BitConverter.ToUInt32(wav, 4)).IsEqualTo((uint)(wav.Length - 8));
		await Assert.That(Encoding.ASCII.GetString(wav, 36, 4)).IsEqualTo("data");
		await Assert.That(BitConverter.ToUInt32(wav, 40)).IsEqualTo(0u);
		await Assert.That(Encoding.ASCII.GetString(wav, 44, 4)).IsEqualTo("LIST");
		byte[] exported = await ExportLegacyClipAudio("EmptyDataPlusList", wav, FmodSoundType.Wav);
		await Assert.That(exported).IsEquivalentTo(wav);
	}

	[Test]
	public async Task GetAudioDoesNotMutateSourceAssetBytesAcrossTwoExports()
	{
		// Legacy raw WAV whose zero sizes would be rewritten in place if the shared
		// decoder mutated audioClip.AudioData (the same array GetAudioData returns).
		// A Session keeps the loaded clip, so a second read observes that mutation.
		byte[] wav = CreatePcmWav(pcm: Repeat(0xAB, 16), formatChunkLength: 16, riffSize: 0, dataSize: 0);
		const string name = "MutateCheckWav";
		VirtualFileSystem fileSystem = CreateAudioClipFixture(
			UnityVersion.Parse("4.6.0f1"),
			name,
			wav,
			resourceName: null,
			configure: clip => clip.Type = (int)FmodSoundType.Wav,
			assetFileName: "legacy-clip.assets");
		string input = Path.Combine(Path.GetTempPath(), "assetripper-audio-mutate-" + Guid.NewGuid().ToString("N"));
		string? id = null;
		try
		{
			Directory.CreateDirectory(input);
			byte[] assetsBytes = fileSystem.File.ReadAllBytes("/game/legacy-clip.assets");
			File.WriteAllBytes(Path.Combine(input, "legacy-clip.assets"), assetsBytes);
			CliResult start = Cli.Run(["session", "start", "--input", input]);
			await Assert.That(start.ExitCode).IsEqualTo(0);
			using JsonDocument started = JsonDocument.Parse(start.Stdout);
			id = started.RootElement.GetProperty("id").GetString()!;

			CliResult listed = Cli.Run(["list", "assets", "--session", id, "--name", name, "--type", "AudioClip"]);
			await Assert.That(listed.ExitCode).IsEqualTo(0);
			using JsonDocument items = JsonDocument.Parse(listed.Stdout);
			string reference = items.RootElement.GetProperty("items").EnumerateArray()
				.First(item => item.GetProperty("name").GetString() == name)
				.GetProperty("ref").GetString()!;

			CliResult jsonBefore = Cli.Run(["get", "asset", reference, "--session", id, "--format", "json"]);
			await Assert.That(jsonBefore.ExitCode).IsEqualTo(0);

			string firstPath = Path.Combine(input, "first.wav");
			CliResult first = Cli.Run(["get", "asset", reference, "--session", id, "--format", "audio", "--output", firstPath]);
			await Assert.That(first.ExitCode).IsEqualTo(0);

			CliResult jsonAfterFirst = Cli.Run(["get", "asset", reference, "--session", id, "--format", "json"]);
			await Assert.That(jsonAfterFirst.ExitCode).IsEqualTo(0);
			await Assert.That(jsonAfterFirst.Stdout).IsEqualTo(jsonBefore.Stdout);

			string secondPath = Path.Combine(input, "second.wav");
			CliResult second = Cli.Run(["get", "asset", reference, "--session", id, "--format", "audio", "--output", secondPath]);
			await Assert.That(second.ExitCode).IsEqualTo(0);

			CliResult jsonAfterSecond = Cli.Run(["get", "asset", reference, "--session", id, "--format", "json"]);
			await Assert.That(jsonAfterSecond.ExitCode).IsEqualTo(0);
			await Assert.That(jsonAfterSecond.Stdout).IsEqualTo(jsonBefore.Stdout);

			byte[] firstExport = File.ReadAllBytes(firstPath);
			byte[] secondExport = File.ReadAllBytes(secondPath);
			await Assert.That(firstExport).IsEquivalentTo(wav);
			await Assert.That(secondExport).IsEquivalentTo(wav);
			await Assert.That(File.ReadAllBytes(Path.Combine(input, "legacy-clip.assets"))).IsEquivalentTo(assetsBytes);
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}
			if (Directory.Exists(input))
			{
				Directory.Delete(input, true);
			}
		}
	}


	private static async Task<byte[]> ExportLegacyClipAudio(string name, byte[] audioBytes, FmodSoundType soundType)
	{
		const string assetFileName = "legacy-clip.assets";
		VirtualFileSystem fileSystem = CreateAudioClipFixture(
			UnityVersion.Parse("4.6.0f1"),
			name,
			audioBytes,
			resourceName: null,
			configure: clip => clip.Type = (int)soundType,
			assetFileName: assetFileName);
		string assetPath = "/game/" + assetFileName;
		Rewind(fileSystem, assetPath);
		string reference = ListClipRef(fileSystem, name);
		Rewind(fileSystem, assetPath);
		fileSystem.Directory.Create("/out");
		string output = "/out/" + name + ".bin";
		CliResult result = Cli.Run(
			["get", "asset", reference, "--input", "/game", "--format", "audio", "--output", output],
			fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		return fileSystem.File.ReadAllBytes(output);
	}

	private static byte[] CreatePcmWav(byte[] pcm, int formatChunkLength, uint riffSize, uint dataSize)
	{
		if (formatChunkLength is not 16 and not 18)
		{
			throw new ArgumentOutOfRangeException(nameof(formatChunkLength));
		}

		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write("RIFF"u8);
		writer.Write(riffSize);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(formatChunkLength);
		writer.Write((ushort)1); // PCM
		writer.Write((ushort)1); // mono
		writer.Write(44100);
		writer.Write(88200);
		writer.Write((ushort)2);
		writer.Write((ushort)16);
		if (formatChunkLength == 18)
		{
			writer.Write((ushort)0);
		}
		writer.Write("data"u8);
		writer.Write(dataSize);
		writer.Write(pcm);
		return stream.ToArray();
	}

	private static byte[] CreateWavWithEmptyDataAndTrailingList()
	{
		byte[] fmtAndEmptyData = CreatePcmWav(pcm: [], formatChunkLength: 16, riffSize: 0, dataSize: 0);
		using MemoryStream stream = new();
		stream.Write(fmtAndEmptyData);
		using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write("LIST"u8);
		writer.Write(8);
		writer.Write("INFOTEST"u8);
		byte[] wav = stream.ToArray();
		BitConverter.TryWriteBytes(wav.AsSpan(4, 4), (uint)(wav.Length - 8));
		return wav;
	}


	private static byte[] Repeat(byte value, int count)
	{
		byte[] data = new byte[count];
		Array.Fill(data, value);
		return data;
	}

	private static string ListClipRef(VirtualFileSystem fileSystem, string name)
	{
		CliResult listed = Cli.Run(["list", "assets", "--input", "/game", "--name", name, "--type", "AudioClip"], fileSystem: fileSystem);
		if (listed.ExitCode != 0)
		{
			throw new InvalidOperationException(listed.Stderr);
		}
		using JsonDocument data = JsonDocument.Parse(listed.Stdout);
		return data.RootElement.GetProperty("items").EnumerateArray()
			.First(item => item.GetProperty("name").GetString() == name)
			.GetProperty("ref").GetString()!;
	}

	private static VirtualFileSystem CreateAudioClipFixture(
		UnityVersion version,
		string name,
		byte[] audioBytes,
		string? resourceName,
		Action<IAudioClip>? configure = null,
		string assetFileName = "ima-clip.assets")
	{
		IAudioClip clip = AssetCreator.CreateAudioClip(version);
		clip.Name = name;
		configure?.Invoke(clip);

		if (clip.Has_Resource())
		{
			ArgumentException.ThrowIfNullOrEmpty(resourceName);
			clip.Resource.Source = resourceName;
			clip.Resource.Offset = 0;
			clip.Resource.Size = (ulong)audioBytes.Length;
		}
		else if (clip.Has_AudioData())
		{
			clip.AudioData = audioBytes;
		}
		else
		{
			throw new InvalidOperationException($"AudioClip at {version} has neither Resource nor AudioData.");
		}

		VirtualFileSystem fileSystem = new();
		fileSystem.Directory.Create("/game");
		using MemoryStream objectData = new();
		AssetWriter writer = new(objectData, clip.Collection);
		clip.WriteRelease(writer);
		writer.Flush();
		SerializedType type = new() { TypeID = clip.ClassID, ScriptTypeIndex = -1 };
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = version,
			EndianType = EndianType.LittleEndian,
			Platform = BuildTarget.StandaloneWin64Player,
		};
		builder.Types.Add(type);
		builder.Objects.Add(new ObjectInfo(type) { FileID = 1, SerializedTypeIndex = 0, ObjectData = objectData.ToArray() });
		string assetPath = "/game/" + assetFileName;
		using (Stream stream = fileSystem.File.Create(assetPath))
		{
			builder.Build().Write(stream);
			stream.Position = 0;
		}

		if (clip.Has_Resource())
		{
			fileSystem.File.WriteAllBytes("/game/" + resourceName, audioBytes);
		}

		Rewind(fileSystem, assetPath);
		if (resourceName is not null)
		{
			Rewind(fileSystem, "/game/" + resourceName);
		}
		return fileSystem;
	}

	private static void Rewind(VirtualFileSystem fileSystem, string path)
	{
		if (!fileSystem.File.Exists(path))
		{
			return;
		}
		using Stream stream = fileSystem.File.OpenRead(path);
		stream.Position = 0;
	}

	/// <summary>
	/// Builds a one-sample FSB5 IMA ADPCM bank. Layout follows Fmod5Sharp 3.1.0
	/// (header version 1 is 0x3C bytes, then one packed 8-byte sample header, then Xbox IMA frames of 0x24 bytes).
	/// Frequency id 8 is 44100 Hz; channel bits 0 is mono. This is original test data, not a game asset.
	/// </summary>
	internal static byte[] CreateMinimalMonoImaAdpcmFsb(int sampleCount, uint frequencyId)
	{
		if (sampleCount <= 0 || sampleCount % 64 != 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleCount), "Xbox IMA frames hold 64 samples each.");
		}

		int frames = sampleCount / 64;
		byte[] data = new byte[frames * 0x24];
		const uint hasChunks = 0;
		const uint channelBits = 0;
		const uint dataOffsetUnits = 0;
		ulong encoded = hasChunks
			| ((frequencyId & 0xF) << 1)
			| ((channelBits & 0x3) << 5)
			| ((dataOffsetUnits & 0x7FFFFFFUL) << 7)
			| (((ulong)(uint)sampleCount & 0x3FFFFFFFUL) << 34);

		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write("FSB5"u8);
		writer.Write(1u); // version
		writer.Write(1u); // num samples
		writer.Write(8u); // sample header size
		writer.Write(0u); // name table size
		writer.Write((uint)data.Length);
		writer.Write(7u); // IMAADPCM
		writer.Write(0u); // reserved
		writer.Write(0u); // flags
		writer.Write(0UL); // hash
		writer.Write(0UL);
		writer.Write(0UL); // unknown
		writer.Write(encoded);
		writer.Write(data);
		return stream.ToArray();
	}
}
