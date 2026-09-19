"""One-time, exact source edits on fix/updater-tls-0.7.2; removed after validation."""
from pathlib import Path

root = Path(__file__).resolve().parents[1]

def edit(path, before, after):
    file = root / path
    text = file.read_text(encoding='utf-8-sig')
    if text.count(before) != 1:
        raise RuntimeError(f'{path}: expected exactly one source anchor: {before[:100]!r}')
    file.write_text(text.replace(before, after), encoding='utf-8', newline='\n')

assert (root / 'version.txt').read_text().strip() == '0.7.1', 'Wrong starting version'
src = 'src/TaskbarTiles/'
edit(src+'AssemblyInfo.cs', 'using System.Runtime.InteropServices;\n', '''using System.Runtime.InteropServices;
using System.Runtime.Versioning;
// Build.ps1 uses CodeDOM, not MSBuild: keep this attribute in source for both paths.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
''')
for field, old, new in [('AssemblyVersion','0.7.1.0','0.7.2.0'), ('AssemblyFileVersion','0.7.1.0','0.7.2.0'), ('AssemblyInformationalVersion','0.7.1','0.7.2')]:
    edit(src+'AssemblyInfo.cs', f'{field}("{old}")', f'{field}("{new}")')
edit(src+'TaskbarTiles.csproj', '    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\n', '''    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
''')
edit(src+'TaskbarTiles.csproj', '    <Compile Include="UpdateTests.cs" />\n', '''    <Compile Include="UpdateTests.cs" />
    <Compile Include="UpdateTlsTests.cs" />
    <Compile Include="InternetDownload.cs" />
''')
edit(src+'app.config', '<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>', '''<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
  <runtime>
    <!-- App-local security policy. No registry edits or certificate bypasses. -->
    <AppContextSwitchOverrides value="Switch.System.Net.DontEnableSchUseStrongCrypto=false;Switch.System.Net.DontEnableSystemDefaultTlsVersions=false" />
  </runtime>
</configuration>''')
edit(src+'TaskbarTiles.cs', 'internal const string Version = "0.7.1";', 'internal const string Version = "0.7.2";')
edit(src+'TaskbarTiles.cs', '// Taskbar Tiles 0.7.0 - source-built Windows utility.', '// Taskbar Tiles 0.7.2 - Windows utility.')
edit(src+'TaskbarTiles.cs', '            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }', '''            if (args.Contains("--test-update-https")) { Environment.Exit(UpdateTlsTests.RunNetwork()); return; }
            if (args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }''')
edit(src+'app.manifest', 'version="0.7.1.0"', 'version="0.7.2.0"')
edit(src+'UpdateTests.cs', '            checks = 0; Version v;', '            UpdateTlsTests.Run(log);\n            checks = 0; Version v;')
edit(src+'Updates.cs', '''        internal static string Download(AvailableUpdate update, CancellationToken token, Action<long, long> progress, out string verifiedHash)
        {
            string sums''', '''        internal static string Download(AvailableUpdate update, CancellationToken token, Action<long, long> progress, out string verifiedHash)
        { return DownloadTo(update, token, progress, out verifiedHash, Path.Combine(Program.Home, "Updates")); }
        // The online integration test uses a unique temporary root; normal updates keep their existing location.
        internal static string DownloadTo(AvailableUpdate update, CancellationToken token, Action<long, long> progress, out string verifiedHash, string updateRoot)
        {
            string sums''')
edit(src+'Updates.cs', 'string folder = Path.Combine(Program.Home, "Updates", update.Tag);', 'string folder = Path.Combine(updateRoot, update.Tag);')
# Write the Internet marker through a native named stream; File.WriteAllText rejects ADS paths.
edit(src+'Updates.cs', '                if (File.Exists(destination)) File.Delete(destination);', '''                InternetDownload.Mark(partial, ReleaseInfo.AssetUrl(update.Tag, update.AssetName));
                if (File.Exists(destination)) File.Delete(destination);''')
edit(src+'Updates.cs', r'''                // Mark the file as an Internet download on NTFS; do not bypass Windows checks.
                try { File.WriteAllText(destination + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + ReleaseInfo.AssetUrl(update.Tag, update.AssetName) + "\r\n"); } catch (IOException) { }
''', '''                // Renaming within the same folder preserves the verified Internet marker.
''')
edit(src+'Updates.cs', 'ClientSize = new Size(680, 460); MinimumSize = new Size(660, 420);', 'ClientSize = new Size(700, 490); MinimumSize = new Size(680, 460);')
edit(src+'Updates.cs', 'body.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));', 'body.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));')
edit(src+'Updates.cs', 'var releases = Theme.Button("Release notes", 125);', 'var releases = Theme.Button("Browser download", 145);')
edit(src+'Updates.cs', '        static string ErrorText(Exception ex)\n', '        internal static string ErrorText(Exception ex)\n')
edit(src+'Updates.cs', '''            var web = ex as WebException; var response = web == null ? null : web.Response as HttpWebResponse;
''', '''            var web = ex as WebException; var response = web == null ? null : web.Response as HttpWebResponse;
            if (web != null && web.Status == WebExceptionStatus.SecureChannelFailure)
                return "Windows could not establish a secure TLS connection to GitHub. Use Browser download to update manually. Certificate checks remain enabled; your installed version is unchanged.";
            if (web != null && web.Status == WebExceptionStatus.TrustFailure)
                return "Windows could not verify GitHub's certificate. Check the PC clock or your network's certificate policy. Do not disable certificate checks. Browser download is available; your installed version is unchanged.";
''')
edit('version.txt', '0.7.1', '0.7.2')
edit('CHANGELOG.md', '# Changelog\n\n## 0.7.0\n\n### Fixed\n', '''# Changelog

## 0.7.2

- Fix updater TLS negotiation in CodeDOM-built executables by declaring the .NET Framework 4.8 target in assembly metadata and opting into OS-selected TLS/strong cryptography in the app-local configuration.
- Retain normal certificate validation, HTTPS-only trusted redirects and SHA-256 verification. Do not enable legacy protocols, modify machine-wide TLS policy or silently retry with weaker security.
- Fix post-download path errors by writing and verifying the Internet security marker through the native named-stream API before exposing the installer.
- Provide an always-available Browser download action and specific TLS/certificate failure explanations.
- Add offline runtime-policy regressions plus an explicit online integration test using the actual built executable to read GitHub metadata, download/verify a released installer and delete the test download without running it.
- Preserve the 0.7.1 launch-placement fixes, Settings Updates tab and all user configuration.

## 0.7.1

- Improve launch metadata and hosted-window identity matching; retain browser-profile and new-window safeguards.
- Show all eligible windows for deliberate selection when the safe placement shortlist is empty.
- Add privacy-conscious launch diagnostics and a dedicated Settings Updates tab.

## 0.7.0

### Fixed
''')
print('Exact 0.7.2 source edits applied. Windows compilation and HTTPS tests are still required.')
