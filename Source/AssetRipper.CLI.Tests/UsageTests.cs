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
	[Arguments("--yes")]
	[Arguments("--allow-mutations")]
	public async Task ThereIsNoGlobalYesOrAllowMutationsSwitch(string flag)
	{
		CliResult result = Cli.Run(["export", "unity-project", "--input", "Game", "--output", "Out", flag]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		await Assert.That(result.Stderr).Contains(flag);
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
		CliResult result = Cli.Run(["list", "assets", "--input", "./Game"]);

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

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnknownFlagsOnKnownCommandsAreUsageErrorsEvenWithHelp(bool help)
	{
		string[] args = help ? ["export", "--help", "--definitely-not-a-flag"] : ["export", "--definitely-not-a-flag"];
		CliResult result = Cli.Run(args);

		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
		await Assert.That(document.RootElement.GetProperty("hint").GetString()).Contains("--help");
	}

	[Test]
	[Arguments("definitely-not-a-command", null)]
	[Arguments("export", "definitely-not-a-subcommand")]
	[Arguments("list", "definitely-not-a-kind")]
	[Arguments("get", "definitely-not-asset")]
	[Arguments("session", "definitely-not-a-subcommand")]
	[Arguments("job", "definitely-not-a-subcommand")]
	public async Task UnknownCommandPathsAreUsageErrorsEvenWithHelp(string command, string? subcommand)
	{
		foreach (bool help in new[] { false, true })
		{
			List<string> args = [command];
			if (subcommand is not null)
			{
				args.Add(subcommand);
			}
			if (help)
			{
				args.Add("--help");
			}

			CliResult result = Cli.Run(args.ToArray());
			await Assert.That(result.ExitCode).IsEqualTo(2);
			await Assert.That(result.Stdout).IsEmpty();
			using JsonDocument document = JsonDocument.Parse(result.Stderr);
			await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
			await Assert.That(document.RootElement.GetProperty("hint").GetString()).Contains("--help");
		}
	}

	[Test]
	[Arguments("export primary-content --session=id --output Out --dry-run --confirm Out --timeout 60 --no-wait --wait")]
	[Arguments("list assets --input Game --session=id --limit 100 --cursor token --name *.png --type Texture2D")]
	[Arguments("get asset ref --input Game --session=id --format json --output out.json --confirm=out.json")]
	[Arguments("search Player --input Game --session=id --limit 100 --cursor token")]
	[Arguments("session start --input Game --timeout 60 --no-wait --wait")]
	[Arguments("job wait id --timeout 60")]
	public async Task PlannedOptionsAreRecognizedWithoutImplementingDomainCommands(string commandLine)
	{
		string[] args = commandLine.Split(' ');
		CliResult result = Cli.Run(args);
		await Assert.That(result.ExitCode).IsEqualTo(1);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("not_implemented");

		CliResult help = Cli.Run([.. args, "--help"]);
		await Assert.That(help.ExitCode).IsEqualTo(0);
		await Assert.That(help.Stderr).IsEmpty();
		await Assert.That(help.Stdout).Contains("EXAMPLES");
	}

	[Test]
	[Arguments("export")]
	[Arguments("list")]
	[Arguments("get")]
	[Arguments("search")]
	[Arguments("session")]
	[Arguments("job")]
	public async Task UnknownFlagsOnEveryDomainCommandCannotBeHiddenByHelp(string command)
	{
		CliResult result = Cli.Run([command, "--help", "--unknown"]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
		using JsonDocument document = JsonDocument.Parse(result.Stderr);
		await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("usage");
	}

	[Test]
	[Arguments("export --input Game unity-project")]
	[Arguments("get --output out.json asset ref")]
	[Arguments("session --input Game start")]
	[Arguments("job --timeout 60 wait id")]
	[Arguments("search --limit 20 Player")]
	public async Task OptionValuesAreNotMistakenForSubcommands(string commandLine)
	{
		CliResult result = Cli.Run([.. commandLine.Split(' '), "--help"]);
		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
	}

	[Test]
	[Arguments("--help=unexpected")]
	[Arguments("--json=unexpected")]
	[Arguments("--version=unexpected")]
	public async Task BooleanGlobalFlagsRejectValues(string flag)
	{
		CliResult result = Cli.Run([flag]);
		await Assert.That(result.ExitCode).IsEqualTo(2);
		await Assert.That(result.Stdout).IsEmpty();
	}
}
