# CLI over the core libraries, not the web API

AssetRipper already has a web GUI and HTTP API. We are adding an `assetripper` command for AI agents. It will be a new `AssetRipper.CLI` project that calls the existing import, processing, and export libraries directly. The GUI executable and its launch arguments stay unchanged.

A thin client over the HTTP API would force agents to start and manage a web server, and would split business logic across two surfaces. Reusing the web server as the CLI backend was rejected for the same reason.
