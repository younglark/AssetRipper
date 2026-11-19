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
				using CancellationTokenSource timeout = new(idle);
				using Socket connection = await listener.AcceptAsync(timeout.Token);
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
					SessionResponse response = request.Command == "inspect"
						? Inspect(request, state, loading) : new() { Session = state };
					await writer.WriteLineAsync(JsonSerializer.Serialize(response, CliJsonContext.Default.SessionResponse));
					if (request.Command == "close")
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

	private static string GetCommand(string[] args) => args.First(arg => arg is "get" or "list" or "search");

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
