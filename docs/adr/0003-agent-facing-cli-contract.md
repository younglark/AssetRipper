# Agent-facing CLI contract

The CLI is meant to be called by agents in a non-TTY, timeout-prone host. Data goes to stdout; progress, warnings, and errors go to stderr. When stdout is not a TTY, output defaults to JSON with stable fields; `--json` is always available; JSON errors include `code` and `hint`. There is no spinner or color when stdout is not a TTY, and `NO_COLOR` is respected.

Exit codes are `0` success, `2` usage, `5` not found, `8` retryable, and `1` other. If an export or wait hits `--timeout` or `--no-wait`, the command returns a job reference with `status: running` and exits `0` — not `8` — so the agent waits with `job wait` instead of retrying the original export. `job wait` itself also accepts `--timeout` and uses the same rule.

Export requires `--output` and supports `--dry-run`. A non-empty output directory is rejected with exit `1` unless confirmed with `--confirm`, which accepts both `--confirm=<name>` and `--confirm <name>`. A second run with the same inputs and output reports `existing: true` and exits `0`. That match is recognized from `.assetripper-export.json` in the output directory. There is no global `--yes` or `--allow-mutations`.

The first version ships static documentation, not a `schema` command or MCP. Only JSON fields already named in the spec are the stable v1 contract; later versions may add fields.
