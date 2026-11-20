using System.Text.Json;
using System.Diagnostics;

namespace AssetRipper.CLI.Tests;

internal class JobTests
{
	[Test]
	[Arguments("0")]
	[Arguments("0.1")]
	public async Task UnknownOwnerRemainsNotFoundWithABoundedWait(string seconds)
	{
		string job = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
		CliResult result = Cli.Run(["job", "wait", job, "--timeout", seconds]);
		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
	}

	[Test]
	public async Task WaitTimeoutBoundsAStalledOwnerReplyAndTheOwnerSurvivesDisconnect()
	{
		string input = SessionTests.CreateGame();
		string output = input + "-stalled-reply";
		string session = Property(Cli.Run(["session", "start", "--input", input], environment: new Dictionary<string, string?>
		{
			["ASSETRIPPER_TEST_EXPORT_DELAY_MS"] = "60000",
			["ASSETRIPPER_TEST_JOB_REPLY_DELAY_MS"] = "2000",
		}), "id");
		string? job = null;
		Task<CliResult>? waiting = null;
		try
		{
			job = Property(Cli.Run(["export", "primary-content", "--session", session, "--output", output, "--no-wait"]), "id");
			waiting = Task.Run(() => Cli.Run(["job", "wait", job, "--timeout", "0.1"]));
			CliResult result = await waiting.WaitAsync(TimeSpan.FromSeconds(1));
			await Assert.That(result.ExitCode).IsEqualTo(0);
			await Assert.That(Property(result, "status")).IsEqualTo("running");
			await Assert.That(Property(result, "id")).IsEqualTo(job);
			await Assert.That(Property(result, "hint")).Contains("job wait " + job);
			// Let the late reply encounter the disconnected caller, then verify the same owner still serves commands.
			await Task.Delay(2200);
			await Assert.That(Property(Cli.Run(["job", "status", job]), "status")).IsEqualTo("running");
			await Assert.That(Cli.Run(["session", "status", session]).ExitCode).IsEqualTo(0);
		}
		finally
		{
			try
			{
				if (waiting is not null) await waiting.WaitAsync(TimeSpan.FromSeconds(10));
			}
			finally
			{
				if (job is not null) Cli.Run(["job", "cancel", job]);
				Cli.Run(["session", "close", session]);
				Directory.Delete(input, true);
				if (Directory.Exists(output)) Directory.Delete(output, true);
			}
		}
	}

