# Agent-oriented AssetRipper CLI

## Problem Statement

Agents, CI, and other non-interactive hosts cannot reliably drive AssetRipper today. The product is a web GUI with a few launch flags and HTTP endpoints for loading, exporting, and inspecting assets. There is no first-class command that an agent can call without starting a web server, with machine-readable output, stable exit codes, bounded lists, and no risk of hanging on a prompt or pager. Internal console tools in the repository are not this contract.

## Solution

Ship an `assetripper` command, as a new `AssetRipper.CLI` project in the existing solution, that agents can invoke directly. It calls the existing import, processing, and export libraries and does not go through the web GUI or its HTTP API. The GUI executable and its launch arguments stay unchanged.

A one-shot command loads its inputs, performs one action, and exits. A Session is a loaded set of game files that later commands can reuse without loading them again. A Job is a long-running export that can be queried until it finishes. When stdout is not a TTY, results default to JSON with stable fields. Export requires an explicit output directory, can be previewed, and is safe to retry.

## User Stories

1. As an agent, I want to run `assetripper` with no TTY, so that I am never left blocked on a prompt, pager, or interactive dialog.
2. As an agent, I want stdout to contain only data, so that I can parse the command result without stripping progress or warnings.
3. As an agent, I want progress, warnings, and errors on stderr, so that they cannot corrupt a pipe.
4. As an agent, I want JSON output by default when stdout is not a TTY, so that I can parse results without adding flags.
5. As a person in a terminal, I want concise human-readable text by default, so that I can read a result quickly.
6. As an agent, I want `--json` on every command that returns data, so that I can force JSON even when a TTY is attached.
7. As an agent, I want JSON errors on stderr to include a stable `code` and a `hint`, so that I know the legal next step.
8. As an agent, I want `--help` at the top level and on each subcommand to include examples and exit codes, so that I can correct a failed invocation.
9. As an agent, I want unknown subcommands and invalid flags to fail as usage errors, so that they are not treated as a default run.
10. As an agent, I want exit code `0` for success, including an already-completed export and a Job that is still running but now queryable, so that I do not retry a command that already started work.
11. As an agent, I want exit code `2` for usage errors, so that I can distinguish bad arguments from a failed load or export.
12. As an agent, I want exit code `5` when an input path, Session, Job, or Asset Reference cannot be found, so that I can recover or reload.
13. As an agent, I want exit code `8` only when it is safe to retry the same command, such as a busy Session or a transient I/O error, so that I do not duplicate an export.
14. As an agent, I want exit code `1` for other runtime failures, so that unexpected errors are still visible and distinct.
15. As an agent, I want `--input` to accept files or directories and to be repeatable, so that I can load the same inputs the GUI would load.
16. As an agent, I want commands that need loaded files to accept either `--input` or `--session`, but not both, so that the source of the loaded data is unambiguous.
17. As an agent, I want a clear usage error when both are missing or both are present, so that I can fix the command.
18. As an agent, I want `export unity-project --output <dir>`, so that I can perform a Unity Project Export without opening the GUI.
19. As an agent, I want `export primary-content --output <dir>`, so that I can perform a Primary Content Export without opening the GUI.
20. As an agent, I want export without `--output` to be a usage error that names the missing flag, so that I can correct it immediately.
21. As an agent, I want `--dry-run` on export, so that I can preview what would be written without changing the disk.
22. As an agent, I want a non-empty output directory to be rejected by default, with a hint to choose another directory, empty it, or pass `--confirm=<directory name>`, so that I do not overwrite unexpected files.
23. As an agent, I want `--confirm` to require the output directory's name, so that a generic yes flag cannot authorize a destructive write.
24. As an agent, I want `--dry-run` to still work when the output directory is non-empty, so that I can preview even when a write would be refused.
25. As an agent, I want a second export of the same inputs to the same output directory to exit `0` and report `existing: true`, so that a lost response is safe to retry.
26. As an agent, I want a non-empty directory that is not the result of that export to still be refused, so that I do not clobber unrelated files.
27. As an agent, I want well-known personal folders such as Desktop, Documents, and Downloads to be rejected as export targets, matching the GUI's existing safeguard, so that I cannot empty a user profile folder.
28. As an agent, I want `list assets`, `list bundles`, `list scenes`, `list collections`, `list resources`, and `list failed-files`, so that I can inspect what was loaded.
29. As an agent, I want `list` and `search` to default to `--limit 100`, so that a large game cannot flood my context.
30. As an agent, I want a truncated list to include `truncated`, `next`, and `hint` in JSON, so that I can fetch the rest on purpose.
31. As an agent, I want `--name` (glob) and `--type` (asset type) filters on `list`, so that I can narrow results without paging through everything.
32. As an agent, I want `list assets` to return an Asset Reference for each item, so that I can pass that same identifier to `get`.
33. As an agent, I want `get asset <ref> --format json|yaml|text|binary|image|audio|model|font|video`, so that I can retrieve an asset in the formats the product already supports.
34. As an agent, I want an invalid `--format` to be a usage error that lists the legal values, so that I can correct it.
35. As an agent, I want a missing Asset Reference to exit `5`, so that I know to list again rather than retry blindly.
36. As an agent, I want `get` to write the payload to stdout by default, so that I can pipe it to another command.
37. As an agent, I want `get --output <file>` to write the payload to that file, so that binary data does not have to pass through my context.
38. As an agent, I want `get --output --json` to print only metadata on stdout (path, format, size), so that raw bytes are never wrapped in JSON.
39. As an agent, I want `search <query>` against the loaded files, with the same limit and truncation rules as `list`, so that I can find assets by text.
40. As an agent, I want `session start --input <path...>` to return a Session id immediately, so that I still have a handle if I cannot wait for the load to finish.
41. As an agent, I want that Session's status to be `loading`, `ready`, or `failed`, so that I know when inspect and export commands are allowed.
42. As an agent, I want `session start` to wait until `ready` by default, so that a simple script can use the Session on the next line.
43. As an agent, I want `--timeout` or `--no-wait` on `session start` to still return the Session id, queryable with `session status`, so that a host timeout does not lose the load.
44. As an agent, I want load progress to live on the Session and not create a Job, so that I do not have to juggle two identifiers for one load.
45. As an agent, I want `session status` to tell me the current state of a Session, so that I can poll after `--no-wait` or a timeout.
46. As an agent, I want `session close` to end a Session and release its loaded data, so that memory does not stay held after I am done.
47. As an agent, I want an idle Session to end after 30 minutes, so that an abandoned load does not live forever.
48. As an agent, I want a Session that disappeared after a machine restart or idle timeout to exit `5`, so that I know I must start a new one.
49. As an agent, I want later commands to accept `--session <id>` instead of `--input`, so that I can list, search, get, and export without loading the game again.
50. As an agent, I want several Sessions to exist at the same time, so that I can work on more than one set of game files.
51. As an agent, I want a Session process to be local only, so that it is not reachable from the network.
52. As an agent, I want export to wait until it finishes by default, so that a simple invocation has a complete result.
53. As an agent, I want export progress to go to stderr only, with no spinner or color when there is no TTY, and to respect `NO_COLOR`.
54. As an agent, I want `--timeout <seconds>` on export to print a Job reference with `status: running`, a hint to run `job wait`, and exit `0`, so that I do not retry `export`.
55. As an agent, I want `--no-wait` on export to return that Job reference immediately, with the same exit code and hint.
56. As an agent, I want `job status`, `job wait`, and `job cancel`, so that I can follow or stop a long export.
57. As an agent, I want a one-shot export that returns a Job to keep enough state for that Job to finish or be cancelled, so that the work does not vanish when the original command exits.
58. As an agent, I want a second mutating operation on the same Session to fail with exit `8` and a hint naming the Job or operation to wait for, so that I can retry safely after it finishes.
59. As an agent, I want `list`, `get`, and `search` to work while an export Job is running, once the Session is `ready`, so that I can keep inspecting without waiting for the write to finish.
60. As an agent, I want inspect commands on a Session that is still `loading` or already `failed` to fail with a structured error and a hint, so that I know whether to wait or start again.
61. As a person who uses the GUI, I want the existing GUI executable and launch arguments left unchanged, so that interactive use is not affected by the new command.
62. As an agent, I want CLI messages and error codes in stable English, so that parsing does not depend on the host locale.
63. As an agent or developer, I want static documentation that lists commands, examples, exit codes, and stable JSON fields, so that I can write a skill or script without a schema command.
64. As a developer, I want the CLI to use the same default configuration the GUI would load, if present, otherwise built-in defaults, so that export quality matches the GUI while settings editing stays out of this version.
65. As an agent, I want the CLI not to write a log file into the working directory by default, so that a run does not leave unexpected files beside the command output.
66. As a developer, I want tests to drive only the `assetripper` command entry, so that we lock the contract agents will actually call.

