using System.Text.Json;

namespace AssetRipper.CLI.Tests;

internal class UsageTests
{
	[Test]
	public async Task UnknownCommandExitsTwoAndWritesErrorToStderr()
	{
		CliResult result = Cli.Run(["definitely-not-a-command"]);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(result.Stderr).Contains("definitely-not-a-command");
		await Assert.That(result.Stderr).Contains("--help");
	}

	[Test]
	public async Task InvalidFlagExitsTwoAndWritesErrorToStderr()
	{
		CliResult result = Cli.Run(["--definitely-not-a-flag"]);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(result.Stderr).Contains("--definitely-not-a-flag");
		await Assert.That(result.Stderr).Contains("--help");
	}

	[Test]
	public async Task InvalidFlagIsNotTreatedAsADefaultRunEvenWhenHelpIsAlsoPresent()
	{
		CliResult result = Cli.Run(["--help", "--definitely-not-a-flag"]);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(result.Stderr).Contains("--definitely-not-a-flag");
	}

	[Test]
	public async Task JsonModeWritesUsageErrorsAsJsonOnStderrWithCodeAndHint()
	{
		CliResult result = Cli.Run(["definitely-not-a-command"]);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();

		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		JsonElement root = document.RootElement;
		await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("usage");
		string? hint = root.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		await Assert.That(hint!).Contains("--help");
	}

	[Test]
	public async Task JsonFlagForcesJsonErrorsEvenWhenStdoutIsATty()
	{
		CliResult result = Cli.Run(["--json", "definitely-not-a-command"], stdoutIsTty: true);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(document.RootElement.GetProperty("hint").GetString()).IsNotNull();
	}

	[Test]
	public async Task TtyWithoutJsonWritesTextUsageErrors()
	{
		CliResult result = Cli.Run(["definitely-not-a-command"], stdoutIsTty: true);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(result.Stderr).Contains("definitely-not-a-command");
		await Assert.That(result.Stderr).Contains("--help");
		await Assert.That(LooksLikeJson(result.Stderr)).IsFalse();
	}

	private static bool LooksLikeJson(string text)
	{
		string trimmed = text.TrimStart();
		return trimmed.StartsWith('{') || trimmed.StartsWith('[');
	}

	[Test]
	public async Task KnownUnimplementedCommandDoesNotHangAndExitsAsARuntimeError()
	{
		CliResult result = Cli.Run(["export", "unity-project", "--input", "./Game", "--output", "./Out"]);

		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("not_implemented");
		string? hint = document.RootElement.GetProperty("hint").GetString();
		await Assert.That(hint).IsNotNull();
		await Assert.That(hint!).Contains("--help");
	}

	[Test]
	public async Task SubcommandHelpExitsZeroAndDocumentsTheCommand()
	{
		CliResult result = Cli.Run(["export", "--help"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).Contains("Usage: assetripper export");
		await Assert.That(result.Stdout).Contains("--output");
		await Assert.That(result.Stdout).Contains("--dry-run");
		await Assert.That(result.Stdout).Contains("--confirm");
		await Assert.That(result.Stdout).Contains("EXAMPLES");
		await Assert.That(result.Stdout).Contains("EXIT CODES");
	}
}
