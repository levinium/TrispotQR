using TrispotQR.Core.Payloads;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// What each content type considers complete and usable.
///
/// The rules themselves are covered in <see cref="FieldRulesTests"/>. What matters here is
/// that each editor applies the right rule to the right field, and reports it against a
/// field name the UI can attach to a box, because an error the user cannot locate is barely
/// better than no error at all.
/// </summary>
public class EditorValidationTests
{
    private static FieldIssue? Issue(ContentEditor editor, string field) =>
        editor.Issues.FirstOrDefault(i => i.Field == field);

    private static void AssertError(ContentEditor editor, string field)
    {
        var issue = Issue(editor, field);
        Assert.True(issue is { Severity: IssueSeverity.Error },
            $"expected an error on '{field}', got: " +
            (editor.Issues.Count == 0 ? "nothing" : string.Join(", ", editor.Issues)));
    }

    [Fact]
    public void EveryEditor_StartsInvalidAndEmpty()
    {
        foreach (var editor in MainViewModel.CreateEditors())
        {
            Assert.True(editor.HasErrors, $"{editor.Title} reported no problem while empty");
            Assert.True(string.IsNullOrEmpty(editor.Payload), $"{editor.Title} built a payload while empty");
        }
    }

    /// <summary>
    /// Every issue has to name a field that exists on its editor, or the UI will show a
    /// red box for a problem and no message, or a message attached to nothing.
    /// </summary>
    [Fact]
    public void EveryIssue_NamesARealFieldOrTheForm()
    {
        foreach (var editor in MainViewModel.CreateEditors())
        {
            var properties = editor.GetType().GetProperties().Select(p => p.Name).ToHashSet();

            foreach (var issue in editor.Issues)
            {
                Assert.True(issue.Field == FieldIssue.Form || properties.Contains(issue.Field),
                    $"{editor.Title} reported an issue on '{issue.Field}', which is not one of its fields");
                Assert.False(string.IsNullOrWhiteSpace(issue.Message));
            }
        }
    }

    [Fact]
    public void PlainText_NeedsText()
    {
        var editor = new PlainTextEditor();
        AssertError(editor, nameof(PlainTextEditor.Text));

        editor.Text = "anything";
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Link_NeedsAUsableAddress()
    {
        var editor = new LinkEditor();
        AssertError(editor, nameof(LinkEditor.Address));

        editor.Address = "not a website";
        AssertError(editor, nameof(LinkEditor.Address));

        editor.Address = "example.org/tickets";
        Assert.False(editor.HasErrors);
        Assert.Contains("https://example.org", editor.Note!);
    }

    [Fact]
    public void Wifi_NeedsANameAndAPasswordWhenSecured()
    {
        var editor = new WifiEditor();
        AssertError(editor, nameof(WifiEditor.Ssid));

        editor.Ssid = "Guest";
        AssertError(editor, nameof(WifiEditor.Password));

        editor.Password = "short";
        AssertError(editor, nameof(WifiEditor.Password));

        editor.Password = "longenough";
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Wifi_WantsNoPasswordOnAnOpenNetwork()
    {
        var editor = new WifiEditor { Ssid = "Guest", Security = WifiSecurity.None };
        Assert.False(editor.HasErrors);
    }

    /// <summary>A key we merely find surprising is a note, not a blocked save.</summary>
    [Fact]
    public void Wifi_OnlyWarnsAboutAnOddWepKey()
    {
        var editor = new WifiEditor { Ssid = "Guest", Security = WifiSecurity.Wep, Password = "abcdefg" };

        Assert.False(editor.HasErrors);
        Assert.Equal(IssueSeverity.Warning, Issue(editor, nameof(WifiEditor.Password))!.Severity);
    }

    [Fact]
    public void Email_NeedsAPlausibleAddress()
    {
        var editor = new EmailEditor();
        AssertError(editor, nameof(EmailEditor.Address));

        editor.Address = "someone";
        AssertError(editor, nameof(EmailEditor.Address));

        editor.Address = "someone@example.org";
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Phone_NeedsADiallableNumber()
    {
        var editor = new PhoneEditor();
        AssertError(editor, nameof(PhoneEditor.Number));

        editor.Number = "212 555 CALL";
        AssertError(editor, nameof(PhoneEditor.Number));

        editor.Number = "212 555 0134";
        Assert.False(editor.HasErrors);
        Assert.Contains("+1212", editor.Note!);
    }

    [Fact]
    public void Sms_NeedsANumberButNotAMessage()
    {
        var editor = new SmsEditor();
        AssertError(editor, nameof(SmsEditor.Number));

        editor.Number = "212 555 0134";
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Contact_NeedsAName()
    {
        var editor = new ContactEditor();
        AssertError(editor, FieldIssue.Form);

        // Details with nobody attached to them: the old rule accepted this and produced a
        // card a phone would offer to save as a contact with no name on it.
        editor.JobTitle = "Director of Music";
        editor.Phone = "212 555 0134";
        AssertError(editor, FieldIssue.Form);

        editor.Organization = "Example Choir";
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Contact_AcceptsAFirstNameAlone()
    {
        var editor = new ContactEditor { FirstName = "Alex" };
        Assert.False(editor.HasErrors);
    }

    [Fact]
    public void Contact_ChecksTheDetailsItWasGiven()
    {
        var editor = new ContactEditor
        {
            FirstName = "Alex",
            Email = "not-an-address",
            Phone = "abc",
            Website = "not a website",
        };

        AssertError(editor, nameof(ContactEditor.Email));
        AssertError(editor, nameof(ContactEditor.Phone));
        AssertError(editor, nameof(ContactEditor.Website));
    }

    [Fact]
    public void Contact_LeavesBlankDetailsAlone()
    {
        var editor = new ContactEditor { FirstName = "Alex" };

        Assert.Empty(editor.Issues);
    }

    /// <summary>
    /// Issues are recomputed after a change rather than cached from construction, which is
    /// the whole basis of the highlighting updating as you type.
    /// </summary>
    [Fact]
    public void Issues_FollowEditsInBothDirections()
    {
        var editor = new EmailEditor();
        Assert.True(editor.HasErrors);

        editor.Address = "someone@example.org";
        Assert.False(editor.HasErrors);

        editor.Address = "someone@";
        Assert.True(editor.HasErrors);
    }
}
