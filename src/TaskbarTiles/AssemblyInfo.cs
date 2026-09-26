using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
// Build.ps1 uses CodeDOM, not MSBuild: keep this attribute in source for both paths.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: AssemblyTitle("Taskbar Tiles")]
[assembly: AssemblyDescription("Mouse-first Windows switcher, app launcher and monitor/zone placement utility")]
[assembly: AssemblyCompany("Taskbar Tiles contributors")]
[assembly: AssemblyProduct("Taskbar Tiles")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Taskbar Tiles contributors")]
[assembly: AssemblyVersion("0.12.1.0")]
[assembly: AssemblyFileVersion("0.12.1.0")]
[assembly: AssemblyInformationalVersion("0.12.1")]
[assembly: ComVisible(false)]