	[Test]
	[Arguments("unity-project")]
	[Arguments("primary-content")]
	public async Task ReadySessionExportsAndRecognizesExisting(string kind)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-export";
		string id = Property(Cli.Run(["session", "start", "--input", input]), "id");
		try
		{
			CliResult exported = Cli.Run(["export", kind, "--session", id, "--output", output]);
			await Assert.That(exported.ExitCode).IsEqualTo(0);
			await Assert.That(Property(exported, "status")).IsEqualTo("completed");
			await Assert.That(Directory.GetFiles(output, "Greeting.*", SearchOption.AllDirectories).Length).IsGreaterThan(0);
			CliResult duplicate = Cli.Run(["export", kind, "--session", id, "--output", output]);
			await Assert.That(duplicate.ExitCode).IsEqualTo(0);
			await Assert.That(duplicate.Stdout).Contains("\"existing\":true");
		}
		finally
		{
			Cli.Run(["session", "close", id]);
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	[Arguments("unity-project", "--no-wait")]
	[Arguments("primary-content", "--timeout=0")]
	public async Task DeferredOneShotRemainsQueryableAndWaits(string kind, string waiting)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-export";
		try
		{
			CliResult exported = Cli.Run(["export", kind, "--input", input, "--output", output, waiting], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_EXPORT_DELAY_MS"] = "1500" });
			await Assert.That(exported.ExitCode).IsEqualTo(0);
			await Assert.That(Property(exported, "status")).IsEqualTo("running");
			string id = Property(exported, "id");
			await Assert.That(exported.Stdout).Contains("job wait " + id);
			CliResult status = Cli.Run(["job", "status", id]);
			await Assert.That(status.ExitCode).IsEqualTo(0);
			await Assert.That(Property(status, "status")).IsEqualTo("running");
			CliResult timeout = Cli.Run(["job", "wait", id, "--timeout", "0"]);
			await Assert.That(timeout.ExitCode).IsEqualTo(0);
			await Assert.That(Property(timeout, "status")).IsEqualTo("running");
			CliResult completed = Cli.Run(["job", "wait", id]);
			await Assert.That(completed.ExitCode).IsEqualTo(0);
			await Assert.That(Property(completed, "status")).IsEqualTo("completed");
			await Assert.That(File.Exists(Path.Combine(output, ".assetripper-export.json"))).IsTrue();
		}
		finally
		{
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	[Arguments("unity-project")]
	[Arguments("primary-content")]
	public async Task CancelKeepsPartialOutputAndSessionStaysReadableWhileBusy(string kind)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-partial";
		string session = Property(Cli.Run(["session", "start", "--input", input], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_EXPORT_WRITE_DELAY_MS"] = "60000" }), "id");
		string? job = null;
		try
		{
			CliResult exported = Cli.Run(["export", kind, "--session", session, "--output", output, "--no-wait"]);
			await Assert.That(exported.ExitCode).IsEqualTo(0);
			job = Property(exported, "id");
			DateTime deadline = DateTime.UtcNow.AddSeconds(10);
			while ((!Directory.Exists(output) || Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length == 0) && DateTime.UtcNow < deadline) await Task.Delay(25);
			await Assert.That(Directory.Exists(output)).IsTrue();
			CliResult listed = Cli.Run(["list", "assets", "--session", session, "--name", "Greeting"]);
			await Assert.That(listed.ExitCode).IsEqualTo(0);
			using JsonDocument items = JsonDocument.Parse(listed.Stdout);
			string reference = items.RootElement.GetProperty("items")[0].GetProperty("ref").GetString()!;
			await Assert.That(Cli.Run(["get", "asset", reference, "--session", session]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["search", "Greeting", "--session", session]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["search", "get", "--session", session]).ExitCode).IsEqualTo(0);
			await Assert.That(Cli.Run(["--name", "get", "list", "assets", "--session", session]).ExitCode).IsEqualTo(0);
			string other = Property(Cli.Run(["session", "start", "--input", input]), "id");
			try
			{
				CliResult independent = Cli.Run(["export", kind, "--session", other, "--output", output + "-other-session"]);
				await Assert.That(independent.ExitCode).IsEqualTo(0);
			}
			finally
			{
				Cli.Run(["session", "close", other]);
				if (Directory.Exists(output + "-other-session")) Directory.Delete(output + "-other-session", true);
			}
			CliResult conflict = Cli.Run(["export", kind, "--session", session, "--output", output + "-other"]);
			await Assert.That(conflict.ExitCode).IsEqualTo(8);
			await Assert.That(conflict.Stdout).IsEmpty();
			await Assert.That(conflict.Stderr).Contains("job wait " + job);
			string assetOutput = output + "-asset.json";
			CliResult write = Cli.Run(["get", "asset", reference, "--session", session, "--output", assetOutput]);
			await Assert.That(write.ExitCode).IsEqualTo(0);
			await Assert.That(File.Exists(assetOutput)).IsTrue();
			await Assert.That(File.ReadAllText(assetOutput)).Contains("Hello from CLI fixture!");
			CliResult close = Cli.Run(["session", "close", session]);
			await Assert.That(close.ExitCode).IsEqualTo(8);
			await Assert.That(close.Stdout).IsEmpty();
			await Assert.That(close.Stderr).Contains("job cancel " + job);
			using JsonDocument closeError = JsonDocument.Parse(close.Stderr);
			await Assert.That(closeError.RootElement.GetProperty("code").GetString()).IsEqualTo("session_busy");
			CliResult cancelled = Cli.Run(["job", "cancel", job]);
			await Assert.That(cancelled.ExitCode).IsEqualTo(0);
			await Assert.That(Property(cancelled, "status")).IsEqualTo("cancelled");
			await Assert.That(Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length).IsGreaterThan(0);
			await Assert.That(File.Exists(Path.Combine(output, ".assetripper-export.json"))).IsFalse();
			CliResult retry = Cli.Run(["export", kind, "--session", session, "--output", output]);
			await Assert.That(retry.ExitCode).IsEqualTo(1);
			await Assert.That(retry.Stderr).Contains("output_not_empty");
		}
		finally
		{
			if (job is not null) Cli.Run(["job", "cancel", job]);
			Cli.Run(["session", "close", session]);
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
			if (File.Exists(output + "-asset.json")) File.Delete(output + "-asset.json");
		}
	}

	[Test]
	public async Task CompletedOneShotKeepsJobQueryableWithoutPretendingTheOwnerFailedToLoad()
	{
		string input = SessionTests.CreateGame();
		string output = input + "-oneshot-owner";
		string? owner = null;
		try
		{
			CliResult exported = Cli.Run(["export", "primary-content", "--input", input, "--output", output, "--timeout", "30"]);
			await Assert.That(exported.ExitCode).IsEqualTo(0);
			await Assert.That(Property(exported, "status")).IsEqualTo("completed");
			string job = Property(exported, "id");
			owner = job[..32];

			CliResult jobStatus = Cli.Run(["job", "status", job]);
			await Assert.That(jobStatus.ExitCode).IsEqualTo(0);
			await Assert.That(Property(jobStatus, "status")).IsEqualTo("completed");

			CliResult sessionStatus = Cli.Run(["session", "status", owner]);
			await Assert.That(sessionStatus.ExitCode).IsEqualTo(5);
			await Assert.That(sessionStatus.Stdout).IsEmpty();
			using JsonDocument error = JsonDocument.Parse(sessionStatus.Stderr);
			await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
			await Assert.That(sessionStatus.Stderr).DoesNotContain("readable inputs");

			CliResult inspect = Cli.Run(["list", "assets", "--session", owner]);
			await Assert.That(inspect.ExitCode).IsEqualTo(5);
			using JsonDocument inspectError = JsonDocument.Parse(inspect.Stderr);
			await Assert.That(inspectError.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");

			CliResult close = Cli.Run(["session", "close", owner]);
			await Assert.That(close.ExitCode).IsEqualTo(0);
			await Assert.That(Property(close, "status")).IsEqualTo("closed");
			owner = null;
			await Assert.That(Cli.Run(["session", "status", job[..32]]).ExitCode).IsEqualTo(5);
			await Assert.That(Cli.Run(["job", "status", job]).ExitCode).IsEqualTo(5);
		}
		finally
		{
			if (owner is not null)
			{
				Cli.Run(["session", "close", owner]);
			}

			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	[Arguments("unity-project")]
	[Arguments("primary-content")]
	public async Task OneShotJobOutlivesActualCallerProcess(string kind)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-subprocess";
		try
		{
			CliResult started = await RunProcess(["export", kind, "--input", input, "--output", output, "--no-wait"], "1500");
			await Assert.That(started.ExitCode).IsEqualTo(0);
			await Assert.That(Property(started, "status")).IsEqualTo("running");
			string id = Property(started, "id");
			CliResult status = await RunProcess(["job", "status", id]);
			await Assert.That(status.ExitCode).IsEqualTo(0);
			CliResult completed = await RunProcess(["job", "wait", id]);
			await Assert.That(completed.ExitCode).IsEqualTo(0);
			await Assert.That(Property(completed, "status")).IsEqualTo("completed");
			await Assert.That(File.Exists(Path.Combine(output, ".assetripper-export.json"))).IsTrue();
		}
		finally
		{
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	[Arguments("--no-wait")]
	[Arguments("--timeout=10")]
	public async Task DeferredExportsStillValidateMissingInputsBeforeStarting(string waiting)
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		CliResult result = Cli.Run(["export", "unity-project", "--input", path, "--output", path + "-out", waiting]);
		await Assert.That(result.ExitCode).IsEqualTo(5);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
	}

	[Test]
	[Arguments("unity-project")]
	[Arguments("primary-content")]
	public async Task DeferredExportsPreserveDestinationSafety(string kind)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-safe";
		Directory.CreateDirectory(output);
		File.WriteAllText(Path.Combine(output, "sentinel.txt"), "keep");
		try
		{
			CliResult refused = Cli.Run(["export", kind, "--input", input, "--output", output, "--no-wait"]);
			await Assert.That(refused.ExitCode).IsEqualTo(1);
			await Assert.That(refused.Stdout).IsEmpty();
			await Assert.That(refused.Stderr).Contains("output_not_empty");
			CliResult protectedTarget = Cli.Run(["export", kind, "--input", input, "--output", Path.Combine(output, "dOwNlOaDs"), "--no-wait"]);
			await Assert.That(protectedTarget.ExitCode).IsEqualTo(2);
			await Assert.That(protectedTarget.Stdout).IsEmpty();
			await Assert.That(File.ReadAllText(Path.Combine(output, "sentinel.txt"))).IsEqualTo("keep");
		}
		finally
		{
			Directory.Delete(input, true);
			Directory.Delete(output, true);
		}
	}

	[Test]
	[Arguments("status")]
	[Arguments("wait")]
	[Arguments("cancel")]
	public async Task MissingOrUnknownJobsAreNotFound(string action)
	{
		foreach (string[] args in new string[][] { ["job", action], ["job", action, "unknown"], ["job", action, new string('a', 64)] })
		{
			CliResult result = Cli.Run(args);
			await Assert.That(result.ExitCode).IsEqualTo(5);
			await Assert.That(result.Stdout).IsEmpty();
			using JsonDocument error = JsonDocument.Parse(result.Stderr);
			await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("not_found");
		}
	}

	[Test]
	[Arguments("--timeout=NaN")]
	[Arguments("--timeout=-1")]
	[Arguments("--timeout=Infinity")]
	[Arguments("--timeout=1 --timeout=2")]
	[Arguments("--timeout")]
	[Arguments("--session=a --session=b")]
	[Arguments("--no-wait --wait")]
	public async Task InvalidExportWaitingOptionsAreUsageErrors(string options)
	{
		string[] source = options.Contains("--session") ? [] : ["--input", "/missing"];
		CliResult result = Cli.Run(["export", "primary-content", .. source, "--output", "/out", .. options.Split(' ')]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	[Arguments("job")]
	[Arguments("job wait id --timeout=-1")]
	[Arguments("job wait id --timeout=NaN")]
	[Arguments("job wait id --timeout=Infinity")]
	[Arguments("job wait id --timeout=1 --timeout=2")]
	[Arguments("job wait id --timeout")]
	[Arguments("job wait id extra")]
	[Arguments("job status id --timeout=0")]
	[Arguments("job cancel id --timeout=0")]
	public async Task InvalidJobArgumentsAreUsageErrors(string command)
	{
		CliResult result = Cli.Run(command.Split(' '));
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument error = JsonDocument.Parse(result.Stderr);
		await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	[Arguments("unity-project")]
	[Arguments("primary-content")]
	public async Task SessionPreviewAndConfirmedWritePreserveUnrelatedFiles(string kind)
	{
		string input = SessionTests.CreateGame();
		string output = input + "-preview";
		Directory.CreateDirectory(output);
		File.WriteAllText(Path.Combine(output, "sentinel.txt"), "keep");
		string id = Property(Cli.Run(["session", "start", "--input", input]), "id");
		try
		{
			CliResult preview = Cli.Run(["export", kind, "--session", id, "--output", output, "--dry-run"]);
			await Assert.That(preview.ExitCode).IsEqualTo(0);
			await Assert.That(Property(preview, "status")).IsEqualTo("dry_run");
			await Assert.That(preview.Stdout).Contains(kind == "unity-project" ? "Greeting.txt" : "Greeting.bytes");
			await Assert.That(Directory.GetFiles(output).Length).IsEqualTo(1);
			await Assert.That(Cli.Run(["export", kind, "--session", id, "--output", output]).ExitCode).IsEqualTo(1);
			CliResult confirmed = Cli.Run(["export", kind, "--session", id, "--output", output + Path.DirectorySeparatorChar, "--confirm=" + Path.GetFileName(output)]);
			await Assert.That(confirmed.ExitCode).IsEqualTo(0);
			await Assert.That(File.ReadAllText(Path.Combine(output, "sentinel.txt"))).IsEqualTo("keep");
		}
		finally
		{
			Cli.Run(["session", "close", id]);
			Directory.Delete(input, true);
			Directory.Delete(output, true);
		}
	}

	[Test]
	public async Task ActiveExportKeepsOwnerAliveThenIdleOwnerEnds()
	{
		string input = SessionTests.CreateGame();
		string output = input + "-idle";
		try
		{
			CliResult started = Cli.Run(["export", "primary-content", "--input", input, "--output", output, "--no-wait"], environment: new Dictionary<string, string?> { ["ASSETRIPPER_TEST_SESSION_IDLE_MS"] = "500", ["ASSETRIPPER_TEST_EXPORT_DELAY_MS"] = "1500" });
			await Assert.That(started.ExitCode).IsEqualTo(0);
			string job = Property(started, "id");
			await Task.Delay(800);
			CliResult completed = Cli.Run(["job", "wait", job]);
			await Assert.That(completed.ExitCode).IsEqualTo(0);
			await Assert.That(Property(completed, "status")).IsEqualTo("completed");
			await Task.Delay(1000);
			await Assert.That(Cli.Run(["job", "status", job]).ExitCode).IsEqualTo(5);
		}
		finally
		{
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	public async Task FailedJobKeepsJsonAndHumanErrorModesDistinct()
	{
		string input = Directory.CreateTempSubdirectory("assetripper-empty-").FullName;
		string output = input + "-failed";
		string session = Property(Cli.Run(["session", "start", "--input", input]), "id");
		try
		{
			CliResult failed = Cli.Run(["export", "primary-content", "--session", session, "--output", output]);
			await Assert.That(failed.ExitCode).IsEqualTo(1);
			string job = Property(failed, "id");
			CliResult human = Cli.Run(["job", "status", job], stdoutIsTty: true);
			await Assert.That(human.ExitCode).IsEqualTo(1);
			await Assert.That(human.Stdout.StartsWith("Job ")).IsTrue();
			await Assert.That(human.Stderr.TrimStart().StartsWith('{')).IsFalse();
			CliResult json = Cli.Run(["job", "status", job, "--json"], stdoutIsTty: true);
			await Assert.That(json.ExitCode).IsEqualTo(1);
			using JsonDocument error = JsonDocument.Parse(json.Stderr);
			await Assert.That(error.RootElement.GetProperty("code").GetString()).IsEqualTo("export_failed");
		}
		finally
		{
			Cli.Run(["session", "close", session]);
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	[Test]
	public async Task DeferredOneShotCanBeCancelledAfterCallerExits()
	{
		string input = SessionTests.CreateGame();
		string output = input + "-cancelled-caller";
		try
		{
			CliResult started = await RunProcess(["export", "primary-content", "--input", input, "--output", output, "--timeout", "0.01"], "60000");
			await Assert.That(started.ExitCode).IsEqualTo(0);
			await Assert.That(Property(started, "status")).IsEqualTo("running");
			string job = Property(started, "id");
			CliResult cancelled = await RunProcess(["job", "cancel", job]);
			await Assert.That(cancelled.ExitCode).IsEqualTo(0);
			await Assert.That(Property(cancelled, "status")).IsEqualTo("cancelled");
			CliResult again = await RunProcess(["job", "wait", job]);
			await Assert.That(again.ExitCode).IsEqualTo(0);
			await Assert.That(Property(again, "status")).IsEqualTo("cancelled");
			await Assert.That(File.Exists(Path.Combine(output, ".assetripper-export.json"))).IsFalse();
		}
		finally
		{
			Directory.Delete(input, true);
			if (Directory.Exists(output)) Directory.Delete(output, true);
		}
	}

	private static async Task<CliResult> RunProcess(string[] args, string? delay = null)
	{
		string dotnet = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, "dotnet");
		ProcessStartInfo start = new(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "assetripper.dll"));
		foreach (string arg in args) start.ArgumentList.Add(arg);
		if (delay is not null) start.Environment["ASSETRIPPER_TEST_EXPORT_DELAY_MS"] = delay;
		using Process process = Process.Start(start)!;
		Task<string> stdout = process.StandardOutput.ReadToEndAsync();
		Task<string> stderr = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
		await process.WaitForExitAsync(timeout.Token);
		return new(process.ExitCode, await stdout, await stderr);
	}

	private static string Property(CliResult result, string name)
	{
		using JsonDocument json = JsonDocument.Parse(result.Stdout);
		return json.RootElement.GetProperty(name).GetString()!;
	}
}
