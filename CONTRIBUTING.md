# Contributing

Open a focused issue describing the navigation problem before a large redesign. Pull requests should preserve existing settings and mouse commands.

Build on Windows with .NET Framework 4.8: run `Build.cmd`. A Visual Studio project/solution is also included. Run the compiled executable with `--self-test` after a Visual Studio build. Inno Setup 6 is required only to package the installer; `tools/Package.ps1` builds, tests and packages.

Keep C# 5 compatibility unless the project explicitly upgrades its runtime/tooling. No private paths, screenshots, tokens, logs or user settings in commits. Do not add background networking, forced focus bypasses or process-killing “fixes”. Separate helper test results from live Windows testing.

For releases, update `version.txt`, Program.Version, assembly/manifest metadata and the installer default; add matching `docs/releases/vX.Y.Z.md`. Build and manually test, then tag `vX.Y.Z` and push that tag. The Release workflow creates the installer and checksum assets after its tests pass. Never move a published tag or replace a published installer. The updater accepts stable three-part tags only.

Maintainer metadata and repository settings are documented in `repository.json`. Forks must change the hard-coded updater repository and release workflow guard before publishing their own installers.
