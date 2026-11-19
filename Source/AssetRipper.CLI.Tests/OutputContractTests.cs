using System.Text.Json;

namespace AssetRipper.CLI.Tests;

internal class OutputContractTests
{
	[Test]
	public async Task VersionDefaultsToJsonWhenStdoutIsNotATty()
	{
		CliResult result = Cli.Run(["--version"]);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		using JsonDocument document = JsonDocument.Parse(result.Stdout);
		string? version = document.RootElement.GetProperty("version").GetString();
		await Assert.That(version).IsNotNull();
		await Assert.That(version!).IsNotEmpty();
	}

	[Test]
	public async Task VersionDefaultsToTextWhenStdoutIsATty()
	{
		CliResult result = Cli.Run(["--version"], stdoutIsTty: true);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).IsNotEmpty();
		await Assert.That(LooksLikeJson(result.Stdout)).IsFalse();
		await Assert.That(result.Stdout).Contains("assetripper");
	}

	[Test]
	public async Task JsonFlagForcesVersionJsonEvenWhenStdoutIsATty()
	{
		CliResult withoutJson = Cli.Run(["--version"]);
		CliResult result = Cli.Run(["--json", "--version"], stdoutIsTty: true);

		await Assert.That(result.ExitCode).IsEqualTo(0);
		await Assert.That(result.Stderr).IsEmpty();
		await Assert.That(result.Stdout).IsEqualTo(withoutJson.Stdout);
		using JsonDocument document = JsonDocument.Parse(result.Stdout);
		await Assert.That(document.RootElement.GetProperty("version").GetString()).IsNotEmpty();
	}

	private static bool LooksLikeJson(string text)
	{
		string trimmed = text.TrimStart();
		return trimmed.StartsWith('{') || trimmed.StartsWith('[');
	}

	[Test]
	public async Task DoesNotUseColorOrSpinnerWhenStdoutIsNotATty()
	{
		CliResult[] results =
		[
			Cli.Run(["--help"]),
			Cli.Run(["--version"]),
			Cli.Run(["definitely-not-a-command"]),
		];

		foreach (CliResult result in results)
		{
			await Assert.That(ContainsAnsiOrSpinner(result.Stdout)).IsFalse();
			await Assert.That(ContainsAnsiOrSpinner(result.Stderr)).IsFalse();
		}
	}

	[Test]
	public async Task NoColorIsRespectedWhenStdoutIsATty()
	{
		Dictionary<string, string?> environment = new() { ["NO_COLOR"] = "1" };
		CliResult[] results =
		[
			Cli.Run(["--help"], stdoutIsTty: true, environment: environment),
			Cli.Run(["--version"], stdoutIsTty: true, environment: environment),
			Cli.Run(["definitely-not-a-command"], stdoutIsTty: true, environment: environment),
		];

		foreach (CliResult result in results)
		{
			await Assert.That(ContainsAnsiOrSpinner(result.Stdout)).IsFalse();
			await Assert.That(ContainsAnsiOrSpinner(result.Stderr)).IsFalse();
		}
	}

	[Test]
	[NotInParallel]
	public async Task DoesNotWriteALogFileToTheWorkingDirectoryByDefault()
	{
		string temp = Directory.CreateTempSubdirectory("assetripper-cli-").FullName;
		string original = Directory.GetCurrentDirectory();
		try
		{
			Directory.SetCurrentDirectory(temp);
			_ = Cli.Run(["--help"]);
			_ = Cli.Run(["--version"]);
			_ = Cli.Run(["definitely-not-a-command"]);
			string[] entries = Directory.GetFileSystemEntries(temp);
			await Assert.That(entries).IsEmpty();
		}
		finally
		{
			Directory.SetCurrentDirectory(original);
			Directory.Delete(temp, recursive: true);
		}
	}

	private static bool ContainsAnsiOrSpinner(string text)
	{
		return text.Contains('\u001b') || text.Contains('\r');
	}
}
