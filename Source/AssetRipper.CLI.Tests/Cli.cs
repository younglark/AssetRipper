namespace AssetRipper.CLI.Tests;

internal readonly record struct CliResult(int ExitCode, string Stdout, string Stderr);

internal static class Cli
{
	public static CliResult Run(string[] args, bool stdoutIsTty = false, IReadOnlyDictionary<string, string?>? environment = null)
	{
		using StringWriter stdout = new();
		using StringWriter stderr = new();
		int exitCode = Program.Run(args, stdout, stderr, stdoutIsTty, name => GetEnvironmentVariable(name, environment));
		return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
	}

	private static string? GetEnvironmentVariable(string name, IReadOnlyDictionary<string, string?>? environment)
	{
		if (environment is not null && environment.TryGetValue(name, out string? value))
		{
			return value;
		}

		return null;
	}
}
