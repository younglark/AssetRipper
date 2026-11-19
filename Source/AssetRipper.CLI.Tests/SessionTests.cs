using AssetRipper.IO.Endian;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using System.Text;
using System.Text.Json;

namespace AssetRipper.CLI.Tests;

internal class SessionTests
{
	[Test]
	public async Task SessionGetKeepsJsonAndOutputFileContract()
	{
		string input = CreateGame();
		string output = Path.Combine(input, "result.txt");
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input]);
			id = ReadId(start);
			CliResult listed = Cli.Run(["list", "assets", "--session", id, "--name", "Greeting"]);
			using JsonDocument items = JsonDocument.Parse(listed.Stdout);
			string reference = items.RootElement.GetProperty("items")[0].GetProperty("ref").GetString()!;
			CliResult get = Cli.Run(["--json", "get", "asset", reference, "--session", id]);
			await Assert.That(get.ExitCode).IsEqualTo(0);
			using JsonDocument payload = JsonDocument.Parse(get.Stdout);
			await Assert.That(get.Stdout).Contains("Hello from CLI fixture!");
			CliResult written = Cli.Run(["get", "asset", reference, "--session", id, "--format", "text", "--output=" + output]);
			await Assert.That(written.ExitCode).IsEqualTo(0);
			using JsonDocument metadata = JsonDocument.Parse(written.Stdout);
			await Assert.That(metadata.RootElement.GetProperty("path").GetString()).IsEqualTo(output);
			await Assert.That(metadata.RootElement.GetProperty("format").GetString()).IsEqualTo("text");
			await Assert.That(metadata.RootElement.GetProperty("size").GetInt32()).IsEqualTo(23);
			await Assert.That(File.ReadAllText(output)).IsEqualTo("Hello from CLI fixture!");
			CliResult refused = Cli.Run(["get", "asset", reference, "--session", id, "--format", "text", "--output", output]);
			await Assert.That(refused.ExitCode).IsEqualTo(1);
			await Assert.That(refused.Stdout).IsEmpty();
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(input, true);
		}
	}

	[Test]
	public async Task ClosingLoadingSessionDoesNotWaitForTheLoad()
	{
		string input = CreateGame();
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input, "--no-wait"], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_SESSION_LOAD_DELAY_MS"] = "60000" });
			await Assert.That(start.ExitCode).IsEqualTo(0);
			id = ReadId(start);
			await Assert.That(ReadStatus(start)).IsEqualTo("loading");
			await Assert.That(Cli.Run(["session", "close", id]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["session", "status", id]).ExitCode).IsEqualTo(5);
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(input, true);
		}
	}

	[Test]
	[Arguments("session")]
	[Arguments("session start")]
	[Arguments("session start --input")]
	[Arguments("session start --input=")]
	[Arguments("session start --input Game --timeout NaN")]
	[Arguments("session start --input Game --timeout=-1")]
	[Arguments("session start --input Game --timeout infinity")]
	[Arguments("session start --input Game --timeout 1 --timeout 2")]
	[Arguments("session start --input Game --no-wait --wait")]
	[Arguments("session start --input Game extra")]
	[Arguments("session start --input Game --idle-timeout 1")]
	[Arguments("session status")]
	[Arguments("session status id --input Game")]
	[Arguments("session status id --no-wait")]
	[Arguments("session close")]
	[Arguments("session close id extra")]
	[Arguments("session close id --timeout 1")]
	public async Task InvalidSessionArgumentsAreUsageErrors(string commandLine)
	{
		CliResult result = Cli.Run(commandLine.Split(' '));
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--help");
	}

	[Test]
	public async Task MultipleSessionsRemainIndependentAndAcceptRepeatedInputs()
	{
		string firstInput = CreateGame();
		string secondInput = CreateGame();
		File.Move(Path.Combine(secondInput, "test.assets"), Path.Combine(secondInput, "second.assets"));
		List<string> ids = [];
		try
		{
			CliResult first = Cli.Run(["session", "start", "--input", firstInput, "--input=" + secondInput]);
			await Assert.That(first.ExitCode).IsEqualTo(0);
			ids.Add(ReadId(first));
			CliResult second = Cli.Run(["session", "start", "--input", secondInput]);
			await Assert.That(second.ExitCode).IsEqualTo(0);
			ids.Add(ReadId(second));
			await Assert.That(ids[0]).IsNotEqualTo(ids[1]);
			CliResult list = Cli.Run(["list", "assets", "--session", ids[0], "--name", "Greeting"]);
			using JsonDocument items = JsonDocument.Parse(list.Stdout);
			await Assert.That(items.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(2);
			await Assert.That(Cli.Run(["session", "close", ids[0]]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["session", "status", ids[0]]).ExitCode).IsEqualTo(5);
			CliResult text = Cli.Run(["session", "status", ids[1]], stdoutIsTty: true);
			await Assert.That(text.ExitCode).IsEqualTo(0);
			await Assert.That(text.Stdout.Trim()).IsEqualTo($"Session {ids[1]}: ready");
			CliResult json = Cli.Run(["session", "status", ids[1], "--json"], stdoutIsTty: true);
			await Assert.That(ReadStatus(json)).IsEqualTo("ready");
		}
		finally
		{
			foreach (string id in ids)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(firstInput, true);
			Directory.Delete(secondInput, true);
		}
	}

	[Test]
	[Arguments("start")]
	[Arguments("status")]
	[Arguments("close")]
	public async Task SessionHelpExplainsLoadStatesWaitingAndLifetime(string action)
	{
		CliResult result = Cli.Run(["session", action, "--help"]);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("loading").And.Contains("ready").And.Contains("failed");
		await Assert.That(result.Stdout).Contains("default").And.Contains("30 minutes").And.Contains("EXAMPLES").And.Contains("EXIT CODES");
		await Assert.That(result.Stdout).DoesNotContain("ASSETRIPPER_TEST");
	}

	[Test]
	public async Task IdleSessionDisappearsButActivityExtendsItsLifetime()
	{
		string input = CreateGame();
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_SESSION_IDLE_MS"] = "1500" });
			await Assert.That(start.ExitCode).IsEqualTo(0);
			id = ReadId(start);
			for (int i = 0; i < 6; i++)
			{
				await Task.Delay(300);
				await Assert.That(Cli.Run(["session", "status", id]).ExitCode).IsEqualTo(0);
			}
			await Task.Delay(2200);
			foreach (string[] command in new string[][] { ["session", "status", id], ["session", "close", id], ["list", "assets", "--session", id], ["search", "Greeting", "--session", id], ["get", "asset", "test.assets:1", "--session", id] })
			{
				CliResult missing = Cli.Run(command);
				await Assert.That(missing.ExitCode).IsEqualTo(5);
				await Assert.That(missing.Stdout).IsEmpty();
				using JsonDocument error = JsonDocument.Parse(missing.Stderr);
				await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
			}
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(input, true);
		}
	}

	[Test]
	public async Task FailedStartRetainsIdAndInspectsExplainHowToStartAgain()
	{
		string input = Path.Combine(Path.GetTempPath(), "missing-session-input-" + Guid.NewGuid().ToString("N"));
		CliResult start = Cli.Run(["session", "start", "--input", input]);
		string id = ReadId(start);
		try
		{
			await Assert.That(start.ExitCode).IsEqualTo(1);
			await Assert.That(ReadStatus(start)).IsEqualTo("failed");
			await Assert.That(ReadStatus(Cli.Run(["session", "status", id]))).IsEqualTo("failed");
			foreach (string[] command in new string[][] { ["list", "assets"], ["search", "Greeting"], ["get", "asset", "test.assets:1"] })
			{
				CliResult result = Cli.Run([.. command, "--session", id]);
				await Assert.That(result.ExitCode).IsEqualTo(1);
				await Assert.That(result.Stdout).IsEmpty();
				using JsonDocument error = JsonDocument.Parse(result.Stderr);
				await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("session start");
			}
		}
		finally { Cli.Run(["session", "close", id]); }
	}

	[Test]
	[Arguments("--no-wait")]
	[Arguments("--timeout=0")]
	public async Task BoundedStartPreservesIdAndLoadingInspectsAreRetryable(string option)
	{
		string input = CreateGame();
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input, option], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_SESSION_LOAD_DELAY_MS"] = "1500" });
			await Assert.That(start.ExitCode).IsEqualTo(0);
			id = ReadId(start);
			await Assert.That(ReadStatus(start)).IsEqualTo("loading");
			foreach (string[] command in new string[][] { ["list", "assets"], ["search", "Greeting"], ["get", "asset", "test.assets:1"] })
			{
				CliResult result = Cli.Run([.. command, "--session", id]);
				await Assert.That(result.ExitCode).IsEqualTo(8);
				await Assert.That(result.Stdout).IsEmpty();
				using JsonDocument error = JsonDocument.Parse(result.Stderr);
				await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("session status");
			}
			await WaitForState(id, "ready");
			await Assert.That(Cli.Run(["list", "assets", "--session", id]).ExitCode).IsEqualTo(0);
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(input, true);
		}
	}

	private static string ReadStatus(CliResult result)
	{
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		return data.RootElement.GetProperty("status").GetString()!;
	}

	private static async Task WaitForState(string id, string status)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(15);
		while (DateTime.UtcNow < deadline)
		{
			CliResult result = Cli.Run(["session", "status", id]);
			if (result.ExitCode != 0)
			{
				throw new InvalidOperationException(result.Stderr);
			}

			if (ReadStatus(result) == status)
			{
				return;
			}

			await Task.Delay(25);
		}
		throw new TimeoutException($"Session never became {status}.");
	}

	[Test]
	public async Task ReadySessionReusesLoadedAssetsForListSearchAndGetAfterInputsDisappear()
	{
		string input = CreateGame();
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input]);
			await Assert.That(start.ExitCode).IsEqualTo(0);
			id = ReadId(start);
			Directory.Delete(input, true);
			CliResult list = Cli.Run(["list", "assets", "--session", id, "--name", "Greeting"]);
			await Assert.That(list.ExitCode).IsEqualTo(0);
			using JsonDocument items = JsonDocument.Parse(list.Stdout);
			JsonElement asset = items.RootElement.GetProperty("items")[0];
			await Assert.That(asset.GetProperty("name").GetString()).IsEqualTo("Greeting");
			string reference = asset.GetProperty("ref").GetString()!;
			CliResult search = Cli.Run(["search", "greet", "--session", id]);
			await Assert.That(search.ExitCode).IsEqualTo(0);
			await Assert.That(search.Stdout).Contains("Greeting");
			CliResult get = Cli.Run(["get", "asset", reference, "--session", id, "--format", "text"]);
			await Assert.That(get.ExitCode).IsEqualTo(0);
			await Assert.That(get.Stdout.Trim()).IsEqualTo("Hello from CLI fixture!");
			foreach (string kind in new[] { "bundles", "collections", "resources", "scenes", "failed-files" })
			{
				await Assert.That(Cli.Run(["list", kind, "--session", id]).ExitCode).IsEqualTo(0);
			}
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

	private static string ReadId(CliResult result)
	{
		using JsonDocument data = JsonDocument.Parse(result.Stdout);
		return data.RootElement.GetProperty("id").GetString()!;
	}

	internal static string CreateGame()
	{
		string directory = Path.Combine(Path.GetTempPath(), "assetripper-session-test-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
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
			foreach (string value in new[] { "Greeting", "Hello from CLI fixture!" })
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
		builder.Objects.Add(new ObjectInfo(type) { FileID = 1, SerializedTypeIndex = 0, ObjectData = objectData.ToArray() });
		using Stream stream = File.Create(Path.Combine(directory, "test.assets"));
		builder.Build().Write(stream);
		return directory;
	}

	[Test]
	[Arguments("list", "assets")]
	[Arguments("search", "Greeting")]
	[Arguments("get", "asset")]
	[Arguments("export", "unity-project")]
	[Arguments("export", "primary-content")]
	public async Task LoadedCommandsRequireExactlyOneInputOrSession(string command, string subcommand)
	{
		string[] prefix = command == "get" ? [command, subcommand, "test.assets:1"]
			: command == "export" ? [command, subcommand, "--output", "Out"] : [command, subcommand];
		foreach (string[] source in new string[][] { [], ["--input", "/game", "--session", "anything"] })
		{
			CliResult result = Cli.Run([.. prefix, .. source]);
			await Assert.That(result.ExitCode).IsEqualTo(2);
			await Assert.That(result.Stdout).IsEmpty();
			using JsonDocument error = JsonDocument.Parse(result.Stderr);
			await Assert.That(error.RootElement.GetProperty("hint").GetString()).Contains("--input").And.Contains("--session");
		}
	}

	[Test]
	public async Task StartWaitsForReadyAndCloseMakesSessionNotFound()
	{
		string input = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(input);
		string? id = null;
		try
		{
			CliResult start = Cli.Run(["session", "start", "--input", input]);
			await Assert.That(start.ExitCode).IsEqualTo(0);
			using JsonDocument data = JsonDocument.Parse(start.Stdout);
			id = data.RootElement.GetProperty("id").GetString()!;
			await Assert.That(data.RootElement.GetProperty("status").GetString()).IsEqualTo("ready");
			CliResult status = Cli.Run(["session", "status", id]);
			await Assert.That(status.ExitCode).IsEqualTo(0);
			await Assert.That(status.Stdout).Contains("ready");
			await Assert.That(Cli.Run(["session", "close", id]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["session", "status", id]).ExitCode).IsEqualTo(5);
		}
		finally
		{
			if (id is not null)
			{
				Cli.Run(["session", "close", id]);
			}

			Directory.Delete(input, true);
		}
	}
}
