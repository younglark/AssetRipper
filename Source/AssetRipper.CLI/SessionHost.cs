using AssetRipper.Export.Configuration;
using AssetRipper.Export.UnityProjects;
using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.Net.Sockets;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class SessionHost
{
	public static int Run(string id, string[] inputs)
	{
		if (!Guid.TryParseExact(id, "N", out _) || inputs.Length == 0)
		{
			return ExitCode.Usage;
		}
		Console.SetOut(TextWriter.Null);
		Console.SetError(TextWriter.Null);
		return RunAsync(id, inputs).GetAwaiter().GetResult();
	}

	private static async Task<int> RunAsync(string id, string[] inputs)
	{
		Task<GameData> loading = Task.Run(async () =>
		{
			if (int.TryParse(Environment.GetEnvironmentVariable("ASSETRIPPER_TEST_SESSION_LOAD_DELAY_MS"), out int delay) && delay > 0)
			{
				await Task.Delay(delay);
			}

			foreach (string input in inputs)
			{
				if (!File.Exists(input) && !Directory.Exists(input))
				{
					throw new FileNotFoundException($"Input path '{input}' was not found.");
				}
			}
			FullConfiguration settings = new();
			settings.LoadFromDefaultPath();
			return new ExportHandler(settings).LoadAndProcess(inputs, LocalFileSystem.Instance);
		});
		TimeSpan idle = TimeSpan.FromMinutes(30);
		if (int.TryParse(Environment.GetEnvironmentVariable("ASSETRIPPER_TEST_SESSION_IDLE_MS"), out int idleMs) && idleMs > 0)
		{
			idle = TimeSpan.FromMilliseconds(idleMs);
		}

		Dictionary<string, ExportJob> jobs = [];
		ExportJob? active = null;
		string socketPath = SessionCommand.SocketPath(id);
		using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		listener.Bind(new UnixDomainSocketEndPoint(socketPath));
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}

		listener.Listen(16);
		try
		{
			while (true)
			{
				Task? running = active?.IsRunning == true ? active.Completion : null;
				using CancellationTokenSource timeout = new(running is not null ? Timeout.InfiniteTimeSpan : idle);
				Task<Socket> accepting = listener.AcceptAsync(timeout.Token).AsTask();
				if (running is not null)
				{
					await Task.WhenAny(accepting, running);
					timeout.CancelAfter(idle);
				}
				using Socket connection = await accepting;
				using NetworkStream pipe = new(connection);
				try
				{
					using StreamReader reader = new(pipe, leaveOpen: true);
					using StreamWriter writer = new(pipe, leaveOpen: true) { AutoFlush = true };
					string? line = await reader.ReadLineAsync(timeout.Token);
					if (line is null)
					{
						continue;
					}

					SessionRequest request = JsonSerializer.Deserialize(line, CliJsonContext.Default.SessionRequest)!;
					string status = request.Command == "close" ? "closed" : loading.IsCompletedSuccessfully ? "ready" : loading.IsFaulted || loading.IsCanceled ? "failed" : "loading";
					SessionPayload state = new()
					{
						Id = id,
						Status = status,
						Hint = status == "loading" ? $"Run assetripper session status {id}." : status == "failed" ? "Run assetripper session start again with readable inputs." : null,
						Message = loading.Exception?.GetBaseException().Message,
					};
					SessionResponse response;
					if (request.Command.StartsWith("job-", StringComparison.Ordinal))
					{
						if (request.Args.Length != 1 || !jobs.TryGetValue(request.Args[0], out ExportJob? job))
							response = Error(request, ExitCode.NotFound, "not_found", "The Job was not found.", "Check the Job id returned by export.");
						else
						{
							if (request.Command == "job-cancel") job.Cancel();
							response = job.Snapshot();
						}
					}
					else if (active?.IsRunning == true && request.Command == "close")
						response = Busy(request, active);
					else if (request.Command is "export" or "export-one-shot")
					{
						if (active?.IsRunning == true) response = Busy(request, active);
						else if (request.Command == "export" && state.Status != "ready") response = Inspect(request, state, loading);
						else
						{
							UnityExportArguments options = UnityExportArguments.Parse(request.Args);
							options.Inputs.Clear();
							options.Inputs.AddRange(inputs);
							using StringWriter checkOut = new();
							using StringWriter checkError = new();
							if (UnityProjectExport.CheckDestination(options, checkOut, checkError, request.JsonMode, LocalFileSystem.Instance) is int checkResult)
								response = new() { ExitCode = checkResult, Stdout = checkOut.ToString(), Stderr = checkError.ToString() };
							else
							{
								active = new(id, options, loading);
								jobs.Add(active.Id, active);
								if (request.Command == "export-one-shot")
								{
									_ = ReleaseOneShotData(active.Completion, loading);
									// The Job now owns the load; keep only the small queryable results here.
									loading = Task.FromCanceled<GameData>(new CancellationToken(true));
								}
								response = active.Started;
							}
						}
					}
					else response = request.Command == "inspect" ? Inspect(request, state, loading) : new() { Session = state };
					await writer.WriteLineAsync(JsonSerializer.Serialize(response, CliJsonContext.Default.SessionResponse));
					if (request.Command == "close" && response.ExitCode == 0)
					{
						break;
					}
				}
				catch (IOException)
				{
					// A caller disappearing must not end the loaded Session.
				}
				catch (JsonException)
				{
					// Ignore an incomplete or invalid local request.
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Idle Sessions end without persisting loaded data.
		}
		finally
		{
			listener.Dispose();
			File.Delete(socketPath);
			if (loading.IsCompletedSuccessfully)
			{
				loading.Result.AssemblyManager.Dispose();
			}
		}
		return ExitCode.Success;
	}

	private static async Task ReleaseOneShotData(Task completion, Task<GameData> loading)
	{
		await completion;
		try
		{
			GameData data = await loading;
			data.AssemblyManager.Dispose();
		}
		catch (Exception)
		{
			// A failed load has no completed GameData to release.
		}
	}

	private static SessionResponse Busy(SessionRequest request, ExportJob job) => Error(request, ExitCode.Retryable, "session_busy", "An export Job is already running on this Session.", $"Run assetripper job wait {job.Id} or assetripper job cancel {job.Id} before retrying.");

	private static SessionResponse Error(SessionRequest request, int exit, string code, string message, string hint)
	{
		using StringWriter stderr = new();
		CliOutput.WriteError(stderr, request.JsonMode, exit, code, message, hint);
		return new() { ExitCode = exit, Stderr = stderr.ToString() };
	}

	private static string GetCommand(string[] args) => Program.GetCommandName(args) ?? "";

	private static SessionResponse Inspect(SessionRequest request, SessionPayload state, Task<GameData> loading)
	{
		using StringWriter stdout = new();
		using StringWriter stderr = new();
		int exit;
		if (state.Status == "loading")
		{
			exit = CliOutput.WriteError(stderr, request.JsonMode, ExitCode.Retryable, "session_loading", "The Session is still loading.", $"Run assetripper session status {state.Id} before retrying.");
		}
		else if (state.Status == "failed")
		{
			exit = CliOutput.WriteError(stderr, request.JsonMode, ExitCode.RuntimeError, "session_failed", state.Message ?? "Session loading failed.", "Run assetripper session start again with readable inputs.");
		}
		else
		{
			try
			{
				exit = GetCommand(request.Args) == "get"
					? GetAssetCommand.ExecuteLoaded(GetAssetArguments.Parse(request.Args), loading.Result, stdout, stderr, request.JsonMode, LocalFileSystem.Instance)
					: InspectCommand.ExecuteLoaded(InspectArguments.Parse(request.Args), loading.Result, stdout, request.JsonMode);
			}
			catch (ArgumentException exception)
			{
				exit = CliOutput.WriteError(stderr, request.JsonMode, ExitCode.Usage, "usage", exception.Message, "Run assetripper --help. " + exception.Message);
			}
			catch (Exception exception)
			{
				exit = CliOutput.WriteError(stderr, request.JsonMode, ExitCode.RuntimeError, "inspect_failed", exception.Message, "Check the Asset Reference and options and try again.");
			}
		}
		return new() { ExitCode = exit, Stdout = stdout.ToString(), Stderr = stderr.ToString() };
	}

}
