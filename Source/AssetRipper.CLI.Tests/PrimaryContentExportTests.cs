using System.Text;
using System.Text.Json;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;

namespace AssetRipper.CLI.Tests;

internal class PrimaryContentExportTests
{
	private const string FixturePath = "Assets/TextAsset/Greeting.bytes";
	private const string FixtureOutput = "/output/Assets/TextAsset/Greeting.bytes";
	private const string FixtureText = "Hello from CLI fixture!";

	[Test]
	public async Task OutputIsRequired()
	{
		CliResult result = Cli.Run(["export", "primary-content", "--input", "Game"]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--output");
	}

	[Test]
	[Arguments("Desktop")]
	[Arguments("documents")]
	[Arguments("DownLoads/")]
	public async Task ProtectedOutputDirectoriesAreUsageErrors(string name)
	{
		CliResult result = Cli.Run(["export", "primary-content", "--input", "missing-game", "--output", Path.Combine(Path.GetTempPath(), name)]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	public async Task MissingInputIsNotFoundAndDoesNotCreateOutput()
	{
		VirtualFileSystem fileSystem = new();
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/missing-game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
	}

	[Test]
	public async Task LoadsARealSerializedFileAndExportsPrimaryContent()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game/test.assets", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(data.RootElement.GetProperty("output").GetString()).IsEqualTo("/output");
		await Assert.That(data.RootElement.TryGetProperty("project", out _)).IsFalse();
		await Assert.That(fileSystem.File.ReadAllText(FixtureOutput)).IsEqualTo(FixtureText);
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
		await Assert.That(result.Stderr).Contains("Finished reading files");
		await Assert.That(result.Stdout).DoesNotContain("Finished reading files");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DryRunPreviewsFilesWithoutChangingDisk(bool nonEmpty)
	{
		VirtualFileSystem fileSystem = CreateGame();
		if (nonEmpty)
		{
			fileSystem.Directory.Create("/output");
			fileSystem.File.WriteAllText("/output/keep.txt", "unchanged");
		}
		byte[] before = fileSystem.File.ReadAllBytes("/game/test.assets");
		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output", "--dry-run"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("dry_run");
		await Assert.That(data.RootElement.GetProperty("output").GetString()).IsEqualTo("/output");
		await Assert.That(data.RootElement.TryGetProperty("project", out _)).IsFalse();
		string files = string.Join('\n', data.RootElement.GetProperty("files").EnumerateArray().Select(element => element.GetString()));
		await Assert.That(files).Contains(FixturePath);
		await Assert.That(result.Stderr).Contains("Finished reading files");
		await Assert.That(fileSystem.File.Exists(FixtureOutput)).IsFalse();
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsFalse();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsEqualTo(nonEmpty);
		await Assert.That(fileSystem.File.ReadAllBytes("/game/test.assets").SequenceEqual(before)).IsTrue();
		if (nonEmpty)
		{
			await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("unchanged");
		}
	}

	[Test]
	public async Task NonEmptyOutputIsRefusedWithoutConfirm()
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.Directory.Create("/output");
		fileSystem.File.WriteAllText("/output/keep.txt", "do not change");
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("output_not_empty");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("Choose another directory");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("empty this one");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--confirm=<directory name>");
		await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("do not change");
		await Assert.That(fileSystem.File.Exists(FixtureOutput)).IsFalse();
	}

	[Test]
	[Arguments("--confirm=output")]
	[Arguments("--confirm", "output")]
	public async Task MatchingConfirmAllowsWriteIntoNonEmptyOutput(params string[] confirm)
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.Directory.Create("/output");
		fileSystem.File.WriteAllText("/output/keep.txt", "do not change");
		List<string> args = ["export", "primary-content", "--input", "/game", "--output", "/output", .. confirm];
		CliResult result = Cli.Run(args.ToArray(), fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("do not change");
		await Assert.That(fileSystem.File.ReadAllText(FixtureOutput)).IsEqualTo(FixtureText);
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsTrue();
	}

	[Test]
	public async Task SuccessfulExportWritesAPrimaryContentManifest()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsTrue();
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("kind").GetString()).IsEqualTo("primary-content");
		List<string> inputs = [.. manifest.RootElement.GetProperty("inputs").EnumerateArray().Select(element => element.GetString()!)];
		await Assert.That(inputs).Contains("/game");
	}

	[Test]
	public async Task RepeatedIdenticalExportReportsExistingAndDoesNotRewrite()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		const string sentinelPath = "/output/TAMPER.txt";
		fileSystem.File.WriteAllText(FixtureOutput, "changed");
		fileSystem.File.WriteAllText(sentinelPath, "leave me");
		string manifest = fileSystem.File.ReadAllText("/output/.assetripper-export.json");

		CliResult second = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(second.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(data.RootElement.GetProperty("existing").GetBoolean()).IsTrue();
		await Assert.That(fileSystem.File.ReadAllText(FixtureOutput)).IsEqualTo("changed");
		await Assert.That(fileSystem.File.ReadAllText(sentinelPath)).IsEqualTo("leave me");
		await Assert.That(fileSystem.File.ReadAllText("/output/.assetripper-export.json")).IsEqualTo(manifest);
	}

	[Test]
	public async Task UnityProjectExportIsNotAnExistingPrimaryContentExport()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult unity = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(unity.ExitCode).IsEqualTo(0);
		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("output_not_empty");
		await Assert.That(fileSystem.File.Exists(FixtureOutput)).IsFalse();
	}

	[Test]
	public async Task SubcommandHelpDocumentsPrimaryContentExport()
	{
		CliResult result = Cli.Run(["export", "primary-content", "--help"]);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("Usage: assetripper export unity-project|primary-content");
		await Assert.That(result.Stdout).Contains("primary decoded files");
		await Assert.That(result.Stdout).Contains("assetripper export primary-content --input ./Game --output ./Content --dry-run");
		await Assert.That(result.Stdout).Contains("existing: true");
		await Assert.That(result.Stdout).Contains("--confirm=<directory name>");
		await Assert.That(result.Stdout).Contains("EXIT CODES");
		await Assert.That(result.Stdout).DoesNotContain("Primary Content Export, Sessions");
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
			WriteAligned(writer, FixtureText);
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
