namespace TrispotQR.Core.Presets;

/// <summary>
/// Where saved styles and settings live.
///
/// An interface because the answer differs on every platform, and differs again on mobile,
/// where the app writes into its own sandbox. Core asks; it does not decide.
/// </summary>
public interface ISettingsLocation
{
    string Directory { get; }
}
