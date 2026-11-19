# AssetRipper

AssetRipper analyzes Unity game files and exports them for inspection or reuse. This glossary records the language for the agent-oriented command-line interface and the export concepts it exposes.

## Language

**Session**:
A loaded set of game files that later commands can reuse without loading them again.
_Avoid_: Connection, server, daemon, workspace

**Job**:
A long-running export that can be queried until it finishes.
_Avoid_: Task, process (when meaning an export in progress)

**Unity Project Export**:
An export of the loaded assets as a Unity project.
_Avoid_: Unity export, project dump

**Primary Content Export**:
An export of the loaded assets as their primary decoded files, such as textures, audio, models, and scripts, not as a Unity project.
_Avoid_: Raw export, resource export, content dump

**Asset Reference**:
A stable identifier for an asset, as returned by list, used to retrieve that same asset.
_Avoid_: Ref, handle, path ID (used alone in prose)
