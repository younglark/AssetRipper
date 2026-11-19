using AssetRipper.Assets.Bundles;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class UnityProjectExport
{
		public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		try
		{
			UnityExportArguments options = UnityExportArguments.Parse(args);
			return Execute(options, stdout, stderr, jsonMode, fileSystem);
		}
		catch (ArgumentException exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", exception.Message, "Run assetripper export --help. " + exception.Message);
		}
		catch (Exception exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "export_failed", exception.Message, "Check the inputs and choose a writable --output directory.");
		}
	}

	private static int Execute(UnityExportArguments options, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		if (options.Unsupported)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "not_implemented", "Session and deferred export options are not implemented in this build.", "Use --input and omit --session, --timeout, and --no-wait for a synchronous export.");
		}
		string output = options.Output;

		string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(output)));
		if (new[] { "Desktop", "Documents", "Downloads" }.Contains(name, StringComparer.OrdinalIgnoreCase))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", "The output is a protected personal directory.", "Choose a different --output directory.");
		}

		List<string> inputs = options.Inputs;
		foreach (string input in inputs)
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

		if (fileSystem.File.Exists(output))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "output_is_file", "The output path is an existing file.", "Choose a new or empty --output directory.");
		}

		lock (CliSync.LoadLock)
		{
			if (!options.DryRun && TryGetExistingExport(output, inputs, options.Kind, fileSystem))
			{
				WriteResult(stdout, jsonMode, options.Kind, "completed", output, existing: true);
				return ExitCode.Success;
			}

			if (!options.DryRun && IsNonEmptyDirectory(output, fileSystem) && !ConfirmMatches(output, options.Confirm, fileSystem))
			{
				return RefuseNonEmptyOutput(stderr, jsonMode);
			}

			StderrLogger logger = new(stderr);
			Logger.Add(logger);
			try
			{
				FullConfiguration settings = new();
				settings.LoadFromDefaultPath();
				ExportHandler handler = new(settings);
				GameData gameData = handler.LoadAndProcess(inputs, fileSystem);
				try
				{
					if (!gameData.GameBundle.HasAnyAssetCollections())
					{
						throw new InvalidOperationException("No loadable Unity asset collections were found.");
					}

					if (options.DryRun)
					{
						VirtualFileSystem preview = new();
						const string previewRoot = "/preview";
						ExportKind(options.Kind, handler, settings, gameData, previewRoot, preview);
						WriteResult(stdout, jsonMode, options.Kind, "dry_run", output, ListExportedFiles(preview, previewRoot));
						return ExitCode.Success;
					}

					// Loading can take a long time. Refuse if the destination changed while loading.
					if (IsNonEmptyDirectory(output, fileSystem) && !ConfirmMatches(output, options.Confirm, fileSystem))
					{
						return RefuseNonEmptyOutput(stderr, jsonMode);
					}
					ExportKind(options.Kind, handler, settings, gameData, output, fileSystem);
					WriteManifest(output, inputs, options.Kind, fileSystem);
				}
				finally
				{
					gameData.AssemblyManager.Dispose();
				}
				WriteResult(stdout, jsonMode, options.Kind, "completed", output);
				return ExitCode.Success;
			}
			catch (Exception exception)
			{
				return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "export_failed", exception.Message, "Check the inputs and choose a writable --output directory.");
			}
			finally
			{
				Logger.Remove(logger);
			}
		}
	}

	private static bool ConfirmMatches(string output, string? confirm, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(confirm))
		{
			return false;
		}

		string name = fileSystem.Path.GetFileName(fileSystem.Path.GetFullPath(output));
		return string.Equals(confirm, name, StringComparison.Ordinal);
	}

	private static bool IsNonEmptyDirectory(string output, FileSystem fileSystem)
	{
		return fileSystem.Directory.Exists(output)
			&& (fileSystem.Directory.EnumerateFiles(output).Any() || fileSystem.Directory.EnumerateDirectories(output).Any());
	}

	private static int RefuseNonEmptyOutput(TextWriter stderr, bool jsonMode)
	{
		return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "output_not_empty",
			"The output directory is not empty.", "Choose another directory, empty this one, or pass --confirm=<directory name>.");
	}

	private static List<string> ListExportedFiles(FileSystem preview, string root)
	{
		if (!preview.Directory.Exists(root))
		{
			return [];
		}

		List<string> files = [];
		foreach (string path in preview.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
		{
			files.Add(preview.Path.GetRelativePath(root, path).Replace('\\', '/'));
		}

		files.Sort(StringComparer.Ordinal);
		return files;
	}

	private const string ManifestFileName = ".assetripper-export.json";
	private const string UnityProjectKind = "unity-project";

	private static List<string> NormalizeInputs(IEnumerable<string> inputs, FileSystem fileSystem)
	{
		return [.. inputs
			.Select(input => fileSystem.Path.GetFullPath(input))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(input => input, StringComparer.Ordinal)];
	}

	private static void ExportKind(string kind, ExportHandler handler, FullConfiguration settings, GameData gameData, string destination, FileSystem fileSystem)
	{
		if (kind == UnityProjectKind)
		{
			handler.Export(gameData, destination, fileSystem);
			return;
		}

		settings.ExportRootPath = destination;
		PrimaryContentExporter.CreateDefault(gameData, settings).Export(gameData.GameBundle, settings, fileSystem);
	}

	private static bool TryGetExistingExport(string output, IReadOnlyList<string> inputs, string kind, FileSystem fileSystem)
	{
		string path = fileSystem.Path.Join(output, ManifestFileName);
		if (!fileSystem.File.Exists(path))
		{
			return false;
		}

		try
		{
			ExportManifest? manifest = JsonSerializer.Deserialize(fileSystem.File.ReadAllText(path), CliJsonContext.Default.ExportManifest);
			if (manifest is null || manifest.Kind != kind || manifest.Inputs is null)
			{
				return false;
			}

			return NormalizeInputs(inputs, fileSystem).SequenceEqual(NormalizeInputs(manifest.Inputs, fileSystem), StringComparer.Ordinal);
		}
		catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static void WriteManifest(string output, IReadOnlyList<string> inputs, string kind, FileSystem fileSystem)
	{
		ExportManifest manifest = new()
		{
			Kind = kind,
			Inputs = NormalizeInputs(inputs, fileSystem),
		};
		string path = fileSystem.Path.Join(output, ManifestFileName);
		fileSystem.File.WriteAllText(path, JsonSerializer.Serialize(manifest, CliJsonContext.Default.ExportManifest));
	}

	private static void WriteResult(TextWriter stdout, bool jsonMode, string kind, string status, string output, IReadOnlyList<string>? files = null, bool existing = false)
	{
		string? project = kind == UnityProjectKind ? Path.Combine(output, "ExportedProject") : null;
		if (jsonMode)
		{
			ExportPayload payload = new() { Status = status, Output = output, Project = project, Files = files, Existing = existing ? true : null };
			stdout.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.ExportPayload));
		}
		else
		{
			string label = kind == UnityProjectKind ? "Unity Project Export" : "Primary Content Export";
			string target = project ?? output;
			stdout.WriteLine(existing
				? $"{label} {status} (existing): {target}"
				: $"{label} {status}: {target}");
			if (files is not null)
			{
				foreach (string file in files)
				{
					stdout.WriteLine(file);
				}
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