## Implementation Decisions

- Add a new `AssetRipper.CLI` project to the existing solution. The command name is `assetripper`. The GUI project is not modified to accept these subcommands.
- The CLI calls the existing import, processing, and export libraries directly, including Unity Project Export and Primary Content Export through the same handler used by the GUI. It does not start the web application and does not call the HTTP API. See ADR 0001.
- Do not route CLI export through the GUI file-loader's headless path. That path currently treats a missing user as consent to delete a non-empty export directory. The CLI must refuse a non-empty output directory unless `--confirm=<directory name>` is supplied, or unless the directory is an Existing export of the same inputs.
- `--input` and `--session` are mutually exclusive. Commands that need loaded files require exactly one of them.
- Session and Job are different concepts. A Session is a local background process that holds a loaded set of game files. `session start` returns its id immediately; load status is `loading`, `ready`, or `failed` on that Session. A Job is only a long-running export or similar write. See ADR 0002.
- Session identifiers are positional on the session subcommands: `session status <id>` and `session close <id>`. Other commands take `--session <id>`. Job identifiers are positional: `job status <id>`, `job wait <id>`, `job cancel <id>`.
- Sessions listen only on the local machine. Idle Sessions exit after 30 minutes. `session close` exits immediately. After a process or machine restart, a previous Session id is not found (exit `5`).
- Several Sessions may exist at once. On a single Session, only one mutating operation may run at a time. A conflicting write returns exit `8` with a hint. After the Session is `ready`, `list`, `get`, and `search` may run during an export Job.
- One-shot export is load, export, then exit. If `--timeout` or `--no-wait` returns a Job, the process that owns that Job keeps the loaded data until the Job finishes, fails, or is cancelled.
- The output contract, exit codes, export confirmation, and idempotent Existing export follow ADR 0003. Exit codes are `0`, `2`, `5`, `8`, and `1`, and their meanings do not change after they are documented in `--help`.
- JSON field names are a stable contract. Error objects on stderr include `code` and `hint`. Truncated lists include `truncated`, `next`, and `hint`. A completed duplicate export includes `existing: true`. A deferred export includes `status: running` and a Job identifier.
- `get` writes the payload to stdout unless `--output` is set, in which case the file receives the payload and `--json` stdout is metadata only (path, format, size). Binary formats are never embedded in JSON.
- An Asset Reference is whatever `list assets` returns for that asset, and `get asset` accepts that same value. The identifier must be stable for the life of a Session, and for a one-shot command it must be stable for the life of that process.
- Export refuses an output path whose final segment is `Desktop`, `Documents`, or `Downloads`, compared case-insensitively, matching the GUI safeguard. That refusal is exit `2`.
- An Existing export is recognized by `.assetripper-export.json` in a successful output directory, recording enough of the inputs and export kind to decide that a later identical command can return `existing: true` without writing again.
- First-version configuration is read-only: load the same default configuration the GUI would load, or built-in defaults if none exists. There is no settings or configuration-file command in this version.
- The CLI does not create a log file by default. Diagnostics go to stderr. An explicit log-file flag is not required for this version.
- First-version documentation is static: examples, exit codes, commands, and JSON fields, in English. There is no `schema` subcommand and no MCP in this version.
- Command and flag names are English, consistent across the CLI, and are not prefix-abbreviated.

