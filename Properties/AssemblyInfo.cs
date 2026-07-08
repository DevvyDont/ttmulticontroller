using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// This is a Windows-only WinForms app (Win32 hooks, PostMessage, DWM). Declare the assembly's platform
// explicitly: the SDK normally injects this for a -windows TFM, but GenerateAssemblyInfo=false (set to keep
// this hand-written file) suppresses that injection, which would otherwise leave CA1416 treating every
// WinForms/Win32 call as "reachable on all platforms".
[assembly: SupportedOSPlatform("windows")]

// Expose internal pure-logic helpers to the characterization test project.
[assembly: InternalsVisibleTo("TTMulti.Tests")]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Multicontroller")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("DF Software")]
[assembly: AssemblyProduct("Multicontroller")]
[assembly: AssemblyCopyright("Copyright © 2015-2021 Daniel Fresneda-Rojas")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("1077f193-3bdb-42ad-9ff9-2a9eb527562b")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("2.0.3.0")]
[assembly: AssemblyFileVersion("2.0.3.0")]
// Pin the informational version too: Application.ProductVersion (what the update check and About window read)
// comes from this attribute, and a single-file build can otherwise append a git-hash suffix that would break
// the version comparison against the release tag.
[assembly: AssemblyInformationalVersion("2.0.3.0")]
