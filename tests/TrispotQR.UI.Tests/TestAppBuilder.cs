using Avalonia;
using Avalonia.Headless;
using TrispotQR.UI;
using TrispotQR.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace TrispotQR.UI.Tests;

/// <summary>
/// Wires the headless platform for every [AvaloniaFact] in this assembly.
///
/// UseHeadlessDrawing is false deliberately. The default headless mode stubs out drawing
/// entirely, which would let a preview test pass without Skia ever running. Turning it off
/// makes Avalonia render through its real Skia backend, which is the thing worth proving
/// given Core pins a different SkiaSharp major than Avalonia was built against.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
