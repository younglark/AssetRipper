# Command-line interface

The `assetripper` command loads, inspects, and exports Unity game files without starting the web GUI or calling its HTTP API. It is the first-version contract for agents, CI, and other non-interactive hosts, and it is also usable by a person in a terminal.

This page is the static documentation for that contract. It is sufficient to write a skill or script. There is no `schema` command, no MCP server, and no `--agent` flag beyond the no-TTY behavior described here. Settings and configuration-file commands are not part of this version.

The GUI executable and its launch arguments are unchanged. Internal console tools in the repository are not this contract.

For domain language, see the repository [glossary](../../GLOSSARY.md). The accepted design spec remains [docs/specs/agent-cli.md](../specs/agent-cli.md); this page documents the shipped command against that spec and does not replace it.

## Contents

- [Build and run](#build-and-run)
- [Output contract](#output-contract)
- [Exit codes](#exit-codes)
- [Stable JSON fields](#stable-json-fields)
- [Commands](#commands)
- [Confirmation and idempotent export](#confirmation-and-idempotent-export)
- [Sessions and Jobs](#sessions-and-jobs)
- [Pagination](#pagination)
- [Platform and packaging](#platform-and-packaging)
- [Writing a skill or script](#writing-a-skill-or-script)
- [Implementation notes and mismatches](#implementation-notes-and-mismatches)

## Build and run

The command name is `assetripper`. Build it from this repository with the [.NET 11 SDK](Requirements.md):

```bash
dotnet build Source/AssetRipper.CLI/AssetRipper.CLI.csproj
```

The executable is written to `Source/0Bins/AssetRipper.CLI/<Configuration>/assetripper` (for example `Source/0Bins/AssetRipper.CLI/Debug/assetripper`). You can also run it without installing:

```bash
dotnet run --project Source/AssetRipper.CLI -- --help
```

Arguments after `--` are passed to `assetripper`.

Current published GUI archives (stable, pre-release, and nightly) ship the GUI executable only. They do not yet include this `assetripper` command. How the CLI binary will be attached to existing releases is still an open packaging decision; until then, build it from source.

First-version automated tests and the command-syntax checks for this documentation were run on Linux. That is a statement about what has been validated, not a claim that Windows or macOS builds fail.

The CLI uses the same default configuration the GUI would load, if that file is present; otherwise it uses built-in defaults. This version does not add a command to write, list, or edit settings. The CLI does not create a log file in the working directory by default. Diagnostics go to stderr.

## Output contract

- Result data goes to stdout. Progress, warnings, and errors go to stderr. Do not parse stderr as the result.
- When stdout is not a TTY, results default to JSON. `--json` forces JSON even when stdout is a TTY.
- `--help` is always plain text, including when `--json` is present or stdout is not a TTY. It is written to stdout only and exits `0`.
- Running `assetripper` with no arguments is the same as `assetripper --help`.
- There is no spinner or color when stdout is not a TTY. `NO_COLOR` is respected even when a TTY is attached.
- Messages, flag names, and error codes are stable English. They do not depend on the host locale.
- The command does not prompt, page, or wait for interactive confirmation. A non-empty export directory is refused unless the rules in [Confirmation and idempotent export](#confirmation-and-idempotent-export) are met.

`--input` and `--session` are mutually exclusive. Commands that need loaded files require exactly one of them. `--input` accepts a file or a directory and may be repeated. A missing or unreadable input path exits `5`.

Options may appear before or after the command and its subcommand. Unknown commands, unknown options, and invalid option values are usage errors (exit `2`), including `assetripper unknown --help`. Use `assetripper --help` or a known command's `--help` instead.

## Exit codes

These meanings match `assetripper --help` and do not change after being published there:

| Code | Meaning |
| --- | --- |
| `0` | Success, including an already-completed export and a Job that is still running but now queryable |
| `2` | Usage error |
| `5` | Not found: input path, Session, Job, or Asset Reference |
| `8` | Retryable error; only used when it is safe to retry the same command |
| `1` | Other runtime failure |

Exit `0` is also used when `export` or `job wait` stops at `--timeout` or `--no-wait` and returns a queryable Job with `status` `running`. Do not retry the original `export` in that case; run `job wait` (or `job status` / `job cancel`).

Typical uses of the other codes:

- `2`: unknown command or option; missing required `--output` or `--input`/`--session`; both `--input` and `--session`; invalid `--format`, `--limit`, `--timeout`, or `--cursor`; `--type` on a `list` kind other than `assets`; export to a well-known personal folder (`Desktop`, `Documents`, or `Downloads`).
- `5`: missing input path, ended or unknown Session, unknown Job, or unknown Asset Reference.
- `8`: a Session is still `loading` when you inspect it, or a mutating operation conflicts with a Job that is already running. Retry the same command after `session status`, `job wait`, or `job cancel`, as the hint says.
- `1`: a non-empty output directory that is not an Existing export and was not confirmed; a `get` format that is legal but not supported for that asset; a Session that already `failed`; other runtime failures.

## Stable JSON fields

Only the fields already named in the spec are the stable v1 contract. Later versions may add fields. Clients must not treat unlisted fields as required. JSON property names are camelCase. In the current implementation, null or inapplicable fields are omitted rather than set to `null`.

**Stable v1 fields and values**

| Context | Stable field or value | Meaning |
| --- | --- | --- |
| Error object on stderr | `code` | Machine-readable error class |
| Error object on stderr | `hint` | Legal next step |
| Truncated `list` or `search` | `truncated` | More results exist |
| Truncated `list` or `search` | `next` | Opaque token for the next page |
| Truncated `list` or `search` | `hint` | How to continue |
| Successful duplicate export | `existing` | `true` when this is an already-completed export of the same inputs and kind |
| Deferred export or `job wait` timeout | `status` | `running` while the Job is still in progress and now queryable |
| Deferred export | a Job identifier | Use this identifier with `job status`, `job wait`, or `job cancel` |
| `get --output` metadata (JSON mode) | `path`, `format`, `size` | Where the payload was written, the requested format, and the size in bytes |
| Session load status | `loading`, `ready`, `failed` | Whether inspect and export commands are allowed |

Continue a truncated list or search with `--cursor`, passing the previous `next` token. Do not invent cursor tokens.

Examples in this page may also show current implementation fields such as `message`, `id`, `items`, `name`, `type`, `ref`, `output`, `project`, `files`, `version`, or a Session `closed` status. Those fields are **additive examples**. They appear in today's JSON so you can recognize a real result, but they are not part of the frozen v1 contract.

### Error object (stderr)

Stable fields: `code`, `hint`. The `message` property in this example is additive.

```json
{
  "code": "output_not_empty",
  "hint": "Choose another directory, empty this one, or pass --confirm=<directory name>.",
  "message": "The output directory is not empty."
}
```

`code` values currently emitted include `usage`, `not_found`, `output_not_empty`, `output_is_file`, `output_exists`, `export_failed`, `inspect_failed`, `get_failed`, `unsupported_format`, `session_failed`, `session_loading`, `session_busy`, and `job_failed`. Treat the field name `code` as stable; do not treat this list of values as a frozen enumeration.

### Truncated list or search (stdout)

Stable fields when a page is truncated: `truncated`, `next`, and `hint`. The `items` array and each item's `name`, `type`, and `ref` properties in this example are additive.

```json
{
  "items": [
    {
      "name": "Greeting",
      "type": "TextAsset",
      "ref": "c0:1"
    }
  ],
  "truncated": true,
  "next": "<token>",
  "hint": "Pass --cursor with this next token to fetch the next page."
}
```

The `ref` value is the Asset Reference. Treat it as opaque: the current `c0:1` form identifies a collection instance and an asset within it, not a collection filename; collections with identical filenames remain distinct. Pass that exact value to `get asset`. An Asset Reference is stable for the life of a Session, and for a one-shot command it is stable for the life of that process. When the result is not truncated, the current implementation omits `truncated`, `next`, and `hint`.

### Duplicate export (stdout)

The stable field is `existing` with the value `true`. Other properties in this example are additive.

```json
{
  "status": "completed",
  "output": "./ExportedProject",
  "project": "./ExportedProject/ExportedProject",
  "existing": true
}
```

### Deferred export or timed-out wait (stdout, exit 0)

The stable signals are `status` with the value `running`, and a Job identifier. In the current implementation the identifier is the `id` property. `id`, `output`, and `hint` in this example are additive.

```json
{
  "id": "<job-id>",
  "status": "running",
  "output": "./ExportedProject",
  "hint": "Run assetripper job wait <job-id>."
}
```

### `get --output` metadata (stdout, JSON mode)

Stable fields: `path`, `format`, and `size`.

```json
{
  "path": "./asset.png",
  "format": "image",
  "size": 2048
}
```

Without `--output`, `get` writes the asset payload itself to stdout. It does not wrap that payload in a CLI JSON envelope, and binary formats are never embedded in JSON. See [get asset](#get-asset).

### Session load status (stdout)

The stable values of the load status are `loading`, `ready`, and `failed`. In the current implementation the status is the `status` property, and the Session identifier is `id`. `id`, `message`, and `hint` in this example are additive.

```json
{
  "id": "<session-id>",
  "status": "ready"
}
```

A Session that is still loading also includes a hint to run `session status`. A failed Session includes a hint to run `session start` again.

## Commands

Every command below accepts `--help` and, where it returns data, `--json`. Examples are copied from `--help` where one exists, and can be pasted into a shell after replacing placeholders such as `<ref>`, `<id>`, and `<token>`.

### Global options

```text
assetripper --help
assetripper --version
assetripper --json --version
```

| Option | Description |
| --- | --- |
| `--help` | Show help for the selected command, or root help if no command is selected |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--version` | Show the version |

`--version` writes a JSON object with a `version` property when JSON mode is on, or a line such as `assetripper 2.0.0` when a TTY is attached and `--json` is not passed. `version` is an additive field.

### `export unity-project` and `export primary-content`

```text
assetripper export unity-project --input ./Game --output ./ExportedProject
assetripper export primary-content --input ./Game --output ./Content --dry-run
assetripper export unity-project --session <id> --output ./ExportedProject --timeout 60
assetripper export primary-content --input ./Game --output ./Content --no-wait
assetripper job wait <id>
```

- `export unity-project` performs a Unity Project Export: the loaded assets as a Unity project.
- `export primary-content` performs a Primary Content Export: the loaded assets as their primary decoded files, such as textures, audio, models, and scripts, not as a Unity project.

Export waits until it finishes by default. Progress goes to stderr only. `--output <dir>` is required; omitting it is a usage error that names the missing flag.

| Option | Description |
| --- | --- |
| `--input <path>` | Game file or directory to load. Repeatable. Mutually exclusive with `--session`. |
| `--session <id>` | Use a previously started Session. Mutually exclusive with `--input`. |
| `--output <dir>` | Destination directory (required) |
| `--dry-run` | Preview the files that would be written without changing the disk, including when the output directory is not empty |
| `--confirm <name>` | Confirm writing into a non-empty directory. `<name>` must be the resolved directory name. Both `--confirm=<name>` and `--confirm <name>` are accepted. |
| `--timeout <seconds>` | Stop waiting after this many seconds and return a queryable Job (exit `0`, not `8`) |
| `--no-wait` | Return a queryable Job immediately (exit `0`, not `8`) |
| `--wait` | Wait for completion (default). Cannot be combined with `--no-wait`. Accepted by the command; not currently listed in `export --help`. |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--help` | Show this help |

`--timeout` takes a non-negative number of seconds. On `--timeout` or `--no-wait`, the process that owns the Job keeps enough state for that Job to finish, fail, or be cancelled after this command exits.

`--dry-run` loads and exports to an in-memory filesystem, then lists the paths that would be written. It does not create, modify, or delete files under `--output`. A dry run currently reports `status` `dry_run` (additive) and, in JSON mode, a `files` array of relative paths (additive).

A completed write currently reports `status` `completed` (additive) and the `--output` path. For Unity Project Export, the current implementation also includes a `project` path pointing at the `ExportedProject` directory under `--output`. These extra fields are not frozen.

Export to a path whose final segment is `Desktop`, `Documents`, or `Downloads` (compared case-insensitively) is refused with exit `2`, matching the GUI safeguard. Only one write runs at a time on a given Session; a conflict exits `8` and the hint names the Job to wait for or cancel.

### `list <kind>`

```text
assetripper list assets --input ./Game --name "*.png"
assetripper list scenes --session <id>
assetripper list assets --input ./Game --cursor <token>
```

Kinds: `assets`, `bundles`, `scenes`, `collections`, `resources`, `failed-files`.

| Option | Description |
| --- | --- |
| `--input <path>` | Game file or directory to load. Repeatable. Mutually exclusive with `--session`. |
| `--session <id>` | Use a previously started Session. Mutually exclusive with `--input`. |
| `--limit <n>` | Maximum number of items to return (default `100`). Must be a positive integer. |
| `--cursor <token>` | Continue a truncated list using the previous `next` token |
| `--name <glob>` | Filter by name glob (every `list` kind; case-insensitive) |
| `--type <type>` | Filter by asset type (`list assets` only). Using it on another kind is exit `2`. |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--help` | Show this help |

`list assets` returns an Asset Reference for each item (the additive `ref` field in JSON, or the third tab-separated column in text mode). Pass that same value to `get asset`. `--type` is a case-insensitive match against the asset type name (the additive `type` field). `--name` uses a simple glob, as in `"*.png"`.

### get asset

```text
assetripper get asset <ref> --input ./Game --format json
assetripper get asset <ref> --session <id> --output ./asset.png --format image
```

Retrieve an asset by the Asset Reference returned by `list assets` (or by `search`, which returns the same kind of identifier). A missing Asset Reference exits `5`.

For `--format json`, non-finite numbers are encoded as the JSON strings `"Infinity"`, `"-Infinity"`, and `"NaN"`. Finite numbers remain JSON numbers; asset string values are unchanged.

| Option | Description |
| --- | --- |
| `--input <path>` | Game file or directory to load. Repeatable. Mutually exclusive with `--session`. |
| `--session <id>` | Use a previously started Session. Mutually exclusive with `--input`. |
| `--format <format>` | Output format: `json`, `yaml`, `text`, `binary`, `image`, `audio`, `model`, `font`, or `video` (default `json`) |
| `--output <file>` | Write the payload to this file instead of stdout |
| `--confirm <name>` | Confirm overwriting an existing file. `<name>` must be the file name. Both `--confirm=<name>` and `--confirm <name>` are accepted. |
| `--json` | Write metadata as JSON even when stdout is a TTY (only when `--output` is used) |
| `--help` | Show this help |

An invalid `--format` is a usage error (exit `2`) whose hint lists the legal values above.

`json` and `yaml` are available for assets in general. `text`, `binary`, `image`, `audio`, `model`, `font`, and `video` are available only when that asset supports them. If the requested format is legal but not valid for the asset, the command exits `1` and the hint lists the formats that asset supports.

Without `--output`, the payload is written to stdout. Text formats (`json`, `yaml`, `text`) are written as UTF-8 text. Other formats are written as raw bytes. The CLI does not wrap that payload in a metadata object, even in JSON mode.

With `--output`, the payload is written to that file. In JSON mode, stdout is metadata only: stable fields `path`, `format`, and `size`. If the output file already exists, the write is refused with exit `1` unless `--confirm=<file name>` matches the resolved file name.

### `search <query>`

```text
assetripper search Player --input ./Game
assetripper search texture --session <id> --limit 20
```

In this version, `search` is a case-insensitive substring match against asset names. It is not a full query language. It does not accept `--name` or `--type`.

| Option | Description |
| --- | --- |
| `--input <path>` | Game file or directory to load. Repeatable. Mutually exclusive with `--session`. |
| `--session <id>` | Use a previously started Session. Mutually exclusive with `--input`. |
| `--limit <n>` | Maximum number of items to return (default `100`). Must be a positive integer. |
| `--cursor <token>` | Continue a truncated search using the previous `next` token |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--help` | Show this help |

Truncation and `--cursor` follow the same rules as `list`.

### `session start`, `session status`, `session close`

```text
assetripper session start --input ./Game
assetripper session start --input ./Game --no-wait
assetripper session start --input ./Game --timeout 10
assetripper list assets --session <id>
assetripper session status <id>
assetripper session close <id>
```

A Session is a loaded set of game files that later commands can reuse without loading them again. It is a local background process, not reachable from the network, and it holds data in memory. Several Sessions may exist at the same time. Identifiers are positional on the session subcommands: `session status <id>` and `session close <id>`. Other commands take `--session <id>`.

`session start` returns a Session identifier immediately in the sense that a host timeout does not have to lose the load: `--no-wait` or `--timeout` still prints the identifier and the current load status, and you can poll with `session status`. By default, `session start` waits until the status is `ready` or `failed`. Load progress lives on the Session; it does not create a Job.

| Option | Description |
| --- | --- |
| `--input <path>` | Game file or directory to load (`start` only). Repeatable. |
| `--timeout <seconds>` | Stop waiting for the load to finish after this many seconds. The Session remains queryable. |
| `--no-wait` | Return the Session identifier without waiting for the load to finish |
| `--wait` | Wait for `ready` or `failed` (default). Cannot be combined with `--no-wait`. |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--help` | Show this help |

`--timeout` and `--no-wait` apply to `session start` only. A missing or unreadable explicit input makes a waiting start exit `5` with error code `not_found`, while still printing the failed Session identifier. If a deferred start returned while loading, later `session status` remains a successful query (exit `0`) reporting `failed`; inspecting that failed Session still exits `1`.

Inspect commands (`list`, `get`, `search`) on a Session that is still `loading` exit `8` with a hint to run `session status`. On a Session that is already `failed`, they exit `1` with a hint to run `session start` again. After the Session is `ready`, `list`, `get`, and `search` may run while an export Job is in progress, including `get --output`.

`session close` ends the Session and releases its loaded data. It exits immediately when successful. Closing while an export Job is still running exits `8` and does not close the Session; the hint tells you to `job wait` or `job cancel` first. An idle Session ends after 30 minutes. After a process or machine restart, or after idle timeout or close, a previous Session identifier is not found (exit `5`). There is no public flag to change the idle timeout.

The current implementation reports a successful close with `status` `closed`. That value is additive; the frozen load-status values remain `loading`, `ready`, and `failed`.

### `job status`, `job wait`, `job cancel`

```text
assetripper job status <id>
assetripper job wait <id> --timeout 60
assetripper job cancel <id>
```

A Job is a long-running export that can be queried until it finishes. Job identifiers are positional. Missing or unknown Job identifiers exit `5`.

| Option | Description |
| --- | --- |
| `--timeout <seconds>` | Stop waiting after this many seconds and leave the Job queryable (`wait` only). Exits `0` with `status` `running`, not `8`. |
| `--json` | Write result data as JSON even when stdout is a TTY |
| `--help` | Show this help |

`job wait` waits for completion by default. `job cancel` stops export I/O cooperatively and leaves any partially written output in place. That directory is not an Existing export. A later export to the same non-empty directory follows the usual confirm rules.

The `job wait --timeout` deadline covers connecting to the owner and reading its reply. If the reply misses the deadline, the result still includes the Job identifier, `status: running`, and a hint to wait again; additive output details may be absent until a reply is available.

The current implementation also reports Job `status` values `completed`, `dry_run`, `failed`, and `cancelled` (as listed in `job --help`). Those values are additive. The stable deferred-export signal is `status` with the value `running`. Jobs remain queryable while their owner process is alive, including 30 minutes idle after completion in the current implementation.

## Confirmation and idempotent export

A well-formed export to a new or empty directory proceeds without `--confirm`.

A non-empty output directory is refused by default with exit `1` and a hint to choose another directory, empty it, or pass `--confirm=<directory name>`. This is not a usage error. `--dry-run` still works when the directory is non-empty; it does not write.

`--confirm` must be the resolved directory name (the final segment of the full path), not a generic yes value, and not a relative token such as `.` or `..`. For example, `--output .` must be confirmed with the current directory's real name. The comparison is case-sensitive. Confirmed export writes into the directory without first deleting files that are already there.

A second export of the same inputs and the same export kind to the same output directory exits `0` and reports `existing: true` without writing again. That match is recognized from `.assetripper-export.json` in a successful output directory. A non-empty directory that is not the result of that same export is still refused unless you pass a matching `--confirm`.

There is no global `--yes` or `--allow-mutations` switch.

`get --output` uses the same confirm style when the target file already exists: `--confirm` must be the output file name.

## Sessions and Jobs

Typical agent flow:

```text
assetripper session start --input ./Game
assetripper list assets --session <id> --name "Player*"
assetripper get asset <ref> --session <id> --format json
assetripper export unity-project --session <id> --output ./ExportedProject --no-wait
assetripper job wait <id> --timeout 60
assetripper session close <id>
```

1. Start a Session and, unless you passed `--no-wait` or `--timeout`, wait until it is `ready`.
2. Inspect with `--session <id>` instead of `--input`.
3. Export with `--session <id>`. If the export might exceed your host timeout, use `--timeout` or `--no-wait` and then `job wait`.
4. Close the Session when you are done.

One-shot commands (`--input` without a Session) load, perform one action, and exit. If a one-shot export returns a Job because of `--timeout` or `--no-wait`, the Job remains queryable while its owner process is alive, including 30 minutes idle after completion in the current implementation. That owner is not a reusable Session: `session status` and inspect commands that use the owner id exit `5` with `not_found` (the loaded data belongs to the Job and is not kept as Session state). `session close` on the owner still ends the process. A Session started with `session start` that actually failed to load still reports `failed` on `session status` (exit `0`).

On a single Session, only one mutating operation may run at a time. A conflicting write exits `8` with a hint naming the Job or operation to wait for. After the Session is `ready`, `list`, `search`, and `get` (including `get --output`) may run during an export Job. Only a second mutating export, or `session close` while a Job is running, is a conflict.

## Pagination

`list` and `search` default to `--limit 100` so a large game cannot flood the caller. When a page is truncated, JSON includes the stable fields `truncated`, `next`, and `hint`. Continue with `--cursor <token>`, using the opaque `next` value from the previous result:

```text
assetripper list assets --input ./Game --limit 100
assetripper list assets --input ./Game --limit 100 --cursor <token>
```

A later page may itself be truncated and will then include a new `next` token. When there are no further items, the current implementation omits `truncated`, `next`, and `hint`. An invalid `--cursor` is a usage error (exit `2`).

## Platform and packaging

- Automated CLI tests in this repository, and the help-syntax checks used for this page, have been run on Linux.
- Some permission-related tests are written for Unix file modes and are skipped on Windows in the test suite. That does not mean the command is unsupported or known to fail on Windows or macOS.
- Sessions are implemented as a local process. The implementation uses a local Unix-domain socket and contains a Windows-specific branch for socket file permissions. Session and Job hosting has not been published as validated on Windows or macOS; do not infer failure from the lack of those results.
- Existing release and nightly packages still contain only the GUI executable. Build `assetripper` from source until a CLI artifact is part of those packages.

## Writing a skill or script

You do not need a `schema` command or MCP. Use this page plus `assetripper <command> --help`.

1. Invoke `assetripper` with stdout redirected or `--json` so results are JSON (except `--help` and a `get` payload written to stdout).
2. Parse stdout as the result and stderr as diagnostics or a JSON error object with stable `code` and `hint`.
3. Branch on exit codes `0`, `2`, `5`, `8`, and `1` as documented above.
4. Prefer a Session when you will inspect and then export the same inputs.
5. Always pass `--output` to export. Use `--dry-run` to preview. Use `--confirm` only with the resolved directory or file name. Treat `existing: true` as success and do not write again.
6. Bound `list` and `search` with `--limit`, and continue with `--cursor` from `next` when `truncated` is true.
7. Take Asset References from `list assets` or `search` and pass them unchanged to `get asset`. Use `--output` for binary payloads you do not want in context.
8. If export returns `status` `running` and a Job identifier with exit `0`, call `job wait` instead of retrying `export`.
9. Depend only on the [stable JSON fields](#stable-json-fields). Ignore or tolerate additional properties.

## Implementation notes and mismatches

This documentation follows the accepted spec. Where the current command differs from a literal reading of the spec, or where help is slightly narrower than the parser, the difference is recorded here instead of rewriting the spec.

- `export` accepts `--wait` as the default (and rejects `--wait` together with `--no-wait`), but `export --help` does not currently list `--wait`. `session --help` does list it.
- Successful `session close` currently reports an additive status value `closed`, which is not one of the frozen load-status values (`loading`, `ready`, `failed`).
- A one-shot export Job's owner process is not a reusable Session. After the one-shot export is accepted, `session status` and `--session` inspect commands on that owner id exit `5` with `not_found` rather than reporting the frozen `failed` load status. `job status` / `job wait` remain queryable, and `session close` still ends the owner.
- Error objects currently include an additive `message` field in addition to the stable `code` and `hint`.
- The current implementation uses the additive property `id` for both Session and Job identifiers (a 32-character lowercase hex Session id, and a 64-character lowercase hex Job id). The stable contract requires that a Session identifier and a Job identifier be returned and reusable; it does not freeze those property names or formats.
- `--confirm` compares the resolved directory or file name with ordinal, case-sensitive equality. Protected personal folder names (`Desktop`, `Documents`, `Downloads`) are compared case-insensitively, as the spec requires.
- `search` in this version matches asset names only, as the spec's implementation decisions already state.

