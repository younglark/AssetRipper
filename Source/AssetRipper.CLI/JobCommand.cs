using AssetRipper.IO.Files;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class JobCommand
{
	public static int Export(UnityExportArguments options, string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, Func<string, string?> environment)
	{
		string? owner = options.Session;
		bool oneShot = owner is null;
		if (oneShot)
		{
			if (UnityProjectExport.CheckProtectedDestination(options.Output, stderr, jsonMode) is int protectedError) return protectedError;
			if (UnityProjectExport.CheckInputs(options.Inputs, stderr, jsonMode, LocalFileSystem.Instance) is int inputError) return inputError;
			if (UnityProjectExport.CheckDestination(options, stdout, stderr, jsonMode, LocalFileSystem.Instance) is int checkedResult) return checkedResult;
			owner = Guid.NewGuid().ToString("N");
			using Process process = SessionCommand.StartHost(owner, [.. options.Inputs.Select(Path.GetFullPath)], environment);
			Stopwatch startup = Stopwatch.StartNew();
			while (SessionCommand.Send(owner, new() { Command = "status" }) is null)
			{
				if (process.HasExited || startup.Elapsed.TotalSeconds > 15) throw new IOException("The export owner could not start.");
				Thread.Sleep(25);
			}
		}
		// Paths belong to this caller, not the Session's original working directory.
		string[] forwarded = (string[])args.Clone();
		for (int i = 0; i < forwarded.Length; i++)
		{
			if (forwarded[i] == "--output") forwarded[++i] = Path.GetFullPath(forwarded[i]);
			else if (forwarded[i].StartsWith("--output=", StringComparison.Ordinal)) forwarded[i] = "--output=" + Path.GetFullPath(forwarded[i][9..]);
		}
		SessionResponse? response = SessionCommand.Send(owner!, new() { Command = oneShot ? "export-one-shot" : "export", Args = forwarded, JsonMode = jsonMode });
		if (response is null) return SessionCommand.NotFound(stderr, jsonMode);
		if (response.Job is null) return Write(response, stdout, stderr, jsonMode);
		return Follow(response, options.NoWait ? 0 : options.Timeout, stdout, stderr, jsonMode);
	}

	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode)
	{
		try
		{
			List<string> positionals = [];
			double? timeout = null;
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				if (arg == "--timeout" || arg.StartsWith("--timeout=", StringComparison.Ordinal))
				{
					string? value = arg.StartsWith("--timeout=", StringComparison.Ordinal) ? arg[10..] : i + 1 < args.Length ? args[++i] : null;
					if (timeout is not null || !double.TryParse(value, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds < 0)
						throw new ArgumentException("Pass --timeout once with a non-negative number of seconds.");
					timeout = seconds;
				}
				else if (arg != "--json") positionals.Add(arg);
			}
			if (positionals.Count == 2) return Missing(stderr, jsonMode);
			if (positionals.Count != 3 || (timeout is not null && positionals[1] != "wait"))
				throw new ArgumentException("Use job status <id>, job wait <id> [--timeout <seconds>], or job cancel <id>.");
			SessionResponse? response = Send(positionals[2], positionals[1], jsonMode);
			if (response is null) return Missing(stderr, jsonMode);
			return positionals[1] is "wait" or "cancel"
				? Follow(response, timeout, stdout, stderr, jsonMode) : Write(response, stdout, stderr, jsonMode);
		}
		catch (ArgumentException exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", exception.Message, "Run assetripper job --help.");
		}
		catch (Exception exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "job_failed", exception.Message, "Run assetripper job status <id> before starting another export.");
		}
	}

	private static int Follow(SessionResponse response, double? timeout, TextWriter stdout, TextWriter stderr, bool jsonMode)
	{
		Stopwatch waiting = Stopwatch.StartNew();
		while (response.Job?.Status == "running" && (timeout is null || waiting.Elapsed.TotalSeconds < timeout))
		{
			Thread.Sleep(25);
			response = Send(response.Job.Id!, "status", jsonMode) ?? throw new IOException("The Job owner has ended.");
		}
		return Write(response, stdout, stderr, jsonMode);
	}

	private static SessionResponse? Send(string id, string action, bool jsonMode)
	{
		if (id.Length != 64 || !Guid.TryParseExact(id[..32], "N", out _) || !Guid.TryParseExact(id[32..], "N", out _)) return null;
		return SessionCommand.Send(id[..32], new() { Command = "job-" + action, Args = [id], JsonMode = jsonMode });
	}

	private static int Write(SessionResponse response, TextWriter stdout, TextWriter stderr, bool jsonMode)
	{
		if (response.Job is { } job)
		{
			if (jsonMode) stdout.WriteLine(JsonSerializer.Serialize(job, CliJsonContext.Default.ExportPayload));
			else stdout.WriteLine($"Job {job.Id}: {job.Status}. {job.Hint}");
		}
		else stdout.Write(response.Stdout);
		if (jsonMode) stderr.Write(response.Stderr);
		else
		{
			using StringReader diagnostics = new(response.Stderr);
			while (diagnostics.ReadLine() is string line)
			{
				if (line.StartsWith('{'))
				{
					try
					{
						ErrorPayload? error = JsonSerializer.Deserialize(line, CliJsonContext.Default.ErrorPayload);
						if (error is not null)
						{
							CliOutput.WriteError(stderr, false, response.ExitCode, error.Code, error.Message ?? error.Code, error.Hint);
							continue;
						}
					}
					catch (JsonException) { /* A progress line may not be an error object. */ }
				}
				stderr.WriteLine(line);
			}
		}
		return response.ExitCode;
	}

	private static int Missing(TextWriter stderr, bool jsonMode) => CliOutput.WriteError(stderr, jsonMode, ExitCode.NotFound, "not_found", "The Job was not found or has ended.", "Check the Job id returned by export.");
}
