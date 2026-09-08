using TrispotQR.Core.Payloads;

namespace TrispotQR.ViewModels;

/// <summary>
/// One way of entering content. Each editor collects whatever fields its format needs and
/// exposes the single string that actually gets encoded.
///
/// The point of these is that a QR code is only ever a string, and a beginner has no way
/// to know that a phone needs "https://" to treat one as a link, or the exact punctuation
/// of a Wi-Fi payload. The editors know, so the user does not have to.
///
/// Each also knows what counts as complete. <see cref="Issues"/> reports problems against
/// the field they belong to, which is what lets the UI put a red border around the box at
/// fault instead of a single line at the bottom that says something is wrong somewhere.
/// </summary>
public abstract class ContentEditor : ObservableObject
{
    private IReadOnlyList<FieldIssue>? _issues;

    /// <summary>Name shown in the content type dropdown.</summary>
    public abstract string Title { get; }

    /// <summary>One line explaining what this type is for, shown under the dropdown.</summary>
    public abstract string Hint { get; }

    /// <summary>The string that gets encoded into the code.</summary>
    public abstract string Payload { get; }

    /// <summary>
    /// Something worth mentioning that is not a problem, such as the https:// that was
    /// added for the user. Shown muted, under the fields.
    /// </summary>
    public virtual string? Note => null;

    /// <summary>
    /// Everything currently wrong with the input, recomputed after each edit.
    ///
    /// Cached between edits because several field controls read it on every keystroke and
    /// some rules parse URLs, which is not free.
    /// </summary>
    public IReadOnlyList<FieldIssue> Issues => _issues ??= [.. Validate()];

    /// <summary>True when something is wrong badly enough to block saving.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>
    /// The line shown under the fields: a problem belonging to the form as a whole, or
    /// failing that the informational note. Problems belonging to a single field appear
    /// under that field instead.
    /// </summary>
    public string? FormMessage =>
        Issues.FirstOrDefault(i => i.Field == FieldIssue.Form)?.Message ?? Note;

    /// <summary>True when <see cref="FormMessage"/> is a problem rather than a note.</summary>
    public bool FormMessageIsProblem => Issues.Any(i => i.Field == FieldIssue.Form);

    /// <summary>Raised when any field changes, so the preview can be rebuilt.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>The problems with the current input. Empty means ready to save.</summary>
    protected abstract IEnumerable<FieldIssue> Validate();

    /// <summary>The message for one field, or null when that field is fine.</summary>
    public FieldIssue? IssueFor(string field) => Issues.FirstOrDefault(i => i.Field == field);

    /// <summary>Called by each editor after a field changes.</summary>
    protected void NotifyContentChanged()
    {
        _issues = null;

        OnPropertyChanged(nameof(Payload));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(FormMessage));
        OnPropertyChanged(nameof(FormMessageIsProblem));
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Yields an error for <paramref name="field"/> when <paramref name="message"/> is not null.</summary>
    protected static IEnumerable<FieldIssue> ErrorIf(string field, string? message) =>
        message is null ? [] : [FieldIssue.Error(field, message)];

    /// <summary>Yields a warning for <paramref name="field"/> when <paramref name="message"/> is not null.</summary>
    protected static IEnumerable<FieldIssue> WarnIf(string field, string? message) =>
        message is null ? [] : [FieldIssue.Warning(field, message)];
}

/// <summary>Anything at all. The default, and the one that imposes nothing.</summary>
public sealed class PlainTextEditor : ContentEditor
{
    private string _text = string.Empty;

    public override string Title => "Plain text";

    public override string Hint => "Any text at all. Scanners will show it as written.";

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public override string Payload => _text;

    protected override IEnumerable<FieldIssue> Validate() =>
        ErrorIf(nameof(Text), FieldRules.Required(_text, "the text to put in the code"));
}

/// <summary>A web address, with the https:// added for the user and a sanity check on the rest.</summary>
public sealed class LinkEditor : ContentEditor
{
    private string _address = string.Empty;

    public override string Title => "Link";

    public override string Hint => "A website. If you leave off https:// it will be added for you.";

