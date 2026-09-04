using System.Reflection;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Tests;

/// <summary>
/// Core must not depend on WPF, or it cannot run on macOS, Linux or mobile.
///
/// A test rather than a note in a document, because this is exactly the kind of constraint
/// that decays: one convenient `using System.Windows.Media` for a Point or a Color and the
/// whole port quietly regresses, on Windows, where nobody would notice.
/// </summary>
public class CorePortabilityTests
{
    private static readonly Assembly Core = typeof(QrDrawing).Assembly;

    [Theory]
    [InlineData("PresentationCore")]
    [InlineData("PresentationFramework")]
    [InlineData("WindowsBase")]
    public void Core_DoesNotReference(string assemblyName)
    {
        var referenced = Core.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.False(
            referenced.Contains(assemblyName, StringComparer.OrdinalIgnoreCase),
            $"TrispotQR.Core references {assemblyName}. Referenced: {string.Join(", ", referenced)}");
    }

    [Fact]
    public void Core_TargetsAPlatformNeutralFramework()
    {
        // Since .NET 5, a `-windows` TFM suffix shows up as a TargetPlatformAttribute, not
        // in TargetFrameworkAttribute.FrameworkName: that string is ".NETCoreApp,Version=v10.0"
        // whether the project is net10.0 or net10.0-windows, so checking it for "windows"
        // can never fail. TargetPlatformAttribute is present only when a platform is set, so
        // its absence is what actually discriminates a platform-neutral assembly.
        var platform = Core.GetCustomAttribute<System.Runtime.Versioning.TargetPlatformAttribute>();

        Assert.Null(platform);
    }

    [Fact]
    public void NoPublicApi_ExposesAWpfType()
    {
        var offenders = Core.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(Describe)
            .Where(d => d.Type?.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true)
            .Select(d => $"{d.Owner}: {d.Type!.FullName}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "WPF types on Core's public surface:\n" + string.Join("\n", offenders));
    }

    private static (string Owner, Type? Type) Describe(MemberInfo member) => member switch
    {
        PropertyInfo p => ($"{p.DeclaringType?.Name}.{p.Name}", p.PropertyType),
        FieldInfo f => ($"{f.DeclaringType?.Name}.{f.Name}", f.FieldType),
        MethodInfo m => ($"{m.DeclaringType?.Name}.{m.Name}", m.ReturnType),
        _ => (member.Name, null),
    };
}
