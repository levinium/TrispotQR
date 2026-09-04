namespace TrispotQR.Core.Payloads;

/// <summary>
/// The result of checking something the user typed into the Link box.
/// </summary>
/// <param name="Payload">
/// What actually gets encoded, with https:// added when the input had no scheme. Usable
/// even when <paramref name="IsValid"/> is false, so the preview keeps working while the
/// user is still typing.
/// </param>
/// <param name="IsValid">True when this looks like a real, openable web address.</param>
/// <param name="Message">
/// Plain-English text for the UI. A warning when invalid, and a short confirmation when
/// https:// was added for the user. Null when there is nothing worth saying.
/// </param>
public sealed record UrlCheck(string Payload, bool IsValid, string? Message);
