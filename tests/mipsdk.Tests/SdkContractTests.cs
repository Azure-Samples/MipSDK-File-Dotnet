using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.InformationProtection;

namespace mipsdk.Tests;

public sealed class SdkContractTests
{
    [Fact]
    public void PlatformWrapperHasTheVerifiedCompatibleTargetFramework()
    {
        var actual = typeof(MIP).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        Assert.Equal(OperatingSystem.IsLinux() ? ".NETCoreApp,Version=v6.0" : ".NETStandard,Version=v2.0", actual);
    }

    [Fact]
    public void PlatformNativeAssetsArePresentAlongsideTheExecutable()
    {
        string[] libraries = OperatingSystem.IsLinux()
            ? ["libmip_dotnet.so", "libmip_core.so", "libmip_file_sdk.so", "libmip_protection_sdk.so", "libmip_upe_sdk.so"]
            : ["mip_dotnet.dll", "mip_core.dll", "mip_file_sdk.dll", "mip_protection_sdk.dll", "mip_upe_sdk.dll"];
        var directory = OperatingSystem.IsLinux() ? AppContext.BaseDirectory : Path.Combine(AppContext.BaseDirectory, "x64");
        foreach (var library in libraries)
        {
            Assert.True(File.Exists(Path.Combine(directory, library)), $"Required native asset is absent: {library}");
        }
    }
}
