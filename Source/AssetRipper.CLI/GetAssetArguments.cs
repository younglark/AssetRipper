namespace AssetRipper.CLI;

internal sealed class GetAssetArguments
{
	internal static readonly string[] LegalFormats =
	[
		"json",
		"yaml",
		"text",
		"binary",
		"image",
		"audio",
		"model",
		"font",
		"video",
	];

	public string? AssetReference { get; private set; }
	public List<string> Inputs { get; } = [];
	public string? Session { get; private set; }
	public string Format { get; private set; } = "json";
	public string? Output { get; private set; }
	public string? Confirm { get; private set; }

	public static GetAssetArguments Parse(string[] args)
	{
		GetAssetArguments result = new();
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
				case "--format":
					if (string.IsNullOrWhiteSpace(value))
					{
						throw new ArgumentException("Pass a value for --format.");
					}
					string format = value.ToLowerInvariant();
					if (!LegalFormats.Contains(format))
					{
						throw new ArgumentException($"Unknown --format value '{value}'.");
					}
					result.Format = format;
					break;
				case "--output":
					if (result.Output is not null)
					{
						throw new ArgumentException("Pass --output only once.");
					}
					result.Output = value;
					break;
				case "--confirm":
					if (result.Confirm is not null)
					{
						throw new ArgumentException("Pass --confirm only once.");
					}
					result.Confirm = value;
					break;
				case "--json":
					break;
				default:
					positionals.Add(flag);
					break;
			}
		}

		if (positionals.Count < 2 || positionals[0] != "get" || positionals[1] != "asset")
		{
			throw new ArgumentException("Unexpected arguments. Run assetripper get --help.");
		}
		if (positionals.Count != 3)
		{
			throw new ArgumentException("Pass an Asset Reference: get asset <ref>.");
		}

		result.AssetReference = positionals[2];
		if ((result.Session is null) == (result.Inputs.Count == 0))
		{
			throw new ArgumentException("Pass exactly one of --input <path> (repeatable) or --session <id>; they are mutually exclusive.");
		}

		return result;
	}

	public static string LegalFormatList => "json, yaml, text, binary, image, audio, model, font, and video";

	private static IEnumerable<(string Flag, string? Value)> ReadArguments(string[] args)
	{
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			int separator = arg.IndexOf('=');
			string flag = separator >= 0 ? arg[..separator] : arg;
			if (flag is "--input" or "--session" or "--format" or "--output" or "--confirm")
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
