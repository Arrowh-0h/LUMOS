using Lumos.Core.Entries;
using Lumos.Core.Totp;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// The form behind the "+ Add entry" overlay. v1 only supports Login;
/// type picker and the other forms come in a later slice.
/// </summary>
public sealed class AddEntryViewModel : ObservableObject
{
    private readonly EntryRepository _entries;
    private string _title = "";
    private string _username = "";
    private string _password = "";
    private string _url = "";
    private string _notes = "";
    private string _totpSecret = "";
    private string _errorMessage = "";

    /// <summary>Raised when an entry is successfully created. The string arg is the new entry id.</summary>
    public event EventHandler<string>? EntryAdded;
    public event EventHandler? Cancelled;

    public AddEntryViewModel(EntryRepository entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
    }

    public string Title       { get => _title;       set => SetField(ref _title, value); }
    public string Username    { get => _username;    set => SetField(ref _username, value); }
    public string Password    { get => _password;    set => SetField(ref _password, value); }
    public string Url         { get => _url;         set => SetField(ref _url, value); }
    public string Notes       { get => _notes;       set => SetField(ref _notes, value); }

    /// <summary>
    /// Optional TOTP secret. Accepts either a base32 string or an otpauth://
    /// URI; parsed at save time. Leave blank for a login without 2FA.
    /// </summary>
    public string TotpSecret { get => _totpSecret; set => SetField(ref _totpSecret, value); }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required.";
            return;
        }

        // Parse the TOTP secret if one was provided. Empty = no TOTP.
        string? parsedTotp = null;
        if (!string.IsNullOrWhiteSpace(TotpSecret))
        {
            parsedTotp = TotpGenerator.TryParseSecret(TotpSecret);
            if (parsedTotp is null)
            {
                ErrorMessage = "TOTP must be a valid base32 secret or otpauth:// URI.";
                return;
            }
        }

        try
        {
            var entry = Entry.NewLogin(
                Title.Trim(),
                new LoginPayload(
                    Username: Username ?? "",
                    Password: Password ?? "",
                    Url: Url ?? "",
                    TotpSecret: parsedTotp));
            // Notes is a common-field, not part of the LoginPayload.
            entry = entry with { Notes = Notes ?? "" };

            var inserted = _entries.Insert(entry);
            EntryAdded?.Invoke(this, inserted.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
