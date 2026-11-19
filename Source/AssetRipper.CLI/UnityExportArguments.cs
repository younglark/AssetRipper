namespace AssetRipper.CLI;

internal sealed class UnityExportArguments
{
	public string Kind { get; private set; } = "";
	public List<string> Inputs { get; } = [];
	public string Output { get; private set; } = "";
	public bool DryRun { get; private set; }
	public string? Confirm { get; private set; }
	public bool Unsupported { get; private set; }

	public static UnityExportArguments Parse(string[] args)
	{
		UnityExportArguments result = new();
		List<string> positionals = [];
		bool session = false;
		foreach ((string flag, string? value) in ReadArguments(args))
		{
			switch (flag)
			{
				case "--input":
					result.Inputs.Add(value!);
					break;
				case "--output":
					if (result.Output.Length > 0)
					{
						throw new ArgumentException("Pass --output only once.");
					}
					result.Output = value!;
					break;
				case "--dry-run":
					result.DryRun = true;
					break;
				case "--session":
					session = true;
					result.Unsupported = true;
					break;
				case "--timeout":
				case "--no-wait":
					result.Unsupported = true;
					break;
				case "--confirm":
					if (result.Confirm is not null)
					{
						throw new ArgumentException("Pass --confirm only once.");
					}
					result.Confirm = value!;
					break;
				case "--wait":
				case "--json":
					break;
				default:
					positionals.Add(flag);
					break;
			}
		}
		if (positionals.Count != 2 || positionals[0] != "export" || positionals[1] is not ("unity-project" or "primary-content"))
		{
			throw new ArgumentException("Unexpected arguments. Run assetripper export --help.");
		}
		result.Kind = positionals[1];
		if (result.Output.Length == 0)
		{
			throw new ArgumentException("Pass --output <dir>.");
		}
		if (session && result.Inputs.Count > 0)
		{
			throw new ArgumentException("Use --input or --session, not both.");
		}
		if (result.Inputs.Count == 0 && !session)
		{
			throw new ArgumentException("Pass exactly one of --input <path> (repeatable) or --session <id>; they are mutually exclusive.");
		}
		return result;
	}

	private static IEnumerable<(string Flag, string? Value)> ReadArguments(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			int separator = arg.IndexOf('=');
			string flag = separator >= 0 ? arg[..separator] : arg;
			if (flag is "--input" or "--output" or "--session" or "--timeout" or "--confirm")
			{
				string? value = separator >= 0 ? arg[(separator + 1)..]
					: i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null;
				if (string.IsNullOrWhiteSpace(value))
				{
					throw new ArgumentException($"Pass a value for {flag}.");
				}
				yield return (flag, value);
			}
			else
			{
				yield return (arg, null);
			}
		}
	}
}
