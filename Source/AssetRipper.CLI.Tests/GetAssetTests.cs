using AssetRipper.Assets;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.SourceGenerated.Classes.ClassID_134;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Extensions;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;

namespace AssetRipper.CLI.Tests;

internal class GetAssetTests
{
	[Test]
	[Arguments("Infinity")]
	[Arguments("-Infinity")]
	[Arguments("NaN")]
	public async Task JsonEncodesNonFiniteFloatsAsStringsWithoutChangingFiniteNumbersOrText(string special)
	{
		IPhysicsMaterial material = AssetCreator.CreatePhysicsMaterial(AssetRipper.Primitives.UnityVersion.Parse("2022.3.0f1"));
		material.Name = "Infinity -Infinity NaN \"literal\"";
		material.Bounciness = float.Parse(special, CultureInfo.InvariantCulture);
		material.DynamicFriction = 0.25f;
		VirtualFileSystem fileSystem = CreateAssetFixture(material);
		CliResult list = Cli.Run(["list", "assets", "--input", "/game", "--type", "PhysicMaterial"], fileSystem: fileSystem);
		await Assert.That(list.ExitCode).IsEqualTo(0);
		using JsonDocument listed = JsonDocument.Parse(list.Stdout);
		string reference = listed.RootElement.GetProperty("items")[0].GetProperty("ref").GetString()!;
		Rewind(fileSystem, "/game/numbers.assets");
		CliResult result = Cli.Run(["get", "asset", reference, "--input", "/game", "--format", "json"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument payload = JsonDocument.Parse(result.Stdout);
		await Assert.That(payload.RootElement.GetProperty("m_Bounciness").GetString()).IsEqualTo(special);
		await Assert.That(payload.RootElement.GetProperty("m_DynamicFriction").GetSingle()).IsEqualTo(0.25f);
		await Assert.That(payload.RootElement.GetProperty("m_Name").GetString()).IsEqualTo("Infinity -Infinity NaN \"literal\"");
	}

	[Test]
	[Arguments("Infinity")]
	[Arguments("-Infinity")]
	[Arguments("NaN")]
	public async Task JsonEncodesNonFiniteDoublesAsStrings(string special)
	{
		IVideoClip clip = AssetCreator.CreateVideoClip(AssetRipper.Primitives.UnityVersion.Parse("2022.3.0f1"));
		clip.Name = "DoubleFixture";
		clip.FrameRate = double.Parse(special, CultureInfo.InvariantCulture);
		VirtualFileSystem fileSystem = CreateAssetFixture(clip);
		CliResult list = Cli.Run(["list", "assets", "--input", "/game", "--name", "DoubleFixture"], fileSystem: fileSystem);
		await Assert.That(list.ExitCode).IsEqualTo(0);
		using JsonDocument listed = JsonDocument.Parse(list.Stdout);
		string reference = listed.RootElement.GetProperty("items")[0].GetProperty("ref").GetString()!;
		Rewind(fileSystem, "/game/numbers.assets");
		CliResult result = Cli.Run(["get", "asset", reference, "--input", "/game", "--format", "json"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument payload = JsonDocument.Parse(result.Stdout);
		await Assert.That(payload.RootElement.GetProperty("m_FrameRate").GetString()).IsEqualTo(special);
	}

	private static VirtualFileSystem CreateAssetFixture(IUnityObjectBase asset)
	{
		VirtualFileSystem fileSystem = new();
		fileSystem.Directory.Create("/game");
		using MemoryStream objectData = new();
		AssetWriter writer = new(objectData, asset.Collection);
		asset.WriteRelease(writer);
		writer.Flush();
		SerializedType type = new() { TypeID = asset.ClassID, ScriptTypeIndex = -1 };
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = asset.Collection.Version,
			EndianType = EndianType.LittleEndian,
			Platform = BuildTarget.StandaloneWin64Player,
		};
		builder.Types.Add(type);
		builder.Objects.Add(new ObjectInfo(type) { FileID = 1, SerializedTypeIndex = 0, ObjectData = objectData.ToArray() });
		using Stream stream = fileSystem.File.Create("/game/numbers.assets");
		builder.Build().Write(stream);
		stream.Position = 0;
		return fileSystem;
	}

	[Test]
	public async Task InvalidFormatIsAUsageErrorThatListsLegalValues()
	{
		CliResult result = Cli.Run(["get", "asset", "collection:1", "--input", "/game", "--format", "not-a-format"]);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		string? hint = error.RootElement.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		foreach (string format in new[] { "json", "yaml", "text", "binary", "image", "audio", "model", "font", "video" })
		{
			await Assert.That(hint!).Contains(format);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SameNamedCollectionsHaveDistinctReferencesThatRetrieveEachAsset(bool useSession)
	{
		string first = SessionTests.CreateGame("FirstGreeting");
		string second = SessionTests.CreateGame("OtherGreeting");
		string? session = null;
		try
		{
			string[] source = ["--input", first, "--input", second];
			if (useSession)
			{
				CliResult start = Cli.Run(["session", "start", .. source]);
				await Assert.That(start.ExitCode).IsEqualTo(0);
				using JsonDocument started = JsonDocument.Parse(start.Stdout);
				session = started.RootElement.GetProperty("id").GetString()!;
				source = ["--session", session];
			}
			CliResult listed = Cli.Run(["list", "assets", .. source, "--type", "TextAsset"]);
			await Assert.That(listed.ExitCode).IsEqualTo(0);
			using JsonDocument list = JsonDocument.Parse(listed.Stdout);
			JsonElement items = list.RootElement.GetProperty("items");
			await Assert.That(items.GetArrayLength()).IsEqualTo(2);
			string firstReference = FindItem(items, "FirstGreeting").GetProperty("ref").GetString()!;
			string otherReference = FindItem(items, "OtherGreeting").GetProperty("ref").GetString()!;
			await Assert.That(firstReference).IsNotEqualTo(otherReference);
			CliResult searched = Cli.Run(["search", "Greeting", .. source]);
			await Assert.That(searched.ExitCode).IsEqualTo(0);
			using JsonDocument search = JsonDocument.Parse(searched.Stdout);
			foreach (string name in new[] { "FirstGreeting", "OtherGreeting" })
			{
				string reference = FindItem(items, name).GetProperty("ref").GetString()!;
				await Assert.That(FindItem(search.RootElement.GetProperty("items"), name).GetProperty("ref").GetString()).IsEqualTo(reference);
				CliResult got = Cli.Run(["get", "asset", reference, .. source]);
				await Assert.That(got.ExitCode).IsEqualTo(0);
				using JsonDocument payload = JsonDocument.Parse(got.Stdout);
				await Assert.That(payload.RootElement.GetProperty("m_Name").GetString()).IsEqualTo(name);
			}
		}
		finally
		{
			if (session is not null) Cli.Run(["session", "close", session]);
			Directory.Delete(first, true);
			Directory.Delete(second, true);
		}
	}

	[Test]
	public async Task GetUsesListReturnedReferenceAndWritesJsonPayloadToStdout()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult listed = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], fileSystem: fileSystem);
		await Assert.That(listed.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(listed.Stdout);
		string assetRef = FindItem(data.RootElement.GetProperty("items"), "Greeting").GetProperty("ref").GetString()!;
		await Assert.That(assetRef).IsNotEmpty();

		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["get", "asset", assetRef, "--input", "/game"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument payload = JsonDocument.Parse(result.Stdout);
		await Assert.That(result.Stdout).Contains("Hello from CLI fixture!");
		await Assert.That(result.Stdout).Contains("Greeting");
	}

	[Test]
	public async Task MissingAssetReferenceExitsFive()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["get", "asset", "missing-collection:999", "--input", "/game"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = ParseJsonObject(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
		string? hint = error.RootElement.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		await Assert.That(hint!).Contains("list assets");
	}

	[Test]
	public async Task GetWritesPayloadToOutputAndJsonStdoutIsMetadataOnly()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		Rewind(fileSystem, "/game/test.assets");

		CliResult result = Cli.Run(
			["get", "asset", assetRef, "--input", "/game", "--output", "/out/greeting.json"],
			fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(fileSystem.File.Exists("/out/greeting.json")).IsTrue();
		string payload = fileSystem.File.ReadAllText("/out/greeting.json");
		using JsonDocument fileJson = JsonDocument.Parse(payload);
		await Assert.That(payload).Contains("Hello from CLI fixture!");

		using JsonDocument metadata = JsonDocument.Parse(result.Stdout);
		JsonElement root = metadata.RootElement;
		await Assert.That(root.GetProperty("path").GetString()).IsEqualTo("/out/greeting.json");
		await Assert.That(root.GetProperty("format").GetString()).IsEqualTo("json");
		await Assert.That(root.GetProperty("size").GetInt64()).IsEqualTo(Encoding.UTF8.GetByteCount(payload));
		await Assert.That(result.Stdout).DoesNotContain("Hello from CLI fixture!");
	}


	[Test]
	public async Task DefaultFormatIsJson()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		Rewind(fileSystem, "/game/test.assets");
		CliResult omitted = Cli.Run(["get", "asset", assetRef, "--input", "/game"], fileSystem: fileSystem);
		Rewind(fileSystem, "/game/test.assets");
		CliResult explicitJson = Cli.Run(["get", "asset", assetRef, "--input", "/game", "--format", "json"], fileSystem: fileSystem);

		await Assert.That(omitted.ExitCode).IsEqualTo(0);
		await Assert.That(explicitJson.ExitCode).IsEqualTo(0);
		await Assert.That(omitted.Stdout).IsEqualTo(explicitJson.Stdout);
		using JsonDocument payload = JsonDocument.Parse(omitted.Stdout);
		await Assert.That(omitted.Stdout).Contains("Hello from CLI fixture!");
	}

	[Test]
	public async Task UnsupportedFormatForAssetExitsOneAndListsSupportedFormats()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["get", "asset", assetRef, "--input", "/game", "--format", "image"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = ParseJsonObject(result.Stderr);
		string? hint = error.RootElement.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		foreach (string format in new[] { "json", "yaml", "text" })
		{
			await Assert.That(hint!).Contains(format);
		}
		foreach (string format in new[] { "image", "audio", "model", "font", "video" })
		{
			await Assert.That(hint!).DoesNotContain(format);
		}
	}

	[Test]
	public async Task ExistingOutputIsRefusedWithoutConfirm()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		fileSystem.Directory.Create("/out");
		fileSystem.File.WriteAllText("/out/greeting.json", "do-not-overwrite");
		Rewind(fileSystem, "/game/test.assets");

		CliResult result = Cli.Run(
			["get", "asset", assetRef, "--input", "/game", "--output", "/out/greeting.json"],
			fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(fileSystem.File.ReadAllText("/out/greeting.json")).IsEqualTo("do-not-overwrite");
		using JsonDocument error = ParseJsonObject(result.Stderr);
		string? hint = error.RootElement.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		await Assert.That(hint!).Contains("--confirm");
	}

	[Test]
	public async Task ConfirmOverwritesAnExistingOutputFile()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		fileSystem.Directory.Create("/out");
		fileSystem.File.WriteAllText("/out/greeting.json", "do-not-keep");
		Rewind(fileSystem, "/game/test.assets");

		CliResult result = Cli.Run(
			["get", "asset", assetRef, "--input", "/game", "--output", "/out/greeting.json", "--confirm", "greeting.json"],
			fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		string payload = fileSystem.File.ReadAllText("/out/greeting.json");
		using JsonDocument fileJson = JsonDocument.Parse(payload);
		await Assert.That(payload).Contains("Hello from CLI fixture!");
		using JsonDocument metadata = JsonDocument.Parse(result.Stdout);
		await Assert.That(metadata.RootElement.GetProperty("path").GetString()).IsEqualTo("/out/greeting.json");
	}

	[Test]
	public async Task TextFormatWritesDecodedTextToStdout()
	{
		VirtualFileSystem fileSystem = CreateGame();
		string assetRef = ListGreetingRef(fileSystem);
		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["get", "asset", assetRef, "--input", "/game", "--format", "text"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout.TrimEnd()).IsEqualTo("Hello from CLI fixture!");
	}

	[Test]
	public async Task MissingInputIsNotFound()
	{
		VirtualFileSystem fileSystem = new();
		CliResult result = Cli.Run(["get", "asset", "collection:1", "--input", "/missing-game"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = ParseJsonObject(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
	}

	[Test]
	public async Task GetHelpDocumentsFormatsAndExitCodes()
	{
		CliResult result = Cli.Run(["get", "asset", "--help"]);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("Usage: assetripper get asset");
		await Assert.That(result.Stdout).Contains("--format");
		foreach (string format in new[] { "json", "yaml", "text", "binary", "image", "audio", "model", "font", "video" })
		{
			await Assert.That(result.Stdout).Contains(format);
		}
		await Assert.That(result.Stdout).Contains("EXAMPLES");
		await Assert.That(result.Stdout).Contains("EXIT CODES");
		await Assert.That(result.Stdout).Contains("Asset Reference");
	}

	private static JsonDocument ParseJsonObject(string text)
	{
		foreach (string line in text.Split('\n').Reverse())
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith('{'))
			{
				return JsonDocument.Parse(trimmed);
			}
		}

		return JsonDocument.Parse(text);
	}

	private static string ListGreetingRef(VirtualFileSystem fileSystem)
	{
		CliResult listed = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], fileSystem: fileSystem);
		if (listed.ExitCode != 0)
		{
			throw new InvalidOperationException(listed.Stderr);
		}
		using JsonDocument data = JsonDocument.Parse(listed.Stdout);
		return FindItem(data.RootElement.GetProperty("items"), "Greeting").GetProperty("ref").GetString()!;
	}

	private static JsonElement FindItem(JsonElement items, string name)
	{
		return items.EnumerateArray().First(item => item.GetProperty("name").GetString() == name);
	}

	private static VirtualFileSystem CreateGame(VirtualFileSystem? fileSystem = null, string directory = "/game", string assetName = "Greeting", string fileName = "test.assets")
	{
		fileSystem ??= new();
		fileSystem.Directory.Create(directory);
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = AssetRipper.Primitives.UnityVersion.Parse("2022.3.0f1"),
			EndianType = EndianType.LittleEndian,
			Platform = BuildTarget.StandaloneWin64Player,
		};
		SerializedType type = new() { TypeID = 49, ScriptTypeIndex = -1 };
		builder.Types.Add(type);
		using MemoryStream objectData = new();
		using (BinaryWriter writer = new(objectData, Encoding.UTF8, leaveOpen: true))
		{
			WriteAligned(writer, assetName);
			WriteAligned(writer, "Hello from CLI fixture!");
		}
		builder.Objects.Add(new ObjectInfo(type) { FileID = 1, SerializedTypeIndex = 0, ObjectData = objectData.ToArray() });
		using Stream stream = fileSystem.File.Create(Path.Combine(directory, fileName));
		builder.Build().Write(stream);
		stream.Position = 0;
		return fileSystem;
	}

	private static void Rewind(VirtualFileSystem fileSystem, string path)
	{
		using Stream stream = fileSystem.File.OpenRead(path);
		stream.Position = 0;
	}

	private static void WriteAligned(BinaryWriter writer, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		writer.Write(bytes.Length);
		writer.Write(bytes);
		while (writer.BaseStream.Position % 4 != 0)
		{
			writer.Write((byte)0);
		}
	}
}
