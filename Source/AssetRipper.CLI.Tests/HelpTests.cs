namespace AssetRipper.CLI.Tests;

internal class HelpTests
{
	[Test]
	public async Task HelpExitsZeroAndDocumentsExamplesAndExitCodes()
	{
		CliResult result = Cli.Run(["--help"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout).Contains("EXAMPLES");
		await Assert.That(result.Stdout).Contains("EXIT CODES");
		await Assert.That(result.Stdout).Contains("already-completed export");
		await Assert.That(result.Stdout).Contains("still running but now queryable");
		await Assert.That(result.Stdout).Contains("Usage error");
		await Assert.That(result.Stdout).Contains("input path, Session, Job, or Asset Reference");
		await Assert.That(result.Stdout).Contains("safe to retry the same command");
		await Assert.That(result.Stdout).Contains("Other runtime failure");
	}

	[Test]
	public async Task NoArgumentsShowsHelpAndExitsZero()
	{
		CliResult help = Cli.Run(["--help"]);
		CliResult result = Cli.Run([]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout).IsEqualTo(help.Stdout);
		await Assert.That(result.Stderr).IsEmpty();
	}

	[Test]
	public async Task HelpIsWrittenToStdoutOnly()
	{
		CliResult result = Cli.Run(["--help"]);

		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).IsNotEmpty();
	}

	[Test]
	public async Task JsonFlagAloneShowsHelpAndExitsZero()
	{
		CliResult help = Cli.Run(["--help"]);
		CliResult result = Cli.Run(["--json"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stdout).IsEqualTo(help.Stdout);
		await Assert.That(result.Stderr).IsEmpty();
	}

	[Test]
	public async Task HelpStaysTextEvenWhenJsonModeWouldApply()
	{
		CliResult result = Cli.Run(["--json", "--help"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("EXAMPLES");
		await Assert.That(result.Stdout.TrimStart().StartsWith('{')).IsFalse();
	}

	[Test]
	[Arguments("export")]
	[Arguments("list")]
	[Arguments("get")]
	[Arguments("search")]
	[Arguments("session")]
	[Arguments("job")]
	public async Task SubcommandHelpDocumentsExamplesAndExitCodes(string command)
	{
		CliResult result = Cli.Run([command, "--help"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains($"Usage: assetripper {command}");
		await Assert.That(result.Stdout).Contains("EXAMPLES");
		await Assert.That(result.Stdout).Contains("EXIT CODES");
	}

	[Test]
	public async Task HelpSeparatesExamplesFromExitCodes()
	{
		CliResult result = Cli.Run(["--help"]);

		await Assert.That(result.Stdout).Contains("\n\nEXIT CODES");
	}

	[Test]
	public async Task ExportHelpDocumentsSessionAndDeferredExecution()
	{
		CliResult result = Cli.Run(["export", "unity-project", "--help"]);
		await Assert.That(result.Stdout).Contains("Export waits for completion by default");
		await Assert.That(result.Stdout).Contains("files that would be written without changing the disk");
		await Assert.That(result.Stdout).Contains("--confirm=<directory name>");
		await Assert.That(result.Stdout).Contains("existing: true");
		await Assert.That(result.Stdout).DoesNotContain("refused even with --confirm");
		await Assert.That(result.Stdout).DoesNotContain("not implemented in this build");
		await Assert.That(result.Stdout).Contains("--timeout").And.Contains("--no-wait").And.Contains("job wait <id>").And.Contains("exit 0, not 8");
	}
}
