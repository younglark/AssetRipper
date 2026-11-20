using AssetRipper.Assets;
using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.Text;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class GetAssetCommand
{
	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		try
		{
			GetAssetArguments options = GetAssetArguments.Parse(args);
			if (options.Session is not null)
			{
				bool binaryOutput = options.Output is null && options.Format is not ("text" or "json" or "yaml");
				return SessionCommand.Query(options.Session, args, stdout, stderr, jsonMode, binaryOutput);
			}
			return Execute(options, stdout, stderr, jsonMode, fileSystem);
		}
		catch (ArgumentException exception)
		{
			string hint = exception.Message.Contains("--format", StringComparison.Ordinal)
				? $"Legal --format values are {GetAssetArguments.LegalFormatList}. Run assetripper get --help."
				: "Run assetripper get --help. " + exception.Message;
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", exception.Message, hint);
		}
		catch (Exception exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "get_failed", exception.Message, "Check the inputs and Asset Reference and try again.");
		}
	}

	private static int Execute(GetAssetArguments options, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		if (!string.IsNullOrEmpty(options.Output) && fileSystem.File.Exists(options.Output) && !ConfirmMatches(options.Output, options.Confirm, fileSystem))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "output_exists", "The output file already exists.", "Pass --confirm=<file name> to overwrite it, or choose a different --output path.");
		}

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
					return ExecuteLoaded(options, gameData, stdout, stderr, jsonMode, fileSystem);
				}
				finally
				{
					gameData.AssemblyManager.Dispose();
				}
			}
			catch (Exception exception)
			{
				return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "get_failed", exception.Message, "Check the inputs and Asset Reference and try again.");
			}
			finally
			{
				Logger.Remove(logger);
			}
		}
	}

	internal static int ExecuteLoaded(GetAssetArguments options, GameData gameData, TextWriter stdout, TextWriter stderr, bool jsonMode, FileSystem fileSystem)
	{
		if (!string.IsNullOrEmpty(options.Output) && fileSystem.File.Exists(options.Output) && !ConfirmMatches(options.Output, options.Confirm, fileSystem))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "output_exists", "The output file already exists.", "Pass --confirm=<file name> to overwrite it, or choose a different --output path.");
		}
		IUnityObjectBase? asset = FindAsset(gameData, options.AssetReference!);
		if (asset is null)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.NotFound, "not_found", $"Asset Reference '{options.AssetReference}' was not found.", "Run assetripper list assets to get a current Asset Reference.");
		}

		IReadOnlyList<string> supported = GetAssetFormat.SupportedFormats(asset);
		if (!supported.Contains(options.Format))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "unsupported_format", $"The '{options.Format}' format is not supported for this asset.", $"This asset supports: {string.Join(", ", supported)}.");
		}

		if (!GetAssetFormat.TryEncode(asset, options.Format, gameData, out byte[]? payload))
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "export_failed", $"The '{options.Format}' payload could not be produced for this asset.", $"This asset supports: {string.Join(", ", supported)}.");
		}

		return WritePayload(options, payload, stdout, jsonMode, fileSystem);
	}

	private static IUnityObjectBase? FindAsset(GameData gameData, string assetReference)
	{
		return AssetReferences.For(gameData.GameBundle).Find(assetReference);
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

	private static int WritePayload(GetAssetArguments options, byte[] payload, TextWriter stdout, bool jsonMode, FileSystem fileSystem)
	{
		if (string.IsNullOrEmpty(options.Output))
		{
			WriteToStdout(stdout, payload, options.Format);
			return ExitCode.Success;
		}

		string output = options.Output;
		string? directory = fileSystem.Path.GetDirectoryName(output);
		if (!string.IsNullOrEmpty(directory))
		{
			fileSystem.Directory.Create(directory);
		}
		fileSystem.File.WriteAllBytes(output, payload);

		if (jsonMode)
		{
			GetAssetPayload metadata = new()
			{
				Path = output,
				Format = options.Format,
				Size = payload.Length,
			};
			stdout.WriteLine(JsonSerializer.Serialize(metadata, CliJsonContext.Default.GetAssetPayload));
		}
		else
		{
			stdout.WriteLine($"Wrote {options.Format} ({payload.Length} bytes) to {output}");
		}

		return ExitCode.Success;
	}

	private static void WriteToStdout(TextWriter stdout, byte[] payload, string format)
	{
		if (format is "json" or "yaml" or "text")
		{
			stdout.Write(Encoding.UTF8.GetString(payload));
			if (payload.Length == 0 || payload[^1] != (byte)'\n')
			{
				stdout.WriteLine();
			}
			return;
		}

		if (ReferenceEquals(stdout, Console.Out))
		{
			Stream stream = Console.OpenStandardOutput();
			stream.Write(payload);
			stream.Flush();
			return;
		}

		stdout.Write(Encoding.Latin1.GetString(payload));
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
