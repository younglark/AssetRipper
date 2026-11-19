namespace AssetRipper.CLI;

internal static class CliSync
{
	/// <summary>
	/// Serializes load and export in-process because <see cref="AssetRipper.Import.Logging.Logger"/> is process-wide.
	/// </summary>
	internal static readonly object LoadLock = new();
}
