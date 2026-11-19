namespace AssetRipper.CLI;

internal static class ExitCode
{
	public const int Success = 0;
	public const int RuntimeError = 1;
	public const int Usage = 2;
	public const int NotFound = 5;
	public const int Retryable = 8;
}
