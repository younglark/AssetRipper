namespace AssetRipper.CLI;

internal sealed class InspectArguments
{
	public string Command { get; private set; } = "";
	public string? Kind { get; private set; }
	public string? Query { get; private set; }
	public List<string> Inputs { get; } = [];
	public string? Session { get; private set; }
	public int Limit { get; private set; } = 100;
	public string? Cursor { get; private set; }
	public string? Name { get; private set; }
	public string? Type { get; private set; }

	public static InspectArguments Parse(string[] args)
	{
		InspectArguments result = new();
		List<string> positionals = [];
		foreach ((string flag, string? value) in ReadArguments(args))
		{
			switch (flag)
			{
				case "--input":
					result.Inputs.Add(value!);
					break;
				case "--session":
					if (result.Session is not null)
					{
						throw new ArgumentException("Pass --session only once.");
					}

					result.Session = value;
					break;
				case "--limit":
					if (!int.TryParse(value, out int limit) || limit < 1)
					{
						throw new ArgumentException("Pass a positive integer for --limit.");
					}
					result.Limit = limit;
					break;
				case "--cursor":
					if (result.Cursor is not null)
					{
						throw new ArgumentException("Pass --cursor only once.");
					}
					result.Cursor = value;
					break;
				case "--name":
					if (result.Name is not null)
					{
						throw new ArgumentException("Pass --name only once.");
					}
					result.Name = value;
					break;
				case "--type":
					if (result.Type is not null)
					{
						throw new ArgumentException("Pass --type only once.");
					}
					result.Type = value;
					break;
				case "--json":
					break;
				default:
					positionals.Add(flag);
					break;
			}
		}

		if (positionals.Count == 0 || positionals[0] is not ("list" or "search"))
		{
			throw new ArgumentException("Unexpected arguments. Run assetripper --help.");
		}

		result.Command = positionals[0];
		if (result.Command == "list")
		{
			if (positionals.Count != 2)
			{
				throw new ArgumentException("Pass a list kind: assets, bundles, scenes, collections, resources, or failed-files.");
			}
			result.Kind = positionals[1];
			if (result.Type is not null && result.Kind != "assets")
			{
				throw new ArgumentException("--type can only be used with list assets.");
			}
		}
		else
		{
			if (positionals.Count != 2)
			{
				throw new ArgumentException("Pass a search query.");
			}
			result.Query = positionals[1];
			if (result.Name is not null || result.Type is not null)
			{
				throw new ArgumentException("search does not accept --name or --type.");
			}
		}

		if ((result.Session is null) == (result.Inputs.Count == 0))
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
			if (flag is "--input" or "--session" or "--limit" or "--cursor" or "--name" or "--type")
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
