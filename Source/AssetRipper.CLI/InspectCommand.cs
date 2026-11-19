using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.Text;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class InspectCommand
{
	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		try
		{
			InspectArguments options = InspectArguments.Parse(args);
			if (options.Session is not null)
			{
				return SessionCommand.Query(options.Session, args, stdout, stderr, jsonMode);
			}

			return Execute(options, stdout, stderr, jsonMode, fileSystem);
		}
		catch (ArgumentException exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", exception.Message, "Run assetripper list --help or assetripper search --help. " + exception.Message);
		}
		catch (Exception exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "inspect_failed", exception.Message, "Check the inputs and try again.");
		}
	}

	private static int Execute(InspectArguments options, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		// Validate the cursor before loading so a bad token stays a usage error.
		_ = DecodeCursor(options.Cursor);

		foreach (string input in options.Inputs)
		{
			try
			{
				if (fileSystem.Directory.Exists(input))
				{
					foreach (string file in fileSystem.Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
					{
						using Stream stream = fileSystem.File.OpenRead(file);
					}
				}
				else
				{
					using Stream stream = fileSystem.File.OpenRead(input);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				return CliOutput.WriteError(stderr, jsonMode, ExitCode.NotFound, "not_found", $"Input path '{input}' is missing or unreadable: {exception.Message}", "Pass an existing, readable --input path.");
			}
		}

		lock (CliSync.LoadLock)
		{
			StderrLogger logger = new(stderr);
			Logger.Add(logger);
			try
			{
				FullConfiguration settings = new();
				settings.LoadFromDefaultPath();
				ExportHandler handler = new(settings);
				GameData gameData = handler.LoadAndProcess(options.Inputs, fileSystem);
				try
				{
					return ExecuteLoaded(options, gameData, stdout, jsonMode);
				}
				finally
				{
					gameData.AssemblyManager.Dispose();
				}
			}
			catch (Exception exception)
			{
				return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "inspect_failed", exception.Message, "Check the inputs and try again.");
			}
			finally
			{
				Logger.Remove(logger);
			}
		}
	}

	internal static int ExecuteLoaded(InspectArguments options, GameData gameData, TextWriter stdout, bool jsonMode)
	{
		List<InspectItem> items = CollectItems(gameData.GameBundle, options);
		(List<InspectItem> page, bool truncated, string? next) = Paginate(items, options);
		WriteResult(stdout, jsonMode, page, truncated, next);
		return ExitCode.Success;
	}

	private static List<InspectItem> CollectItems(GameBundle gameBundle, InspectArguments options)
	{
		List<InspectItem> items = [];
		if (options.Command == "search" || options.Kind == "assets")
		{
			foreach (IUnityObjectBase asset in gameBundle.FetchAssets())
			{
				string name = asset.GetBestName();
				if (options.Command == "search")
				{
					if (!name.Contains(options.Query!, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
				}
				else if (!MatchesName(name, options.Name) || !MatchesType(asset.ClassName, options.Type))
				{
					continue;
				}

				items.Add(new InspectItem
				{
					Name = name,
					Type = asset.ClassName,
					Ref = $"{asset.Collection.Name}:{asset.PathID}",
				});
			}
		}
		else
		{
			IEnumerable<string> names = options.Kind switch
			{
				"bundles" => FetchBundles(gameBundle).Select(bundle => bundle.Name),
				"scenes" => gameBundle.Scenes.Select(scene => scene.Name),
				"collections" => gameBundle.FetchAssetCollections().Select(collection => collection.Name),
				"resources" => gameBundle.FetchResourceFiles().Select(resource => resource.Name),
				"failed-files" => FetchFailedFiles(gameBundle).Select(file => file.Name),
				_ => throw new ArgumentException("Pass a list kind: assets, bundles, scenes, collections, resources, or failed-files."),
			};
			foreach (string name in names)
			{
				if (MatchesName(name, options.Name))
				{
					items.Add(new InspectItem { Name = name });
				}
			}
		}

		items.Sort(CompareItems);
		return items;
	}

	private static IEnumerable<Bundle> FetchBundles(Bundle bundle)
	{
		yield return bundle;
		foreach (Bundle child in bundle.Bundles)
		{
			foreach (Bundle nested in FetchBundles(child))
			{
				yield return nested;
			}
		}
	}

	private static IEnumerable<FailedFile> FetchFailedFiles(Bundle bundle)
	{
		foreach (FailedFile failed in bundle.FailedFiles)
		{
			yield return failed;
		}
		foreach (Bundle child in bundle.Bundles)
		{
			foreach (FailedFile failed in FetchFailedFiles(child))
			{
				yield return failed;
			}
		}
	}

	private static (List<InspectItem> Page, bool Truncated, string? Next) Paginate(List<InspectItem> items, InspectArguments options)
	{
		int offset = DecodeCursor(options.Cursor);
		if (offset > items.Count)
		{
			offset = items.Count;
		}

		List<InspectItem> page = items.Skip(offset).Take(options.Limit).ToList();
		bool truncated = offset + page.Count < items.Count;
		return (page, truncated, truncated ? EncodeCursor(offset + page.Count) : null);
	}

	private static int DecodeCursor(string? cursor)
	{
		if (string.IsNullOrEmpty(cursor))
		{
			return 0;
		}

		try
		{
			string text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
			if (text.StartsWith("o:", StringComparison.Ordinal) && int.TryParse(text[2..], out int offset) && offset >= 0)
			{
				return offset;
			}
		}
		catch (FormatException)
		{
		}

		throw new ArgumentException("Pass a --cursor token from a previous truncated result.");
	}

	private static string EncodeCursor(int offset)
	{
		return Convert.ToBase64String(Encoding.UTF8.GetBytes($"o:{offset}"));
	}


	private static bool MatchesName(string name, string? pattern)
	{
		return string.IsNullOrEmpty(pattern)
			|| System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true);
	}

	private static bool MatchesType(string type, string? expected)
	{
		return string.IsNullOrEmpty(expected)
			|| string.Equals(type, expected, StringComparison.OrdinalIgnoreCase);
	}

	private static int CompareItems(InspectItem left, InspectItem right)
	{
		int name = string.CompareOrdinal(left.Name, right.Name);
		if (name != 0)
		{
			return name;
		}

		return string.CompareOrdinal(left.Ref, right.Ref);
	}

	private static void WriteResult(TextWriter stdout, bool jsonMode, IReadOnlyList<InspectItem> items, bool truncated, string? next)
	{
		if (jsonMode)
		{
			ListPayload payload = new()
			{
				Items = items,
				Truncated = truncated ? true : null,
				Next = next,
				Hint = truncated ? "Pass --cursor with this next token to fetch the next page." : null,
			};
			stdout.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.ListPayload));
		}
		else
		{
			foreach (InspectItem item in items)
			{
				if (item.Type is not null && item.Ref is not null)
				{
					stdout.WriteLine($"{item.Name}\t{item.Type}\t{item.Ref}");
				}
				else
				{
					stdout.WriteLine(item.Name);
				}
			}
			if (truncated && next is not null)
			{
				stdout.WriteLine($"truncated; pass --cursor {next} to fetch the next page.");
			}
		}
	}

	private sealed class StderrLogger(TextWriter writer) : ILogger
	{
		public void Log(LogType type, LogCategory category, string message) => writer.WriteLine($"{category}: {message}");
		public void BlankLine(int numLines)
		{
			for (int i = 0; i < numLines; i++)
			{
				writer.WriteLine();
			}
		}
	}
}
