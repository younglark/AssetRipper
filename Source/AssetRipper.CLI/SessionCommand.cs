using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;

namespace AssetRipper.CLI;

internal sealed class SessionPayload
{
	public required string Id { get; init; }
	public required string Status { get; init; }
	public string? Message { get; init; }
	public string? Hint { get; init; }
}

internal sealed class SessionRequest
{
	public required string Command { get; init; }
	public string[] Args { get; init; } = [];
	public bool JsonMode { get; init; }
}

internal sealed class SessionResponse
{
	public SessionPayload? Session { get; init; }
	public int ExitCode { get; init; }
	public string Stdout { get; init; } = "";
	public string Stderr { get; init; } = "";
}

internal static class SessionCommand
{
	public static int Run(string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, Func<string, string?> environment)
	{
		try
		{
			List<string> positionals = [];
			List<string> inputs = [];
			double? timeout = null;
			bool noWait = false;
			bool wait = false;
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				int separator = arg.IndexOf('=');
				string flag = separator < 0 ? arg : arg[..separator];
				if (flag is "--input" or "--timeout")
				{
					string? value = separator >= 0 ? arg[(separator + 1)..] : i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : null;
					if (string.IsNullOrWhiteSpace(value))
					{
						throw new ArgumentException($"Pass a value for {flag}.");
					}

					if (flag == "--input")
					{
						inputs.Add(Path.GetFullPath(value));
					}
					else if (timeout is not null || !double.TryParse(value, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds < 0)
					{
						throw new ArgumentException("Pass --timeout once with a non-negative number of seconds.");
					}
					else
					{
						timeout = seconds;
					}
				}
				else if (arg == "--no-wait")
				{
					noWait = true;
				}
				else if (arg == "--wait")
				{
					wait = true;
				}
				else if (arg != "--json")
				{
					positionals.Add(arg);
				}
			}
			if (positionals.Count < 2)
			{
				throw new ArgumentException("Pass session start, session status <id>, or session close <id>.");
			}

			string action = positionals[1];
			if (action == "start")
			{
				if (positionals.Count != 2 || inputs.Count == 0 || (wait && noWait))
				{
					throw new ArgumentException("Use session start --input <path>... [--timeout <seconds> | --no-wait].");
				}

				string id = Guid.NewGuid().ToString("N");
				using Process process = StartHost(id, inputs, environment);
				Stopwatch startup = Stopwatch.StartNew();
				SessionResponse? response;
				do
				{
					response = Send(id, new() { Command = "status" });
					if (response is not null)
					{
						break;
					}

					if (process.HasExited || startup.Elapsed > TimeSpan.FromSeconds(15))
					{
						throw new IOException("The Session could not start.");
					}

					Thread.Sleep(25);
				} while (true);
				Stopwatch waiting = Stopwatch.StartNew();
				while (response.Session!.Status == "loading" && !noWait && (timeout is null || waiting.Elapsed.TotalSeconds < timeout))
				{
					Thread.Sleep(25);
					response = Send(id, new() { Command = "status" }) ?? throw new IOException("The Session ended while loading.");
				}
				WriteSession(stdout, jsonMode, response.Session);
				if (response.Session.Status == "failed")
				{
					return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "session_failed", response.Session.Message ?? "Session loading failed.", "Run assetripper session start again with readable inputs.");
				}

				return ExitCode.Success;
			}
			if (positionals.Count != 3 || inputs.Count != 0 || timeout is not null || noWait || wait)
			{
				throw new ArgumentException("Use session status <id> or session close <id>; loading options are for session start only.");
			}

			SessionResponse? result = Send(positionals[2], new() { Command = action });
			if (result is null)
			{
				return NotFound(stderr, jsonMode);
			}

			WriteSession(stdout, jsonMode, result.Session!);
			return result.ExitCode;
		}
		catch (ArgumentException exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.Usage, "usage", exception.Message, "Run assetripper session --help. " + exception.Message);
		}
		catch (Exception exception)
		{
			return CliOutput.WriteError(stderr, jsonMode, ExitCode.RuntimeError, "session_failed", exception.Message, "Run assetripper session start again.");
		}
	}

	private static Process StartHost(string id, List<string> inputs, Func<string, string?> environment)
	{
		string assembly = Path.Combine(AppContext.BaseDirectory, "assetripper.dll");
		ProcessStartInfo start;
		if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "assetripper" || !File.Exists(assembly))
		{
			start = new(Environment.ProcessPath!);
		}
		else
		{
			string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
				?? (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? Environment.ProcessPath!
					: Environment.GetEnvironmentVariable("DOTNET_ROOT") is string root ? Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet") : "dotnet");
			start = new(dotnet);
			start.ArgumentList.Add("exec");
			string runtimeConfig = Path.ChangeExtension(assembly, ".runtimeconfig.json");
			start.ArgumentList.Add("--runtimeconfig");
			start.ArgumentList.Add(runtimeConfig);
			start.ArgumentList.Add(assembly);
		}
		start.UseShellExecute = false;
		start.RedirectStandardInput = true;
		start.RedirectStandardOutput = true;
		start.RedirectStandardError = true;
		start.CreateNoWindow = true;
		start.ArgumentList.Add("--session-host");
		start.ArgumentList.Add(id);
		foreach (string input in inputs)
		{
			start.ArgumentList.Add(input);
		}

		foreach (string name in new[] { "ASSETRIPPER_TEST_SESSION_IDLE_MS", "ASSETRIPPER_TEST_SESSION_LOAD_DELAY_MS" })
		{
			start.Environment.Remove(name);
			if (environment(name) is string value)
			{
				start.Environment[name] = value;
			}
		}
		return Process.Start(start) ?? throw new IOException("The Session process could not start.");
	}

	internal static int Query(string id, string[] args, TextWriter stdout, TextWriter stderr, bool jsonMode, bool binaryOutput = false)
	{
		// Relative output paths belong to this invocation, not the Session's original working directory.
		string[] forwarded = (string[])args.Clone();
		for (int i = 0; i < forwarded.Length; i++)
		{
			if (forwarded[i] == "--output")
			{
				i++;
				forwarded[i] = Path.GetFullPath(forwarded[i]);
			}
			else if (forwarded[i].StartsWith("--output=", StringComparison.Ordinal))
			{
				forwarded[i] = "--output=" + Path.GetFullPath(forwarded[i][9..]);
			}
		}
		SessionResponse? result = Send(id, new() { Command = "inspect", Args = forwarded, JsonMode = jsonMode });
		if (result is null)
		{
			return NotFound(stderr, jsonMode);
		}

		if (binaryOutput && ReferenceEquals(stdout, Console.Out))
		{
			Stream stream = Console.OpenStandardOutput();
			stream.Write(System.Text.Encoding.Latin1.GetBytes(result.Stdout));
			stream.Flush();
		}
		else
		{
			stdout.Write(result.Stdout);
		}

		stderr.Write(result.Stderr);
		return result.ExitCode;
	}

	internal static SessionResponse? Send(string id, SessionRequest request)
	{
		if (!Guid.TryParseExact(id, "N", out _))
		{
			return null;
		}

		try
		{
			using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(250));
			socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath(id)), timeout.Token).AsTask().GetAwaiter().GetResult();
			using NetworkStream pipe = new(socket);
			using StreamWriter writer = new(pipe, leaveOpen: true) { AutoFlush = true };
			using StreamReader reader = new(pipe, leaveOpen: true);
			writer.WriteLine(JsonSerializer.Serialize(request, CliJsonContext.Default.SessionRequest));
			string? line = reader.ReadLine();
			return line is null ? null : JsonSerializer.Deserialize(line, CliJsonContext.Default.SessionResponse);
		}
		catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal static string SocketPath(string id) => Path.Combine(Path.GetTempPath(), "assetripper-" + id + ".sock");

	internal static int NotFound(TextWriter stderr, bool jsonMode) => CliOutput.WriteError(stderr, jsonMode, ExitCode.NotFound, "not_found", "The Session was not found or has ended.", "Run assetripper session start to load the inputs again.");

	private static void WriteSession(TextWriter stdout, bool jsonMode, SessionPayload payload)
	{
		if (jsonMode)
		{
			stdout.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.SessionPayload));
		}
		else
		{
			stdout.WriteLine($"Session {payload.Id}: {payload.Status}" + (payload.Message is null ? "" : $" ({payload.Message})"));
		}
	}
}