- `--confirm` accepts both `--confirm=<name>` and `--confirm <name>`. The value is the output directory name, or the output file name for `get --output`.
- A well-formed export to a non-empty directory that is not an Existing export of the same inputs, and that lacks a matching `--confirm`, exits `1` with a hint. It is not a usage error.
- `job cancel` leaves any partially written output in place. That directory is not an Existing export. A later export to the same non-empty directory follows the usual confirm rules.
- `list` and `search` continue a truncated page with `--cursor`, whose value is the opaque `next` token from the previous JSON result.
- `--name` (glob) applies to every `list` kind. `--type` applies only to `list assets`; using it on another `list` kind is exit `2`.
- `search <query>` in this version is a case-insensitive substring match against asset names. It is not a full query language.
- `get` defaults to `--format json`. If the requested format is not valid for that asset, the command exits `1` and the hint lists the formats that asset supports. If `--output` already exists, the write is refused unless `--confirm=<file name>` is supplied.
- Inspect commands (`list`, `get`, `search`) against a Session that is still `loading` exit `8` with a hint to run `session status`. Against a Session that is already `failed`, they exit `1` with a hint to run `session start` again.
- `job wait` accepts `--timeout`. On expiry it exits `0` with `status: running` and a hint to wait again.
- `session close` while an export Job is still running exits `8` and does not close the Session. The hint tells the agent to `job wait` or `job cancel` first.
- The idle Session timeout remains 30 minutes. Tests may shorten the wait only with an undocumented test-only environment variable; there is no public flag for this.
- Only JSON fields already named in this spec are the stable v1 contract: error `code` and `hint`; list `truncated`, `next`, and `hint`; `existing`; Job `status` and identifier; `get` metadata `path`, `format`, and `size`; Session status `loading`, `ready`, and `failed`. Later versions may add fields. Clients must not treat unlisted fields as required.
- When stdout is not a TTY, the CLI uses no spinner and no color. `NO_COLOR` is respected even when a TTY is attached.

