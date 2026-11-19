using AssetRipper.IO.Files;
using AssetRipper.Processing;
using System.Text.Json;

namespace AssetRipper.CLI;

/// <summary>A single export's result and cooperative cancellation, owned by a local Session.</summary>
internal sealed class ExportJob
{
	private readonly object gate = new();
	private readonly CancellationTokenSource cancellation = new();
	private SessionResponse response;

	public ExportJob(string owner, UnityExportArguments options, Task<GameData> loading)
	{
		Id = owner + Guid.NewGuid().ToString("N");
		Started = new() { Job = new() { Id = Id, Status = "running", Output = options.Output, Hint = $"Run assetripper job wait {Id}." } };
		response = Started;
		Completion = Task.Run(async () =>
		{
			using StringWriter stdout = new();
			using StringWriter stderr = new();
			SessionResponse completed;
			GameData? data = null;
			try
			{
				data = await loading.WaitAsync(cancellation.Token);
				if (int.TryParse(Environment.GetEnvironmentVariable("ASSETRIPPER_TEST_EXPORT_DELAY_MS"), out int delay) && delay > 0)
					await Task.Delay(delay, cancellation.Token);
				int exit = UnityProjectExport.Execute(options, stdout, stderr, true, LocalFileSystem.Instance, data, cancellation.Token);
				if (exit != 0) cancellation.Token.ThrowIfCancellationRequested();
				ExportPayload result = exit == 0
					? JsonSerializer.Deserialize(stdout.ToString(), CliJsonContext.Default.ExportPayload)!
					: new() { Status = "failed", Output = options.Output };
				result.Id = Id;
				completed = new() { Job = result, ExitCode = exit, Stderr = stderr.ToString() };
			}
			catch (OperationCanceledException)
			{
				completed = new() { Job = new() { Id = Id, Status = "cancelled", Output = options.Output } };
			}
			catch (Exception exception)
			{
				int exit = CliOutput.WriteError(stderr, true, ExitCode.RuntimeError, "export_failed", exception.GetBaseException().Message, "Check the inputs and choose a writable --output directory.");
				completed = new() { Job = new() { Id = Id, Status = "failed", Output = options.Output }, ExitCode = exit, Stderr = stderr.ToString() };
			}
			finally
			{
				// Completed Jobs retain only their result, not the loaded game's object graph.
				data = null;
				loading = null!;
			}
			lock (gate)
			{
				response = completed;
				cancellation.Dispose();
			}
		});
	}

	public SessionResponse Started { get; }
	public Task Completion { get; }
	public string Id { get; }
	public bool IsRunning => Snapshot().Job!.Status == "running";
	public SessionResponse Snapshot()
	{
		lock (gate) return response;
	}
	public void Cancel()
	{
		lock (gate)
		{
			if (response.Job!.Status == "running") cancellation.Cancel();
		}
	}
}
