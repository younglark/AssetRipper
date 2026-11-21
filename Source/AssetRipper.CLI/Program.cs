using AssetRipper.IO.Files;

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
		if (args.Length >= 2 && args[0] == "--session-host")
		{
			return SessionHost.Run(args[1], args[2..]);
		}
		return Run(args, Console.Out, Console.Error, !Console.IsOutputRedirected, static name => Environment.GetEnvironmentVariable(name));
	}

	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool stdoutIsTty, Func<string, string?> getEnvironmentVariable, FileSystem? fileSystem = null)
	{

		bool jsonMode = CliOutput.IsJsonMode(stdoutIsTty, HasFlag(args, "--json"));
		List<string> positionals = GetPositionals(args);
		string? command = positionals.FirstOrDefault();
		bool hasDomainCommand = command is not null && KnownCommands.Contains(command);

		string? invalidFlag = GetInvalidFlag(args, command);
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

		string? unknownCommand = GetUnknownCommand(positionals);
		if (unknownCommand is not null)
		{
			return CliOutput.WriteError(
				stderr,
				jsonMode,
				ExitCode.Usage,
				"usage",
				$"Unknown command '{unknownCommand}'.",
				"Run assetripper --help to see available commands.");
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

		if (command == "export" && positionals.Count > 1 && positionals[1] is "unity-project" or "primary-content")
		{
			return UnityProjectExport.Run(args, stdout, stderr, jsonMode, fileSystem ?? LocalFileSystem.Instance, getEnvironmentVariable);
		}

		if (command is "list" or "search")
		{
			return InspectCommand.Run(args, stdout, stderr, jsonMode, fileSystem ?? LocalFileSystem.Instance);
		}

		if (command == "get" && positionals.Count > 1 && positionals[1] == "asset")
		{
			return GetAssetCommand.Run(args, stdout, stderr, jsonMode, fileSystem ?? LocalFileSystem.Instance);
		}

		if (command == "session")
		{
			return SessionCommand.Run(args, stdout, stderr, jsonMode, getEnvironmentVariable);
		}

		if (command == "job")
		{
			return JobCommand.Run(args, stdout, stderr, jsonMode);
		}

		if (command is not null)
		{
			return CliOutput.WriteError(
				stderr,
				jsonMode,
				ExitCode.Usage,
				"usage",
				$"The '{command}' command requires a subcommand.",
				$"Run assetripper {command} --help to see available usage.");
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

	internal static string? GetCommandName(string[] args) => GetPositionals(args).FirstOrDefault();

	private static List<string> GetPositionals(string[] args)
	{
		List<string> positionals = [];
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (!arg.StartsWith('-'))
			{
				positionals.Add(arg);
			}
			else if (TakesValue(arg) && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
			{
				i++;
			}
		}

		return positionals;
	}

	private static bool TakesValue(string flag)
	{
		return flag is "--input" or "--session" or "--output" or "--confirm" or "--timeout"
			or "--limit" or "--cursor" or "--name" or "--type" or "--format" or "--sprite-export-mode";
	}

	private static string? GetUnknownCommand(List<string> positionals)
	{
		if (positionals.Count == 0)
		{
			return null;
		}

		string command = positionals[0];
		if (!KnownCommands.Contains(command))
		{
			return command;
		}

		if (positionals.Count < 2 || command == "search")
		{
			return null;
		}

		string subcommand = positionals[1];
		bool known = command switch
		{
			"export" => subcommand is "unity-project" or "primary-content",
			"list" => subcommand is "assets" or "bundles" or "scenes" or "collections" or "resources" or "failed-files",
			"get" => subcommand == "asset",
			"session" => subcommand is "start" or "status" or "close",
			"job" => subcommand is "status" or "wait" or "cancel",
			_ => false,
		};
		return known ? null : $"{command} {subcommand}";
	}

	private static string? GetInvalidFlag(string[] args, string? command)
	{
		string[] plannedFlags = command switch
		{
			"export" => ["--input", "--session", "--output", "--dry-run", "--confirm", "--timeout", "--no-wait", "--wait", "--sprite-export-mode"],
			"list" => ["--input", "--session", "--limit", "--cursor", "--name", "--type"],
			"get" => ["--input", "--session", "--format", "--output", "--confirm"],
			"search" => ["--input", "--session", "--limit", "--cursor"],
			"session" => ["--input", "--timeout", "--no-wait", "--wait"],
			"job" => ["--timeout"],
			_ => [],
		};

		foreach (string arg in args)
		{
			if (!arg.StartsWith('-'))
			{
				continue;
			}

			int separator = arg.IndexOf('=');
			string flag = separator >= 0 ? arg[..separator] : arg;
			bool takesValue = TakesValue(flag);
			if ((!GlobalFlags.Contains(flag) && !plannedFlags.Contains(flag)) || (separator >= 0 && !takesValue))
			{
				return arg;
			}
		}

		return null;
	}
}