## Testing Decisions

- Tests describe external behavior only: arguments in; exit code, stdout, stderr, and filesystem effects out. They do not assert internal types, process layouts, or how a Session is hosted.
- The single test seam is the `assetripper` command entry. Session, Job, `--dry-run`, `--confirm`, Existing export, list truncation, and `get` output rules are all exercised through that entry.
- Tests reuse the existing `FileSystem` / `VirtualFileSystem` abstraction so most cases do not depend on a real game install. Usage errors, `--help`, missing inputs, and refusal of a non-empty output directory can run without loading any assets. Paths that must actually load, list, get, or export use a small loadable fixture written into that filesystem.
- A new TUnit test project for the CLI should follow the style of the existing `AssetRipper.Tests` project, which already tests Unity Project Export against a `VirtualFileSystem`.
- Cover at least: no-TTY JSON default; TTY text default and `--json`; stdout/stderr split; each exit code; `--input` vs `--session` exclusivity; both export kinds with `--dry-run`; non-empty directory refusal and `--confirm`; Existing export returning `0`; list limit and truncation; `get` stdout vs `--output`; Session start/status/close and idle-not-found behavior at the command level; export `--timeout` / `--no-wait` returning a Job with exit `0`; `job wait` and `job cancel`; mutating-operation conflict returning `8`; no color/spinner when stdout is not a TTY and `NO_COLOR`; `--cursor` continuation; inspect on a `loading` or `failed` Session; `job wait --timeout`; `session close` during a running Job; `job cancel` leaving partial output.


## Out of Scope

- Changing the GUI executable, its launch arguments, or its HTTP API.
- A `schema` introspection command, an MCP server, or a `--agent` flag beyond the no-TTY behavior already specified.
- Settings or configuration-file commands, including writing or listing configuration files.
- Reusing the web GUI server as a Session, or building a full on-disk cache of processed games as a Session.
- A global `--yes` or `--allow-mutations` switch.
- Premium-only features beyond what the free edition already exposes through load, the two export kinds, and inspect.
- Shipping a separate public release channel for the CLI; how the binary is attached to existing releases can be decided when the command is ready to publish.

## Further Notes

Domain language is in `GLOSSARY.md`. The architectural choices are in `docs/adr/0001-cli-over-core-libraries.md`, `docs/adr/0002-sessions-and-jobs.md`, and `docs/adr/0003-agent-facing-cli-contract.md`.

This version was agreed for AI agents first, but the same command should remain usable by a person in a terminal (text default when a TTY is attached).

The project issue tracker has not been configured for agent-ready issues, so this spec is kept in the repository rather than published as a GitHub issue.
