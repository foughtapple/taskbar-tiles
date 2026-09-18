# Updater HTTPS and TLS

## Why 0.7.0 / 0.7.1 could fail

`tools/Build.ps1` invokes CodeDOM directly. Unlike the MSBuild project, that path did not generate a `TargetFrameworkAttribute`. `supportedRuntime` selected the installed CLR but did not declare the executable's framework target. This left networking defaults dependent on legacy compatibility and local machine configuration. The reported secure-channel error is consistent with this gap; it is not proof that every TLS failure has the same cause.

Starting in 0.7.2, AssemblyInfo.cs declares `.NETFramework,Version=v4.8`. The MSBuild project disables duplicate attribute generation. The installed EXE configuration explicitly sets `Switch.System.Net.DontEnableSchUseStrongCrypto=false` and `Switch.System.Net.DontEnableSystemDefaultTlsVersions=false`.

Windows chooses the secure protocol. The application does not pin an obsolete protocol, change Schannel registry keys, set an accept-all certificate callback, or retry with validation disabled. The existing GitHub host allowlist, redirect checks, download limits and SHA-256 verification remain.

## Recovering an affected installation

Download the latest Setup.exe from https://github.com/foughtapple/taskbar-tiles/releases/latest in a browser and run it over the current installation. Do not uninstall first. Existing settings, favourites and X-Mouse paths are retained. Afterwards, Settings > Updates uses the corrected transport.

A corporate proxy, invalid certificate chain, disabled OS TLS policy or incorrect clock can cause an independent failure. Use the browser fallback or consult the network administrator; do not bypass certificate validation or disable security software.

## Tests

- `Build.cmd` runs offline regressions inside the built executable. They check its framework-target attribute, app-local security switches, actual default TLS policy and error messages.
- `tools/Test-UpdateHttps.ps1` is an explicitly online integration test. It invokes that same executable with `--test-update-https`, uses its own .config, queries the public GitHub release and exercises the production checksum/redirect/installer-download path. It never starts the downloaded installer and cleans its unique temporary directory.
- The test writes `build/app/update-network-test.log`. The main build and release workflows retain that record and require the test to pass.
- Separate installer smoke tests exercise install, upgrade and uninstall on the disposable runner.

Reference: https://learn.microsoft.com/en-us/dotnet/framework/network-programming/tls
