using System.Text;
using System.Text.Json;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;

namespace AssetRipper.CLI.Tests;

internal class ListAndSearchTests
{
	[Test]
	public async Task ListAssetsReturnsLoadedAssetsFromInput()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		JsonElement greeting = FindItem(data.RootElement.GetProperty("items"), "Greeting");
		await Assert.That(greeting.GetProperty("type").GetString()).IsEqualTo("TextAsset");
		await Assert.That(greeting.GetProperty("ref").GetString()).IsNotEmpty();
	}


	[Test]
	public async Task ListRequiresInput()
	{
		CliResult result = Cli.Run(["list", "assets"]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--input");
	}

	[Test]
	public async Task MissingInputIsNotFound()
	{
		VirtualFileSystem fileSystem = new();
		CliResult result = Cli.Run(["list", "assets", "--input", "/missing-game"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
	}

	[Test]
	public async Task TypeFilterIsAUsageErrorOnNonAssetListKinds()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", "bundles", "--input", "/game", "--type", "TextAsset"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--type");
	}

	[Test]
	public async Task NameGlobAndTypeFilterNarrowListAssets()
	{
		VirtualFileSystem fileSystem = CreateGame(null, "/game", "test.assets", ["Greeting", "PlayerIcon", "EnemyIcon"]);
		CliResult result = Cli.Run(["list", "assets", "--input", "/game", "--name", "*Icon", "--type", "TextAsset"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		List<string> names = [.. data.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
		await Assert.That(names).IsEquivalentTo(["EnemyIcon", "PlayerIcon"]);
	}


	[Test]
	public async Task ListDefaultsToLimit100AndIncludesTruncationFields()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", "assets", "--input", "/game"], fileSystem: fileSystem);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		JsonElement root = data.RootElement;
		await Assert.That(root.GetProperty("items").GetArrayLength()).IsEqualTo(100);
		await Assert.That(root.GetProperty("truncated").GetBoolean()).IsTrue();
		string? next = root.GetProperty("next").GetString();
		await Assert.That(next).IsNotNull();
		await Assert.That(next!).IsNotEmpty();
		string? hint = root.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		await Assert.That(hint!).Contains("--cursor");
	}

	[Test]
	public async Task CursorContinuesATruncatedListWithoutRepeatingItems()
	{
		VirtualFileSystem fileSystem = CreateGame(null, "/game", "test.assets", ["EnemyIcon", "PlayerIcon"]);
		CliResult first = Cli.Run(["list", "assets", "--input", "/game", "--name", "*Icon", "--limit", "1"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		using JsonDocument firstData = JsonDocument.Parse(first.Stdout);
		await Assert.That(firstData.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(1);
		await Assert.That(firstData.RootElement.GetProperty("truncated").GetBoolean()).IsTrue();
		string firstName = firstData.RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
		await Assert.That(firstName).IsEqualTo("EnemyIcon");
		string? next = firstData.RootElement.GetProperty("next").GetString();
		await Assert.That(next).IsNotNull();

		Rewind(fileSystem, "/game/test.assets");
		CliResult second = Cli.Run(["list", "assets", "--input", "/game", "--name", "*Icon", "--limit", "1", "--cursor", next!], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(0);
		using JsonDocument secondData = JsonDocument.Parse(second.Stdout);
		await Assert.That(secondData.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(1);
		string secondName = secondData.RootElement.GetProperty("items")[0].GetProperty("name").GetString()!;
		await Assert.That(secondName).IsEqualTo("PlayerIcon");
		await Assert.That(secondData.RootElement.TryGetProperty("truncated", out _)).IsFalse();
	}

	[Test]
	public async Task AssetReferenceIsStableForTheLifeOfTheProcess()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], fileSystem: fileSystem);
		Rewind(fileSystem, "/game/test.assets");
		CliResult second = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], fileSystem: fileSystem);
		using JsonDocument firstData = JsonDocument.Parse(first.Stdout);
		using JsonDocument secondData = JsonDocument.Parse(second.Stdout);
		string? firstRef = FindItem(firstData.RootElement.GetProperty("items"), "Greeting").GetProperty("ref").GetString();
		string? secondRef = FindItem(secondData.RootElement.GetProperty("items"), "Greeting").GetProperty("ref").GetString();
		await Assert.That(firstRef).IsNotNull();
		await Assert.That(firstRef).IsEqualTo(secondRef);
	}


	[Test]
	public async Task SearchMatchesAssetNamesCaseInsensitively()
	{
		VirtualFileSystem fileSystem = CreateGame(null, "/game", "test.assets", ["Greeting", "PlayerIcon", "EnemyIcon"]);
		CliResult result = Cli.Run(["search", "gReEtInG", "--input", "/game"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		List<string> names = [.. data.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
		await Assert.That(names).IsEquivalentTo(["Greeting"]);
		JsonElement greeting = FindItem(data.RootElement.GetProperty("items"), "Greeting");
		await Assert.That(greeting.GetProperty("type").GetString()).IsEqualTo("TextAsset");
		await Assert.That(greeting.GetProperty("ref").GetString()).IsNotEmpty();
	}

	[Test]
	public async Task SearchUsesTheSameLimitAndCursorRulesAsList()
	{
		VirtualFileSystem fileSystem = CreateGame(null, "/game", "test.assets", ["EnemyIcon", "PlayerIcon"]);
		CliResult first = Cli.Run(["search", "icon", "--input", "/game", "--limit", "1"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		using JsonDocument firstData = JsonDocument.Parse(first.Stdout);
		await Assert.That(firstData.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(1);
		await Assert.That(firstData.RootElement.GetProperty("truncated").GetBoolean()).IsTrue();
		await Assert.That(firstData.RootElement.GetProperty("items")[0].GetProperty("name").GetString()).IsEqualTo("EnemyIcon");
		string? next = firstData.RootElement.GetProperty("next").GetString();
		await Assert.That(next).IsNotNull();
		await Assert.That(firstData.RootElement.GetProperty("hint").GetString()).Contains("--cursor");

		Rewind(fileSystem, "/game/test.assets");
		CliResult second = Cli.Run(["search", "icon", "--input", "/game", "--limit", "1", "--cursor", next!], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(0);
		using JsonDocument secondData = JsonDocument.Parse(second.Stdout);
		await Assert.That(secondData.RootElement.GetProperty("items")[0].GetProperty("name").GetString()).IsEqualTo("PlayerIcon");
		await Assert.That(secondData.RootElement.TryGetProperty("truncated", out _)).IsFalse();
	}

	[Test]
	public async Task SearchRequiresAQueryAndInput()
	{
		CliResult missingQuery = Cli.Run(["search", "--input", "/game"]);
		await Assert.That(missingQuery.ExitCode).IsEqualTo(2);
		await Assert.That(missingQuery.Stdout).IsEmpty();

		CliResult missingInput = Cli.Run(["search", "Greeting"]);
		await Assert.That(missingInput.ExitCode).IsEqualTo(2);
		await Assert.That(missingInput.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(missingInput.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--input");
	}


	[Test]
	[Arguments("bundles", "GameBundle")]
	[Arguments("collections", "test.assets")]
	public async Task ListKindsReturnTheCorrespondingLoadedItems(string kind, string expectedName)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", kind, "--input", "/game"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		List<string> names = [.. data.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
		await Assert.That(names).Contains(expectedName);
	}

	[Test]
	[Arguments("scenes")]
	[Arguments("resources")]
	public async Task ListKindsWithNoMatchingItemsStillSucceed(string kind)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", kind, "--input", "/game"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(0);
		await Assert.That(data.RootElement.TryGetProperty("truncated", out _)).IsFalse();
	}

	[Test]
	public async Task ListFailedFilesReturnsUnreadableLoadedFiles()
	{
		VirtualFileSystem fileSystem = CreateGame();
		byte[] valid = fileSystem.File.ReadAllBytes("/game/test.assets");
		byte[] broken = (byte[])valid.Clone();
		for (int i = broken.Length / 2; i < broken.Length; i++)
		{
			broken[i] ^= 0xFF;
		}
		fileSystem.File.WriteAllBytes("/game/broken.assets", broken);
		Rewind(fileSystem, "/game/test.assets");
		Rewind(fileSystem, "/game/broken.assets");
		CliResult result = Cli.Run(["list", "failed-files", "--input", "/game/test.assets", "--input", "/game/broken.assets", "--name", "broken*"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		List<string> names = [.. data.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
		await Assert.That(names).IsEquivalentTo(["broken.assets"]);
	}

	[Test]
	public async Task NameGlobAppliesToEveryListKind()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["list", "collections", "--input", "/game", "--name", "test*"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		List<string> names = [.. data.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
		await Assert.That(names).IsEquivalentTo(["test.assets"]);
	}

	[Test]
	public async Task ListDefaultsToTextOnATtyAndJsonCanBeForced()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult text = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting"], stdoutIsTty: true, fileSystem: fileSystem);
		await Assert.That(text.ExitCode).IsEqualTo(0);
		await Assert.That(text.Stdout).Contains("Greeting");
		await Assert.That(text.Stdout.TrimStart().StartsWith('{')).IsFalse();

		Rewind(fileSystem, "/game/test.assets");
		CliResult json = Cli.Run(["list", "assets", "--input", "/game", "--name", "Greeting", "--json"], stdoutIsTty: true, fileSystem: fileSystem);
		await Assert.That(json.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(json.Stdout);
		await Assert.That(FindItem(data.RootElement.GetProperty("items"), "Greeting").GetProperty("type").GetString()).IsEqualTo("TextAsset");
	}

	[Test]
	public async Task InvalidLimitAndCursorAreUsageErrors()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult limit = Cli.Run(["list", "assets", "--input", "/game", "--limit", "0"], fileSystem: fileSystem);
		await Assert.That(limit.ExitCode).IsEqualTo(2);
		await Assert.That(limit.Stdout).IsEmpty();

		Rewind(fileSystem, "/game/test.assets");
		CliResult cursor = Cli.Run(["list", "assets", "--input", "/game", "--cursor", "not-a-token"], fileSystem: fileSystem);
		await Assert.That(cursor.ExitCode).IsEqualTo(2);
		await Assert.That(cursor.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(cursor.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--cursor");
	}

	private static JsonElement FindItem(JsonElement items, string name)
	{
		return items.EnumerateArray().First(item => item.GetProperty("name").GetString() == name);
	}

	private static VirtualFileSystem CreateGame(VirtualFileSystem? fileSystem = null, string directory = "/game", string assetName = "Greeting", string fileName = "test.assets")
	{
		return CreateGame(fileSystem, directory, fileName, [assetName]);
	}

	private static VirtualFileSystem CreateGame(VirtualFileSystem? fileSystem, string directory, string fileName, IReadOnlyList<string> assetNames)
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
		long fileId = 1;
		foreach (string assetName in assetNames)
		{
			using MemoryStream objectData = new();
			using (BinaryWriter writer = new(objectData, Encoding.UTF8, leaveOpen: true))
			{
				WriteAligned(writer, assetName);
				WriteAligned(writer, "Hello from CLI fixture!");
			}
			builder.Objects.Add(new ObjectInfo(type) { FileID = fileId, SerializedTypeIndex = 0, ObjectData = objectData.ToArray() });
			fileId++;
		}
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
