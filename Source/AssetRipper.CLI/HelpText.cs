namespace AssetRipper.CLI;

internal static class HelpText
{
	internal const string ExitCodes = """
EXIT CODES
  0  Success, including an already-completed export and a Job that is still running but now queryable
  2  Usage error
  5  Not found: input path, Session, Job, or Asset Reference
  8  Retryable error; only used when it is safe to retry the same command
  1  Other runtime failure
""";

	internal const string Root = """
Usage: assetripper <command> [options]

Export Unity game files and inspect loaded assets. Intended for both interactive use and non-interactive agents.

COMMANDS
  export unity-project        Export loaded assets as a Unity project
  export primary-content      Export loaded assets as their primary decoded files
  list <kind>                 List loaded assets, bundles, scenes, collections, resources, or failed-files
  get asset <ref>             Retrieve an asset by Asset Reference
  search <query>              Search loaded assets by name
  session start|status|close  Manage a Session of loaded game files
  job status|wait|cancel      Query or control a long-running export Job

OPTIONS
  --json      Write result data as JSON even when stdout is a TTY
  --version   Show the version
  --help      Show this help

EXAMPLES
  assetripper export unity-project --input ./Game --output ./ExportedProject
  assetripper export primary-content --input ./Game --output ./Content --dry-run
  assetripper list assets --input ./Game --name "*.png"
  assetripper get asset <ref> --session <id> --format json
  assetripper session start --input ./Game
  assetripper job wait <id>


""" + ExitCodes;

	internal const string Export = """
Usage: assetripper export unity-project|primary-content --output <dir> [options]

Export loaded assets as a Unity project or as their primary decoded files.

This build supports synchronous unity-project and primary-content with repeatable --input and required --output.
--dry-run previews the files that would be written without changing the disk, including when the output directory is not empty.
A non-empty output directory is refused unless it is an Existing export of the same inputs, or --confirm=<directory name> is passed. A second identical export reports existing: true and does not write again.
Export with --session, --timeout, and --no-wait is not implemented in this build.
The remaining options below describe the planned CLI contract.

OPTIONS
  --input <path>       Game file or directory to load. Repeatable. Mutually exclusive with --session.
  --session <id>       Use a previously started Session. Mutually exclusive with --input.
  --output <dir>       Destination directory (required)
  --dry-run            Preview the files that would be written without changing the disk
  --confirm <name>     Confirm writing into a non-empty directory; <name> must be the directory name
  --timeout <seconds>  Stop waiting after this many seconds and return a queryable Job
  --no-wait            Return a queryable Job immediately
  --json               Write result data as JSON even when stdout is a TTY
  --help               Show this help

EXAMPLES
  assetripper export unity-project --input ./Game --output ./ExportedProject
  assetripper export primary-content --input ./Game --output ./Content --dry-run
  assetripper export unity-project --session <id> --output ./ExportedProject --timeout 60


""" + ExitCodes;

	internal const string List = """
Usage: assetripper list assets|bundles|scenes|collections|resources|failed-files [options]

List loaded assets or related items. Defaults to --limit 100.

OPTIONS
  --input <path>    Game file or directory to load. Repeatable. Mutually exclusive with --session.
  --session <id>    Use a previously started Session. Mutually exclusive with --input.
  --limit <n>       Maximum number of items to return (default 100)
  --cursor <token>  Continue a truncated list using the previous next token
  --name <glob>     Filter by name glob
  --type <type>     Filter by asset type (list assets only)
  --json            Write result data as JSON even when stdout is a TTY
  --help            Show this help

EXAMPLES
  assetripper list assets --input ./Game --name "*.png"
  assetripper list scenes --session <id>
  assetripper list assets --input ./Game --cursor <token>


""" + ExitCodes;

	internal const string Get = """
Usage: assetripper get asset <ref> [options]

Retrieve an asset by the Asset Reference returned by list.

OPTIONS
  --input <path>     Game file or directory to load. Repeatable. Mutually exclusive with --session.
  --session <id>     Use a previously started Session. Mutually exclusive with --input.
  --format <format>  Output format: json, yaml, text, binary, image, audio, model, font, or video (default json)
  --output <file>    Write the payload to this file instead of stdout
  --confirm <name>   Confirm overwriting an existing file; <name> must be the file name
  --json             Write metadata as JSON even when stdout is a TTY
  --help             Show this help

EXAMPLES
  assetripper get asset <ref> --input ./Game --format json
  assetripper get asset <ref> --session <id> --output ./asset.png --format image


""" + ExitCodes;

	internal const string Search = """
Usage: assetripper search <query> [options]

Search loaded assets by a case-insensitive name substring. Defaults to --limit 100.

OPTIONS
  --input <path>    Game file or directory to load. Repeatable. Mutually exclusive with --session.
  --session <id>    Use a previously started Session. Mutually exclusive with --input.
  --limit <n>       Maximum number of items to return (default 100)
  --cursor <token>  Continue a truncated search using the previous next token
  --json            Write result data as JSON even when stdout is a TTY
  --help            Show this help

EXAMPLES
  assetripper search Player --input ./Game
  assetripper search texture --session <id> --limit 20


""" + ExitCodes;

	internal const string Session = """
Usage: assetripper session start --input <path>... [options]
       assetripper session status <id>
       assetripper session close <id>

A Session is a loaded set of game files that later commands can reuse.
start waits for ready or failed by default. --no-wait or --timeout returns the id
and current status without stopping the load. status reports loading, ready, or failed.
Inspect a ready Session with --session <id> instead of --input. Inspecting loading
exits 8; inspecting failed exits 1. Use session status to wait, or start again after failure.
Sessions are local only, hold data in memory, and end after 30 minutes idle or close.
An ended Session (including after a process or machine restart) exits 5.

OPTIONS
  --input <path>       Game file or directory to load (start only). Repeatable.
  --timeout <seconds>  Stop waiting for the load to finish after this many seconds
  --no-wait            Return the Session id without waiting for the load to finish
  --wait               Wait for ready or failed (default); cannot be combined with --no-wait
  --json               Write result data as JSON even when stdout is a TTY
  --help               Show this help

EXAMPLES
  assetripper session start --input ./Game
  assetripper session start --input ./Game --no-wait
  assetripper session start --input ./Game --timeout 10
  assetripper list assets --session <id>
  assetripper session status <id>
  assetripper session close <id>


""" + ExitCodes;

	internal const string Job = """
Usage: assetripper job status <id>
       assetripper job wait <id> [options]
       assetripper job cancel <id>

A Job is a long-running export that can be queried until it finishes.

OPTIONS
  --timeout <seconds>  Stop waiting after this many seconds and leave the Job queryable (wait only)
  --json               Write result data as JSON even when stdout is a TTY
  --help               Show this help

EXAMPLES
  assetripper job status <id>
  assetripper job wait <id> --timeout 60
  assetripper job cancel <id>


""" + ExitCodes;

	public static string ForCommand(string? command)
	{
		return command switch
		{
			"export" => Export,
			"list" => List,
			"get" => Get,
			"search" => Search,
			"session" => Session,
			"job" => Job,
			_ => Root,
		};
	}
}
