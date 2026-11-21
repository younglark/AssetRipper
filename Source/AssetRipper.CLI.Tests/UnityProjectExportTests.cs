using System.Text;
using System.Text.Json;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;

namespace AssetRipper.CLI.Tests;

internal class UnityProjectExportTests
{
	[Test]
	public async Task OutputIsRequired()
	{
		CliResult result = Cli.Run(["export", "unity-project", "--input", "Game"]);
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
		CliResult result = Cli.Run(["export", "unity-project", "--input", "missing-game", "--output", Path.Combine(Path.GetTempPath(), name)]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	public async Task MissingInputIsNotFoundAndDoesNotCreateOutput()
	{
		VirtualFileSystem fileSystem = new();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/missing-game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
	}

	[Test]
	public async Task LoadsARealSerializedFileAndExportsAUnityProject()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game/test.assets", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/ProjectSettings/ProjectVersion.txt")).Contains("2022.3.0f1");
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Greeting.txt")).IsEqualTo("Hello from CLI fixture!");
		await Assert.That(result.Stderr).Contains("Finished reading files");
		await Assert.That(result.Stderr).Contains("Finished post-export");
	}

	[Test]
	public async Task NonEmptyOutputIsRefusedWithoutConfirm()
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.Directory.Create("/output");
		fileSystem.File.WriteAllText("/output/keep.txt", "do not change");
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("output_not_empty");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("Choose another directory");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("empty this one");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--confirm=<directory name>");
		await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("do not change");
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
	}

	[Test]
	[Arguments("--confirm=output")]
	[Arguments("--confirm", "output")]
	public async Task MatchingConfirmAllowsWriteIntoNonEmptyOutput(params string[] confirm)
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.Directory.Create("/output");
		fileSystem.File.WriteAllText("/output/keep.txt", "do not change");
		List<string> args = ["export", "unity-project", "--input", "/game", "--output", "/output", .. confirm];
		CliResult result = Cli.Run(args.ToArray(), fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("do not change");
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Greeting.txt")).IsEqualTo("Hello from CLI fixture!");
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsTrue();
	}

	[Test]
	[Arguments("--confirm=yes")]
	[Arguments("--confirm", "other")]
	public async Task MismatchedConfirmDoesNotAuthorizeNonEmptyOutput(params string[] confirm)
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.Directory.Create("/output");
		fileSystem.File.WriteAllText("/output/keep.txt", "do not change");
		List<string> args = ["export", "unity-project", "--input", "/game", "--output", "/output", .. confirm];
		CliResult result = Cli.Run(args.ToArray(), fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--confirm=<directory name>");
		await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("do not change");
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
	}

	[Test]
	public async Task SuccessfulUnityProjectExportWritesAManifest()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsTrue();
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("kind").GetString()).IsEqualTo("unity-project");
		List<string> inputs = [.. manifest.RootElement.GetProperty("inputs").EnumerateArray().Select(element => element.GetString()!)];
		await Assert.That(inputs).Contains("/game");
	}

	[Test]
	public async Task RepeatedIdenticalExportReportsExistingAndDoesNotRewrite()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		const string greetingPath = "/output/ExportedProject/Assets/TextAsset/Greeting.txt";
		const string sentinelPath = "/output/ExportedProject/TAMPER.txt";
		fileSystem.File.WriteAllText(greetingPath, "changed");
		fileSystem.File.WriteAllText(sentinelPath, "leave me");
		string manifest = fileSystem.File.ReadAllText("/output/.assetripper-export.json");

		CliResult second = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(second.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		await Assert.That(data.RootElement.GetProperty("existing").GetBoolean()).IsTrue();
		await Assert.That(fileSystem.File.ReadAllText(greetingPath)).IsEqualTo("changed");
		await Assert.That(fileSystem.File.ReadAllText(sentinelPath)).IsEqualTo("leave me");
		await Assert.That(fileSystem.File.ReadAllText("/output/.assetripper-export.json")).IsEqualTo(manifest);
		await Assert.That(second.Stderr).DoesNotContain("Finished post-export");
	}

	[Test]
	public async Task NonMatchingExportIsStillRefusedWithoutConfirm()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		string greeting = fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Greeting.txt");
		string manifest = fileSystem.File.ReadAllText("/output/.assetripper-export.json");
		CreateGame(fileSystem, "/other", "Other", "other.assets");
		Rewind(fileSystem, "/other/other.assets");

		CliResult result = Cli.Run(["export", "unity-project", "--input", "/other", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--confirm=<directory name>");
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Greeting.txt")).IsEqualTo(greeting);
		await Assert.That(fileSystem.File.ReadAllText("/output/.assetripper-export.json")).IsEqualTo(manifest);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DryRunReportsTheDestinationWithoutChangingDisk(bool nonEmpty)
	{
		VirtualFileSystem fileSystem = CreateGame();
		if (nonEmpty)
		{
			fileSystem.Directory.Create("/output");
			fileSystem.File.WriteAllText("/output/keep.txt", "unchanged");
		}
		byte[] before = fileSystem.File.ReadAllBytes("/game/test.assets");
		Rewind(fileSystem, "/game/test.assets");
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--dry-run"], fileSystem: fileSystem);
		const string fixturePath = "ExportedProject/Assets/TextAsset/Greeting.txt";
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("dry_run");
		await Assert.That(data.RootElement.GetProperty("output").GetString()).IsEqualTo("/output");
		await Assert.That(data.RootElement.GetProperty("project").GetString()).IsEqualTo("/output/ExportedProject");
		string files = string.Join('\n', data.RootElement.GetProperty("files").EnumerateArray().Select(element => element.GetString()));
		await Assert.That(files).Contains(fixturePath);
		await Assert.That(result.Stderr).Contains("Finished reading files");
		await Assert.That(result.Stderr).Contains("Finished post-export");
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
		await Assert.That(fileSystem.File.Exists("/output/.assetripper-export.json")).IsFalse();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsEqualTo(nonEmpty);
		await Assert.That(fileSystem.File.ReadAllBytes("/game/test.assets").SequenceEqual(before)).IsTrue();
		if (nonEmpty)
		{
			await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("unchanged");
		}

		Rewind(fileSystem, "/game/test.assets");
		CliResult text = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--dry-run"], stdoutIsTty: true, fileSystem: fileSystem);
		await Assert.That(text.ExitCode).IsEqualTo(0);
		await Assert.That(text.Stdout.TrimStart().StartsWith('{')).IsFalse();
		await Assert.That(text.Stdout).Contains(fixturePath);
		await Assert.That(fileSystem.Directory.Exists("/output/ExportedProject")).IsFalse();
		if (nonEmpty)
		{
			await Assert.That(fileSystem.File.ReadAllText("/output/keep.txt")).IsEqualTo("unchanged");
		}
	}

	[Test]
	[Arguments("--output /output")]
	[Arguments("--input --output /output")]
	[Arguments("--input= --output /output")]
	[Arguments("--input /game --output")]
	[Arguments("--input /game --output=")]
	[Arguments("--input /game --output /output --output /other")]
	[Arguments("unexpected --input /game --output /output")]
	[Arguments("--input /game --session id --output /output")]
	public async Task InvalidExportArgumentsFailBeforeAnyWrites(string options)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", .. options.Split(' ')], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	[Arguments("--timeout=NaN")]
	[Arguments("--no-wait --wait")]
	public async Task InvalidDeferredExecutionOptionsDoNotExport(string option)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", .. option.Split(' ')], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnreadableInputIsNotFoundBeforeExport(bool directory)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}
		string temp = Directory.CreateTempSubdirectory("assetripper-unreadable-").FullName;
		string input = Path.Combine(temp, "game.assets");
		File.WriteAllText(input, "unreadable fixture");
		string blocked = directory ? temp : input;
		UnixFileMode originalMode = File.GetUnixFileMode(blocked);
		try
		{
			File.SetUnixFileMode(blocked, UnixFileMode.None);
			CliResult result = Cli.Run(["export", "unity-project", "--input", blocked, "--output", Path.Combine(temp, "output")]);
			await Assert.That(result.ExitCode).IsEqualTo(5);
			await Assert.That(result.Stdout).IsEmpty();
			using JsonDocument error = JsonDocument.Parse(result.Stderr);
			await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
		}
		finally
		{
			File.SetUnixFileMode(blocked, originalMode);
			Directory.Delete(temp, recursive: true);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InvalidGameDataIsARuntimeFailureRatherThanAnEmptySuccessfulExport(bool dryRun)
	{
		VirtualFileSystem fileSystem = new();
		fileSystem.File.WriteAllText("/bad.assets", "not a Unity file");
		List<string> args = ["export", "unity-project", "--input", "/bad.assets", "--output", "/output"];
		if (dryRun)
		{
			args.Add("--dry-run");
		}
		CliResult result = Cli.Run(args.ToArray(), fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
		string errorLine = result.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
		using JsonDocument error = JsonDocument.Parse(errorLine);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("export_failed");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).IsNotEmpty();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RepeatedFileAndDirectoryInputsExportTogetherToAnEmptyDirectory(bool json)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CreateGame(fileSystem, "/other", "Second", "second.assets");
		fileSystem.Directory.Create("/output");
		List<string> args = ["export", "unity-project", "--input", "/game/test.assets", "--input=/other", "--output=/output"];
		if (json)
		{
			args.Add("--json");
		}
		CliResult result = Cli.Run(args.ToArray(), stdoutIsTty: true, fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Greeting.txt")).IsEqualTo("Hello from CLI fixture!");
		await Assert.That(fileSystem.File.ReadAllText("/output/ExportedProject/Assets/TextAsset/Second.txt")).IsEqualTo("Hello from CLI fixture!");
		if (json)
		{
			using JsonDocument data = JsonDocument.Parse(result.Stdout);
			await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
		}
		else
		{
			await Assert.That(result.Stdout).Contains("Unity Project Export completed");
			await Assert.That(result.Stdout.TrimStart().StartsWith('{')).IsFalse();
		}
	}

	[Test]
	[NotInParallel]
	public async Task ConfirmUsesTheResolvedDirectoryNameForDotAndDotDotOutputs()
	{
		string parent = Directory.CreateTempSubdirectory("assetripper-confirm-").FullName;
		string cwdName = $"cwd-{Guid.NewGuid():N}";
		string cwd = Path.Combine(parent, cwdName);
		Directory.CreateDirectory(cwd);
		File.WriteAllText(Path.Combine(cwd, "keep.txt"), "do not change");
		VirtualFileSystem fixture = CreateGame();
		string input = Path.Combine(parent, "test.assets");
		File.WriteAllBytes(input, fixture.File.ReadAllBytes("/game/test.assets"));
		string originalCwd = Environment.CurrentDirectory;
		try
		{
			Directory.SetCurrentDirectory(cwd);

			CliResult confirmDot = Cli.Run(["export", "unity-project", "--input", input, "--output", ".", "--confirm", "."]);
			await Assert.That(confirmDot.ExitCode).IsEqualTo(1);
			await Assert.That(confirmDot.Stdout).IsEmpty();
			await Assert.That(Directory.Exists(Path.Combine(cwd, "ExportedProject"))).IsFalse();
			await Assert.That(File.ReadAllText(Path.Combine(cwd, "keep.txt"))).IsEqualTo("do not change");

			CliResult confirmCwd = Cli.Run(["export", "unity-project", "--input", input, "--output", ".", "--confirm", cwdName]);
			await Assert.That(confirmCwd.ExitCode).IsEqualTo(0);
			await Assert.That(File.ReadAllText(Path.Combine(cwd, "ExportedProject", "Assets", "TextAsset", "Greeting.txt"))).IsEqualTo("Hello from CLI fixture!");
			await Assert.That(File.ReadAllText(Path.Combine(cwd, "keep.txt"))).IsEqualTo("do not change");

			CliResult confirmDotDot = Cli.Run(["export", "unity-project", "--input", input, "--output", "..", "--confirm", ".."]);
			await Assert.That(confirmDotDot.ExitCode).IsEqualTo(1);
			await Assert.That(confirmDotDot.Stdout).IsEmpty();
			await Assert.That(Directory.Exists(Path.Combine(parent, "ExportedProject"))).IsFalse();

			string parentName = Path.GetFileName(parent);
			CliResult confirmParent = Cli.Run(["export", "unity-project", "--input", input, "--output", "..", "--confirm", parentName]);
			await Assert.That(confirmParent.ExitCode).IsEqualTo(0);
			await Assert.That(File.ReadAllText(Path.Combine(parent, "ExportedProject", "Assets", "TextAsset", "Greeting.txt"))).IsEqualTo("Hello from CLI fixture!");
		}
		finally
		{
			Directory.SetCurrentDirectory(originalCwd);
			Directory.Delete(parent, recursive: true);
		}
	}

	[Test]
	public async Task RealFilesystemExportWritesOnlyInsideTheRequestedDestination()
	{
		string temp = Directory.CreateTempSubdirectory("assetripper-export-").FullName;
		try
		{
			VirtualFileSystem fixture = CreateGame();
			string input = Path.Combine(temp, "test.assets");
			File.WriteAllBytes(input, fixture.File.ReadAllBytes("/game/test.assets"));
			string output = Path.Combine(temp, "output");
			CliResult result = Cli.Run(["export", "unity-project", "--input", input, "--output", output]);
			await Assert.That(result.ExitCode).IsEqualTo(0);
			using JsonDocument data = JsonDocument.Parse(result.Stdout);
			await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("completed");
			await Assert.That(File.ReadAllText(Path.Combine(output, "ExportedProject", "Assets", "TextAsset", "Greeting.txt"))).IsEqualTo("Hello from CLI fixture!");
			await Assert.That(Directory.GetFiles(temp).Select(Path.GetFileName).SequenceEqual(new[] { "test.assets" })).IsTrue();
		}
		finally
		{
			Directory.Delete(temp, recursive: true);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExistingOutputFileIsNeverTreatedAsADirectory(bool dryRun)
	{
		VirtualFileSystem fileSystem = CreateGame();
		fileSystem.File.WriteAllText("/output", "preserve");
		List<string> args = ["export", "unity-project", "--input", "/game", "--output", "/output"];
		if (dryRun)
		{
			args.Add("--dry-run");
		}
		CliResult result = Cli.Run(args.ToArray(), fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(fileSystem.File.ReadAllText("/output")).IsEqualTo("preserve");
	}


	[Test]
	public async Task SuccessfulExportManifestIncludesTheSpriteModeFingerprint()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("settingsFingerprint").GetString()).IsEqualTo("sprite-export-mode=yaml");
	}

	[Test]
	[Arguments("yaml")]
	[Arguments("yaml-unpacked")]
	[Arguments("native")]
	[Arguments("texture2d")]
	public async Task SpriteExportModeFlagIsAccepted(string mode)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", mode], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("settingsFingerprint").GetString()).IsEqualTo($"sprite-export-mode={mode}");
	}

	[Test]
	public async Task InvalidSpriteExportModeIsAUsageError()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult result = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", "not-a-mode"], fileSystem: fileSystem);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--sprite-export-mode");
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
	}

	[Test]
	public async Task ChangingSpriteExportModeIsNotAnExistingExport()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		const string greetingPath = "/output/ExportedProject/Assets/TextAsset/Greeting.txt";
		fileSystem.File.WriteAllText(greetingPath, "changed");

		CliResult second = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", "yaml-unpacked"], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(1);
		await Assert.That(second.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(second.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("output_not_empty");
		await Assert.That(fileSystem.File.ReadAllText(greetingPath)).IsEqualTo("changed");
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("settingsFingerprint").GetString()).IsEqualTo("sprite-export-mode=yaml");
	}

	[Test]
	public async Task ConfirmedSpriteModeChangeWritesANewFingerprint()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		Rewind(fileSystem, "/game/test.assets");

		CliResult second = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", "yaml-unpacked", "--confirm", "output"], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(0);
		using JsonDocument data = JsonDocument.Parse(second.Stdout);
		await Assert.That(data.RootElement.TryGetProperty("existing", out _)).IsFalse();
		using JsonDocument manifest = JsonDocument.Parse(fileSystem.File.ReadAllText("/output/.assetripper-export.json"));
		await Assert.That(manifest.RootElement.GetProperty("settingsFingerprint").GetString()).IsEqualTo("sprite-export-mode=yaml-unpacked");
	}

	[Test]
	[Arguments("yaml")]
	[Arguments("native")]
	public async Task LegacyManifestWithoutFingerprintCannotProveEffectiveSettings(string mode)
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult first = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output"], fileSystem: fileSystem);
		await Assert.That(first.ExitCode).IsEqualTo(0);
		fileSystem.File.WriteAllText("/output/.assetripper-export.json", """{"kind":"unity-project","inputs":["/game"]}""");
		const string greetingPath = "/output/ExportedProject/Assets/TextAsset/Greeting.txt";
		fileSystem.File.WriteAllText(greetingPath, "changed");
		CliResult second = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", mode], fileSystem: fileSystem);
		await Assert.That(second.ExitCode).IsEqualTo(1);
		using JsonDocument error = JsonDocument.Parse(second.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("output_not_empty");
		await Assert.That(fileSystem.File.ReadAllText(greetingPath)).IsEqualTo("changed");
	}

	[Test]
	public async Task YamlUnpackedDryRunMatchesTheRealExportFileList()
	{
		VirtualFileSystem fileSystem = CreateGame();
		CliResult preview = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", "yaml-unpacked", "--dry-run"], fileSystem: fileSystem);
		await Assert.That(preview.ExitCode).IsEqualTo(0);
		using JsonDocument previewData = JsonDocument.Parse(preview.Stdout);
		await Assert.That(previewData.RootElement.GetProperty("status").GetString()).IsEqualTo("dry_run");
		List<string> previewFiles = [.. previewData.RootElement.GetProperty("files").EnumerateArray().Select(element => element.GetString()!)];
		await Assert.That(fileSystem.Directory.Exists("/output")).IsFalse();
		Rewind(fileSystem, "/game/test.assets");

		CliResult exported = Cli.Run(["export", "unity-project", "--input", "/game", "--output", "/output", "--sprite-export-mode", "yaml-unpacked"], fileSystem: fileSystem);
		await Assert.That(exported.ExitCode).IsEqualTo(0);
		List<string> exportedFiles = [];
		foreach (string path in fileSystem.Directory.EnumerateFiles("/output", "*", System.IO.SearchOption.AllDirectories))
		{
			string relative = fileSystem.Path.GetRelativePath("/output", path).Replace('\\', '/');
			if (relative != ".assetripper-export.json")
			{
				exportedFiles.Add(relative);
			}
		}

		exportedFiles.Sort(StringComparer.Ordinal);
		previewFiles.Sort(StringComparer.Ordinal);
		await Assert.That(previewFiles.SequenceEqual(exportedFiles, StringComparer.Ordinal)).IsTrue();
	}

	[Test]
	public async Task ExportHelpDocumentsSpriteExportMode()
	{
		CliResult result = Cli.Run(["export", "--help"]);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("--sprite-export-mode");
		await Assert.That(result.Stdout).Contains("yaml-unpacked");
		await Assert.That(result.Stdout).Contains("yaml-baked");
	}

	[Test]
	public async Task PrimaryContentRejectsSpriteExportMode()
	{
		VirtualFileSystem fs = CreateGame();
		CliResult result = Cli.Run(["export", "primary-content", "--input", "/game", "--output", "/output", "--sprite-export-mode", "yaml-unpacked"], fileSystem: fs);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stderr).Contains("only supported for unity-project");
		await Assert.That(fs.Directory.Exists("/output")).IsFalse();
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