    public string Address
    {
        get => _address;
        set
        {
            if (SetField(ref _address, value))
            {
                NotifyContentChanged();
            }
        }
    }

    private UrlCheck Check => PayloadBuilder.CheckUrl(_address);

    public override string Payload => Check.Payload;

    // Only ever the "saved as https://..." confirmation. When the address is unusable the
    // same text comes back through Validate as an error instead.
    public override string? Note => Check.IsValid ? Check.Message : null;

    protected override IEnumerable<FieldIssue> Validate()
    {
        if (FieldRules.Required(_address, "a web address, for example example.org/tickets") is { } missing)
        {
            return [FieldIssue.Error(nameof(Address), missing)];
        }

        var check = Check;
        return check.IsValid ? [] : [FieldIssue.Error(nameof(Address), check.Message!)];
    }
}

/// <summary>Wi-Fi joining details.</summary>
public sealed class WifiEditor : ContentEditor
{
    private string _ssid = string.Empty;
    private string _password = string.Empty;
    private WifiSecurity _security = WifiSecurity.Wpa;
    private bool _hidden;

    public override string Title => "Wi-Fi";

    public override string Hint => "Scanning this joins the network. Nothing is sent anywhere.";

    public string Ssid
    {
        get => _ssid;
        set
        {
            if (SetField(ref _ssid, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetField(ref _password, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public WifiSecurity Security
    {
        get => _security;
        set
        {
            if (SetField(ref _security, value))
            {
                OnPropertyChanged(nameof(NeedsPassword));
                NotifyContentChanged();
            }
        }
    }

    public bool Hidden
    {
        get => _hidden;
        set
        {
            if (SetField(ref _hidden, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public bool NeedsPassword => _security != WifiSecurity.None;

    public IReadOnlyList<WifiSecurity> SecurityOptions { get; } =
        [WifiSecurity.Wpa, WifiSecurity.Wep, WifiSecurity.None];

    public override string Payload =>
        _ssid.Length == 0 ? string.Empty : PayloadBuilder.Wifi(_ssid, _password, _security, _hidden);

    protected override IEnumerable<FieldIssue> Validate()
    {
        var issues = new List<FieldIssue>();

        issues.AddRange(ErrorIf(nameof(Ssid), FieldRules.Required(_ssid, "the network name")));
        issues.AddRange(ErrorIf(nameof(Ssid), FieldRules.Ssid(_ssid)));
        issues.AddRange(ErrorIf(nameof(Password), FieldRules.WifiPassword(_password, _security)));

        if (_security == WifiSecurity.Wep)
        {
            issues.AddRange(WarnIf(nameof(Password), FieldRules.WepKeyNote(_password)));
        }

        return issues;
    }
}

/// <summary>An email address, optionally with a prepared subject and body.</summary>
public sealed class EmailEditor : ContentEditor
{
    private string _address = string.Empty;
    private string _subject = string.Empty;
    private string _body = string.Empty;

    public override string Title => "Email";

    public override string Hint => "Scanning opens a new email, already addressed.";

    public string Address
    {
        get => _address;
        set
        {
            if (SetField(ref _address, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Subject
    {
        get => _subject;
        set
        {
            if (SetField(ref _subject, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (SetField(ref _body, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public override string Payload =>
        _address.Length == 0 ? string.Empty : PayloadBuilder.Email(_address, _subject, _body);

    protected override IEnumerable<FieldIssue> Validate() =>
        ErrorIf(nameof(Address), FieldRules.Required(_address, "an email address"))
            .Concat(ErrorIf(nameof(Address), FieldRules.Email(_address)));
}

/// <summary>A phone number to dial.</summary>
public sealed class PhoneEditor : ContentEditor
{
    private string _number = string.Empty;

    public override string Title => "Phone";

    public override string Hint => "Scanning starts a call to this number.";

    public string Number
    {
        get => _number;
        set
        {
            if (SetField(ref _number, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public override string Payload => _number.Length == 0 ? string.Empty : PayloadBuilder.Phone(_number);

    // Shown only once the number is usable, because confirming how we saved something we
    // have just called invalid would contradict the error sitting above it.
    public override string? Note => HasErrors ? null : $"Will be saved as {Payload[4..]}.";

    protected override IEnumerable<FieldIssue> Validate() =>
        ErrorIf(nameof(Number), FieldRules.Required(_number, "a phone number"))
            .Concat(ErrorIf(nameof(Number), FieldRules.Phone(_number)));
}

/// <summary>A prepared text message.</summary>
public sealed class SmsEditor : ContentEditor
{
    private string _number = string.Empty;
    private string _message = string.Empty;

    public override string Title => "Text message";

    public override string Hint => "Scanning opens a text message, ready to send.";

    public string Number
    {
        get => _number;
        set
        {
            if (SetField(ref _number, value))
            {
                NotifyContentChanged();
            }
        }
    }

    /// <summary>The message body. Named Text rather than Message to leave the base
    /// class's naming free.</summary>
    public string Text
    {
        get => _message;
        set
        {
            if (SetField(ref _message, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public override string Payload => _number.Length == 0 ? string.Empty : PayloadBuilder.Sms(_number, _message);

    // The message body stays optional: a code that just opens a conversation with the
    // right number is a perfectly reasonable thing to want.
    protected override IEnumerable<FieldIssue> Validate() =>
        ErrorIf(nameof(Number), FieldRules.Required(_number, "a phone number"))
            .Concat(ErrorIf(nameof(Number), FieldRules.Phone(_number)));
}

/// <summary>Contact details, encoded as a vCard the phone can save.</summary>
public sealed class ContactEditor : ContentEditor
{
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _organization = string.Empty;
    private string _jobTitle = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _website = string.Empty;

    public override string Title => "Contact card";

    public override string Hint => "Scanning offers to save these details as a contact.";

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetField(ref _firstName, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetField(ref _lastName, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Organization
    {
        get => _organization;
        set
        {
            if (SetField(ref _organization, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string JobTitle
    {
        get => _jobTitle;
        set
        {
            if (SetField(ref _jobTitle, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Phone
    {
        get => _phone;
        set
        {
            if (SetField(ref _phone, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (SetField(ref _email, value))
            {
                NotifyContentChanged();
            }
        }
    }

    public string Website
    {
        get => _website;
        set
        {
            if (SetField(ref _website, value))
            {
                NotifyContentChanged();
            }
        }
    }

    /// <summary>
    /// Whether the card names anybody. A card carrying a phone number and a job title but
    /// no name imports as a contact with nothing in the name field, which is worse than
    /// useless because it looks like it worked.
    /// </summary>
    private bool HasIdentity =>
        !string.IsNullOrWhiteSpace(_firstName)
        || !string.IsNullOrWhiteSpace(_lastName)
        || !string.IsNullOrWhiteSpace(_organization);

    public override string Payload
    {
        get
        {
            var card = new ContactCard
            {
                FirstName = _firstName,
                LastName = _lastName,
                Organization = _organization,
                Title = _jobTitle,
                Phone = _phone,
                Email = _email,
                Website = _website,
            };

            // An empty card is still four lines of vCard boilerplate, which would encode
            // into a perfectly scannable but completely useless code. The preview follows
            // any detail at all, so it fills in as the user types; whether the card is
            // complete enough to save is Validate's business.
            var hasAnything = new[] { _firstName, _lastName, _organization, _jobTitle, _phone, _email, _website }
                .Any(f => !string.IsNullOrWhiteSpace(f));

            return hasAnything ? PayloadBuilder.VCard(card) : string.Empty;
        }
    }

    protected override IEnumerable<FieldIssue> Validate()
    {
        var issues = new List<FieldIssue>();

        // Against the form rather than a field: three boxes any one of which would satisfy
        // the rule should not all turn red.
        if (!HasIdentity)
        {
            issues.Add(FieldIssue.Error(
                FieldIssue.Form, "Enter a first name, last name or organisation so the contact has a name."));
        }

        issues.AddRange(ErrorIf(nameof(Email), FieldRules.Email(_email)));
        issues.AddRange(ErrorIf(nameof(Phone), FieldRules.Phone(_phone)));
        issues.AddRange(ErrorIf(nameof(Website), FieldRules.Website(_website)));

        return issues;
    }
}
