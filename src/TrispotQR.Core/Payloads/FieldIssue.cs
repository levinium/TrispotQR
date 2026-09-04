namespace TrispotQR.Core.Payloads;

/// <summary>How badly wrong a field is.</summary>
public enum IssueSeverity
{
    /// <summary>Worth saying, but the code is still worth making. Does not block saving.</summary>
    Warning,

    /// <summary>The code would be broken or useless. Blocks saving.</summary>
    Error,
}

/// <summary>
/// One problem with one field of the current content.
/// </summary>
/// <param name="Field">
/// Which input it belongs to, matching the <c>FieldName</c> on the control that shows it.
/// <see cref="FieldIssue.Form"/> for a problem that belongs to the form as a whole rather
/// than to any single box.
/// </param>
/// <param name="Message">Plain-English text shown under the field.</param>
/// <param name="Severity">Whether this blocks saving.</param>
public sealed record FieldIssue(string Field, string Message, IssueSeverity Severity)
{
    /// <summary>
    /// The field name for a problem no single box owns, such as a contact card that has
    /// details but no name. Highlighting three boxes red for one missing identity would
    /// point at the wrong thing.
    /// </summary>
    public const string Form = "";

    public static FieldIssue Error(string field, string message) =>
        new(field, message, IssueSeverity.Error);

    public static FieldIssue Warning(string field, string message) =>
        new(field, message, IssueSeverity.Warning);
}
