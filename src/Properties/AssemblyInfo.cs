using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Allow test project to access internal types
[assembly: InternalsVisibleTo("Qobuzarr.Tests")]

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
// Note: Basic assembly attributes are now generated automatically by MSBuild from csproj properties
[assembly: AssemblyDescription("High-quality music indexer and download client for Qobuz streaming service integration with Lidarr")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("12345678-1234-5678-9012-123456789012")]

// Version information is now managed in Qobuzarr.csproj to maintain single source of truth
// The build system will automatically set AssemblyVersion, FileVersion, and InformationalVersion
// from the project file properties

// Plugin-specific attributes for Lidarr
[assembly: AssemblyMetadata("PluginName", "Qobuzarr")]
[assembly: AssemblyMetadata("PluginAuthor", "RicherTunes")]
[assembly: AssemblyMetadata("PluginUrl", "https://github.com/richertunes/qobuzarr")]
[assembly: AssemblyMetadata("MinimumLidarrVersion", "2.0.0.0")]
