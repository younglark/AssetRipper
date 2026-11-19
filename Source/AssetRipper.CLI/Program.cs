namespace AssetRipper.CLI;

public static class Program
{
	private static readonly HashSet<string> KnownCommands = new(StringComparer.Ordinal)
	{
		"export",
		"list",
		"get",
		"search",
		"session",
		"job",
	};

	private static readonly HashSet<string> GlobalFlags = new(StringComparer.Ordinal)
	{
		"--help",
		"--json",
		"--version",
	};

	public static int Main(string[] args)
	{
		return Run(args, Console.Out, Console.Error, !Console.IsOutputRedirected, static name => Environment.GetEnvironmentVariable(name));
	}

	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool stdoutIsTty, Func<string, string?> getEnvironmentVariable)
	{
		_ = getEnvironmentVariable;

		bool jsonMode = CliOutput.IsJsonMode(stdoutIsTty, HasFlag(args, "--json"));
		string? command = GetCommand(args);
		bool hasDomainCommand = command is not null && KnownCommands.Contains(command);

		if (!hasDomainCommand)
		{
			string? invalidFlag = GetInvalidGlobalFlag(args);
			if (invalidFlag is not null)
			{
				return CliOutput.WriteError(
					stderr,
					jsonMode,
					ExitCode.Usage,
					"usage",
					$"Unknown option '{invalidFlag}'.",
					"Run assetripper --help to see available options.");
			}
		}

		if (args.Length == 0 || HasFlag(args, "--help"))
		{
			stdout.WriteLine(HelpText.ForCommand(hasDomainCommand ? command : null));
			return ExitCode.Success;
		}

		if (HasFlag(args, "--version"))
		{
			CliOutput.WriteVersion(stdout, jsonMode);
			return ExitCode.Success;
		}

		if (command is not null && !KnownCommands.Contains(command))
		{
			return CliOutput.WriteError(
				stderr,
				jsonMode,
				ExitCode.Usage,
				"usage",
				$"Unknown command '{command}'.",
				"Run assetripper --help to see available commands.");
		}

		if (command is not null)
		{
			return CliOutput.WriteError(
				stderr,
				jsonMode,
				ExitCode.RuntimeError,
				"not_implemented",
				$"The '{command}' command is not implemented in this build.",
				$"Run assetripper {command} --help to see the planned usage.");
		}

		stdout.WriteLine(HelpText.Root);
		return ExitCode.Success;
	}

	private static bool HasFlag(string[] args, string flag)
	{
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == flag)
			{
				return true;
			}
		}

		return false;
	}

	private static string? GetCommand(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			if (!args[i].StartsWith('-'))
			{
				return args[i];
			}
		}

		return null;
	}

	private static string? GetInvalidGlobalFlag(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (!arg.StartsWith('-'))
			{
				continue;
			}

			string flag = arg;
			int separator = arg.IndexOf('=');
			if (separator >= 0)
			{
				flag = arg[..separator];
			}

			if (!GlobalFlags.Contains(flag))
			{
				return arg;
			}
		}

		return null;
	}
}
