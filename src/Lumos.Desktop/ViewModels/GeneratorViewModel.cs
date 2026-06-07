using Lumos.Core.Generator;
using Lumos.Desktop.Common;

namespace Lumos.Desktop.ViewModels;

public enum GeneratorMode
{
    Passphrase,
    Random,
}

/// <summary>
/// The password generator pane. Two modes, lots of options, live regenerate
/// on any change. Generation is fast enough (a few ms) that we don't bother
/// debouncing — every property change triggers an immediate regenerate.
/// </summary>
public sealed class GeneratorViewModel : ObservableObject
{
    private GeneratorMode _mode = GeneratorMode.Passphrase;
    private string _result = "";
    private string _statusMessage = "";

    // Random options
    private int _randomLength = 24;
    private bool _includeUppercase = true;
    private bool _includeLowercase = true;
    private bool _includeDigits = true;
    private bool _includeSymbols = true;
    private bool _excludeAmbiguous = false;

    // Passphrase options
    private int _wordCount = 5;
    private string _separator = "-";
    private bool _capitalizeWords = false;
    private bool _appendDigits = false;

    public GeneratorViewModel()
    {
        CopyCommand = new RelayCommand(Copy, () => !string.IsNullOrEmpty(Result));
        RegenerateCommand = new RelayCommand(Regenerate);
        SwitchToPassphraseCommand = new RelayCommand(() => Mode = GeneratorMode.Passphrase);
        SwitchToRandomCommand = new RelayCommand(() => Mode = GeneratorMode.Random);
        Regenerate();
    }

    public GeneratorMode Mode
    {
        get => _mode;
        set
        {
            if (SetField(ref _mode, value))
            {
                OnPropertyChanged(nameof(IsPassphraseMode));
                OnPropertyChanged(nameof(IsRandomMode));
                Regenerate();
            }
        }
    }

    public bool IsPassphraseMode => Mode == GeneratorMode.Passphrase;
    public bool IsRandomMode => Mode == GeneratorMode.Random;

    public string Result
    {
        get => _result;
        private set
        {
            if (SetField(ref _result, value))
                CopyCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    // ----- Random options (notify and regenerate) -----

    public int RandomLength
    {
        get => _randomLength;
        set
        {
            // Clamp to the validator range. Slider can land out-of-bounds during drag.
            var clamped = Math.Clamp(value, 8, 64);
            if (SetField(ref _randomLength, clamped)) Regenerate();
        }
    }

    public bool IncludeUppercase { get => _includeUppercase; set { if (SetField(ref _includeUppercase, value)) Regenerate(); } }
    public bool IncludeLowercase { get => _includeLowercase; set { if (SetField(ref _includeLowercase, value)) Regenerate(); } }
    public bool IncludeDigits    { get => _includeDigits;    set { if (SetField(ref _includeDigits, value))    Regenerate(); } }
    public bool IncludeSymbols   { get => _includeSymbols;   set { if (SetField(ref _includeSymbols, value))   Regenerate(); } }
    public bool ExcludeAmbiguous { get => _excludeAmbiguous; set { if (SetField(ref _excludeAmbiguous, value)) Regenerate(); } }

    // ----- Passphrase options -----

    public int WordCount
    {
        get => _wordCount;
        set
        {
            var clamped = Math.Clamp(value, 3, 10);
            if (SetField(ref _wordCount, clamped)) Regenerate();
        }
    }

    public string Separator
    {
        get => _separator;
        set
        {
            if (SetField(ref _separator, value))
            {
                OnPropertyChanged(nameof(IsSeparatorSpace));
                Regenerate();
            }
        }
    }

    /// <summary>
    /// True when the current separator is a single space. We expose this as
    /// a discrete bool so the "space" radio button can two-way-bind to it
    /// without needing a XAML literal-space ConverterParameter, which has
    /// flaky support in WPF's XAML reader.
    /// </summary>
    public bool IsSeparatorSpace
    {
        get => _separator == " ";
        set
        {
            // Two-way: when the radio gets selected, switch the separator
            // to a space. When it gets deselected (because another radio was
            // selected), do nothing — the other radio's setter handles it.
            if (value && _separator != " ")
                Separator = " ";
        }
    }

    public bool CapitalizeWords { get => _capitalizeWords; set { if (SetField(ref _capitalizeWords, value)) Regenerate(); } }
    public bool AppendDigits    { get => _appendDigits;    set { if (SetField(ref _appendDigits, value)) Regenerate(); } }

    public RelayCommand CopyCommand { get; }
    public RelayCommand RegenerateCommand { get; }
    public RelayCommand SwitchToPassphraseCommand { get; }
    public RelayCommand SwitchToRandomCommand { get; }

    private void Regenerate()
    {
        try
        {
            if (Mode == GeneratorMode.Passphrase)
            {
                Result = PasswordGenerator.GeneratePassphrase(new PassphraseOptions
                {
                    WordCount = WordCount,
                    Separator = Separator,
                    CapitalizeWords = CapitalizeWords,
                    AppendDigits = AppendDigits,
                });
            }
            else
            {
                // If the user just toggled off every class, GenerateRandom would
                // throw. Catch that and show a quiet status instead of crashing.
                if (!(IncludeUppercase || IncludeLowercase || IncludeDigits || IncludeSymbols))
                {
                    Result = "";
                    StatusMessage = "Enable at least one character class.";
                    return;
                }

                Result = PasswordGenerator.GenerateRandom(new RandomPasswordOptions
                {
                    Length = RandomLength,
                    IncludeUppercase = IncludeUppercase,
                    IncludeLowercase = IncludeLowercase,
                    IncludeDigits = IncludeDigits,
                    IncludeSymbols = IncludeSymbols,
                    ExcludeAmbiguous = ExcludeAmbiguous,
                });
            }
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            Result = "";
            StatusMessage = ex.Message;
        }
    }

    private void Copy()
    {
        if (string.IsNullOrEmpty(Result)) return;
        AppServices.Clipboard.SetTextWithAutoClear(Result);
        ShowStatusBriefly($"Copied. Clipboard clears in {AppServices.Clipboard.DefaultClearTimeout.TotalSeconds:0}s.");
    }

    private void ShowStatusBriefly(string message)
    {
        StatusMessage = message;
        var captured = message;
        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (StatusMessage == captured) StatusMessage = "";
            });
        });
    }
}
