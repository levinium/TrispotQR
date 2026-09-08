using System.Reflection;

namespace TrispotQR.ViewModels.Tests;

/// <summary>
/// The shared view models must not depend on any UI toolkit, or the Avalonia app cannot use
/// them and CI cannot run them on Linux and macOS.
///
/// A test rather than a note, because this is the kind of constraint that decays: one
/// convenient using directive for a Color or a Dispatcher and it is gone, on Windows, where
/// nobody would notice. The same guard exists for Core in CorePortabilityTests.
/// </summary>
public class ViewModelsPortabilityTests
{
    private static readonly Assembly ViewModels = typeof(MainViewModel).Assembly;

    [Theory]
    [InlineData("PresentationCore")]
    [InlineData("PresentationFramework")]
    [InlineData("WindowsBase")]
    [InlineData("Avalonia.Base")]
    [InlineData("Avalonia.Controls")]
    public void ViewModels_DoNotReference(string assemblyName)
    {
        var referenced = ViewModels.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.False(
            referenced.Contains(assemblyName, StringComparer.OrdinalIgnoreCase),
            $"TrispotQR.ViewModels references {assemblyName}. Referenced: {string.Join(", ", referenced)}");
    }

    /// <summary>
    /// TargetFrameworkAttribute is NOT the thing to check: its FrameworkName is identical
    /// for net10.0 and net10.0-windows. Since .NET 5 the platform suffix lives here.
    /// </summary>
    [Fact]
    public void ViewModels_TargetAPlatformNeutralFramework() =>
        Assert.Null(ViewModels.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>());

    [Fact]
    public void ViewModels_DoNotReferenceTheApp()
    {
        var referenced = ViewModels.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("TrispotQR", referenced);
    }
}
