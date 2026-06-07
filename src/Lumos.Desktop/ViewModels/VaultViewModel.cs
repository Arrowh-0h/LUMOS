using System.Collections.ObjectModel;
using Lumos.Core.Attachments;
using Lumos.Core.Entries;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// The page-level VM for the unlocked vault. Owns:
///   - The visible entry list
///   - The search text (with debounce)
///   - The currently-selected entry
///   - The detail VM
///   - The "is the add overlay open" state and the add VM
///
/// v2: fully offline. No breach scanner, no network.
/// </summary>
public sealed class VaultViewModel : ObservableObject, IDisposable
{
    private readonly EntryRepository _entries;
    private CancellationTokenSource? _searchDebounceCts;

    private string _searchText = "";
    private EntryListItemViewModel? _selectedItem;
    private bool _isAddOverlayOpen;
    private AddEntryViewModel? _addEntry;

    public ObservableCollection<EntryListItemViewModel> Items { get; } = new();

    public EntryDetailViewModel Detail { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                ScheduleSearchRefresh();
        }
    }

    public EntryListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                Detail.Entry = value is null ? null : _entries.GetById(value.Id);
            }
        }
    }

    public bool IsEmpty => Items.Count == 0 && string.IsNullOrEmpty(SearchText);
    public bool HasNoSearchResults => Items.Count == 0 && !string.IsNullOrEmpty(SearchText);

    public bool IsAddOverlayOpen
    {
        get => _isAddOverlayOpen;
        private set => SetField(ref _isAddOverlayOpen, value);
    }

    public AddEntryViewModel? AddEntry
    {
        get => _addEntry;
        private set => SetField(ref _addEntry, value);
    }

    public RelayCommand OpenAddOverlayCommand { get; }
    public RelayCommand CloseAddOverlayCommand { get; }

    public VaultViewModel(EntryRepository entries,
                          AttachmentRepository attachments,
                          IFileDialogService fileDialogs)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(fileDialogs);
        _entries = entries;
        Detail = new EntryDetailViewModel(entries, attachments, fileDialogs);

        // Refresh the list whenever the detail VM mutates the data.
        Detail.EntryChanged += (_, _) => RefreshList();
        Detail.EntryDeleted += (_, _) => { SelectedItem = null; RefreshList(); };

        OpenAddOverlayCommand = new RelayCommand(OpenAddOverlay);
        CloseAddOverlayCommand = new RelayCommand(CloseAddOverlay);

        RefreshList();
    }

    private void OpenAddOverlay()
    {
        AddEntry = new AddEntryViewModel(_entries);
        AddEntry.EntryAdded += OnEntryAdded;
        AddEntry.Cancelled += (_, _) => CloseAddOverlay();
        IsAddOverlayOpen = true;
    }

    private void CloseAddOverlay()
    {
        if (AddEntry is not null)
        {
            AddEntry.EntryAdded -= OnEntryAdded;
        }
        AddEntry = null;
        IsAddOverlayOpen = false;
    }

    private void OnEntryAdded(object? sender, string newEntryId)
    {
        CloseAddOverlay();
        RefreshList();
        var newItem = Items.FirstOrDefault(i => i.Id == newEntryId);
        if (newItem is not null) SelectedItem = newItem;
    }

    private void ScheduleSearchRefresh()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(150, token); }
            catch (OperationCanceledException) { return; }

            if (token.IsCancellationRequested) return;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                RefreshList();
            });
        }, token);
    }

    /// <summary>
    /// Reload the list from the repository, applying the current search.
    /// Preserves the selected item by id if it's still in the list.
    /// </summary>
    private void RefreshList()
    {
        var previouslySelectedId = SelectedItem?.Id;

        IReadOnlyList<Entry> source = string.IsNullOrWhiteSpace(SearchText)
            ? _entries.ListAll()
            : _entries.Search(SearchText);

        Items.Clear();
        foreach (var entry in source)
            Items.Add(new EntryListItemViewModel(entry));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoSearchResults));

        if (previouslySelectedId is not null)
            SelectedItem = Items.FirstOrDefault(i => i.Id == previouslySelectedId);
    }

    public void Dispose()
    {
        try { _searchDebounceCts?.Cancel(); } catch { }
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = null;
    }
}
