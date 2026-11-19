using System.Reflection;
using System.Text.Json;

namespace AssetRipper.CLI;

internal static class CliOutput
{
	public static bool IsJsonMode(bool stdoutIsTty, bool jsonFlag)
	{
		return jsonFlag || !stdoutIsTty;
	}

	public static int WriteError(TextWriter stderr, bool jsonMode, int exitCode, string code, string message, string hint)
	{
		if (jsonMode)
		{
			ErrorPayload payload = new()
			{
				Code = code,
				Hint = hint,
				Message = message,
			};
			stderr.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.ErrorPayload));
		}
		else
		{
			stderr.WriteLine(message);
			stderr.WriteLine(hint);
		}

		return exitCode;
	}

	public static void WriteVersion(TextWriter stdout, bool jsonMode)
	{
		string version = GetVersion();
		if (jsonMode)
		{
			VersionPayload payload = new() { Version = version };
			stdout.WriteLine(JsonSerializer.Serialize(payload, CliJsonContext.Default.VersionPayload));
		}
		else
		{
			stdout.WriteLine($"assetripper {version}");
		}
	}

	public static string GetVersion()
	{
		Assembly assembly = typeof(Program).Assembly;
		string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if (!string.IsNullOrEmpty(informational))
		{
			int plus = informational.IndexOf('+');
			return plus >= 0 ? informational[..plus] : informational;
		}

		Version? version = assembly.GetName().Version;
		return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
	}
}
