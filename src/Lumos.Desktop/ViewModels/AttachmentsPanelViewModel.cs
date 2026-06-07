using System.Collections.ObjectModel;
using System.IO;
using Lumos.Core.Attachments;
using Lumos.Desktop.Common;
using Lumos.Desktop.Platform;

namespace Lumos.Desktop.ViewModels;

/// <summary>
/// Drives the attachments section of the entry detail pane. Bound to a single
/// entry at a time (set via <see cref="SetEntry"/>); shows that entry's
/// attachments and supports add / save-out / delete.
///
/// Bytes are only ever loaded on demand (preview, save-out). The list itself
/// holds metadata only.
/// </summary>
public sealed class AttachmentsPanelViewModel : ObservableObject
{
    private readonly AttachmentRepository _attachments;
    private readonly IFileDialogService _dialogs;

    private string? _entryId;
    private string _statusMessage = "";
    private string _errorMessage = "";
    private AttachmentItemViewModel? _selected;
    private byte[]? _previewBytes;

    public ObservableCollection<AttachmentItemViewModel> Items { get; } = new();

    public AttachmentsPanelViewModel(AttachmentRepository attachments, IFileDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(dialogs);
        _attachments = attachments;
        _dialogs = dialogs;

        AddCommand = new RelayCommand(AddAttachment, () => _entryId is not null);
        SaveSelectedCommand = new RelayCommand(SaveSelected, () => Selected is not null);
        DeleteSelectedCommand = new RelayCommand(DeleteSelected, () => Selected is not null);
        SelectRowCommand = new RelayCommand<AttachmentItemViewModel?>(row => Selected = row);
    }

    public RelayCommand AddCommand { get; }
    public RelayCommand SaveSelectedCommand { get; }
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand<AttachmentItemViewModel?> SelectRowCommand { get; }

    public bool HasAttachments => Items.Count > 0;
    public int Count => Items.Count;

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string ErrorMessage  { get => _errorMessage;  private set => SetField(ref _errorMessage, value); }

    public AttachmentItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                LoadPreview();
                SaveSelectedCommand.RaiseCanExecuteChanged();
                DeleteSelectedCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(PreviewIsImage));
            }
        }
    }

    public bool HasSelection => Selected is not null;
    public bool PreviewIsImage => Selected?.IsImage == true && _previewBytes is not null;

    /// <summary>Raw bytes of the selected image, for the inline preview (null if not an image).</summary>
    public byte[]? PreviewBytes => PreviewIsImage ? _previewBytes : null;

    /// <summary>
    /// Point the panel at a different entry (or null to clear). Reloads the list.
    /// </summary>
    public void SetEntry(string? entryId)
    {
        _entryId = entryId;
        Selected = null;
        StatusMessage = "";
        ErrorMessage = "";
        Reload();
        AddCommand.RaiseCanExecuteChanged();
    }

    private void Reload()
    {
        Items.Clear();
        if (_entryId is not null)
        {
            foreach (var info in _attachments.ListForEntry(_entryId))
                Items.Add(new AttachmentItemViewModel(info));
        }
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(Count));
    }

    private void AddAttachment()
    {
        if (_entryId is null) return;
        ErrorMessage = "";
        StatusMessage = "";

        var filters = new[] { new FileFilter("All files (*.*)", "*.*") };
        var path = _dialogs.ShowOpenDialog("Attach a file", filters);
        if (path is null) return;

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.LongLength > AttachmentRepository.MaxFileSizeBytes)
            {
                ErrorMessage = $"That file is {AttachmentInfo.FormatSize(bytes.LongLength)}. " +
                               $"The limit is {AttachmentInfo.FormatSize(AttachmentRepository.MaxFileSizeBytes)}.";
                return;
            }

            var fileName = Path.GetFileName(path);
            var mime = GuessMimeType(fileName);
            var info = _attachments.Add(_entryId, fileName, mime, bytes);
            Items.Add(new AttachmentItemViewModel(info));
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(Count));
            StatusMessage = $"Attached {fileName} ({info.SizeDisplay}).";
        }
        catch (AttachmentTooLargeException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't attach the file: {ex.Message}";
        }
    }

    private void SaveSelected()
    {
        if (Selected is null) return;
        ErrorMessage = "";
        StatusMessage = "";

        var content = _attachments.GetContent(Selected.Id);
        if (content is null)
        {
            ErrorMessage = "That attachment is no longer available.";
            return;
        }

        var ext = Path.GetExtension(Selected.FileName);
        var filterLabel = string.IsNullOrEmpty(ext)
            ? "All files (*.*)"
            : $"{ext.TrimStart('.').ToUpperInvariant()} file (*{ext})";
        var pattern = string.IsNullOrEmpty(ext) ? "*.*" : "*" + ext;

        var path = _dialogs.ShowSaveDialog("Save attachment as",
            Selected.FileName,
            new[] { new FileFilter(filterLabel, pattern), new FileFilter("All files (*.*)", "*.*") });
        if (path is null) return;

        try
        {
            File.WriteAllBytes(path, content.Bytes);
            StatusMessage = $"Saved to {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't save the file: {ex.Message}";
        }
    }

    private void DeleteSelected()
    {
        if (Selected is null) return;
        var id = Selected.Id;
        var name = Selected.FileName;
        ErrorMessage = "";
        try
        {
            if (_attachments.Delete(id))
            {
                var row = Items.FirstOrDefault(i => i.Id == id);
                if (row is not null) Items.Remove(row);
                Selected = null;
                OnPropertyChanged(nameof(HasAttachments));
                OnPropertyChanged(nameof(Count));
                StatusMessage = $"Removed {name}.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't remove the attachment: {ex.Message}";
        }
    }

    private void LoadPreview()
    {
        _previewBytes = null;
        if (Selected is { IsImage: true })
        {
            try
            {
                var content = _attachments.GetContent(Selected.Id);
                _previewBytes = content?.Bytes;
            }
            catch
            {
                _previewBytes = null;
            }
        }
        OnPropertyChanged(nameof(PreviewBytes));
        OnPropertyChanged(nameof(PreviewIsImage));
    }

    /// <summary>Very small MIME guesser by extension — just enough for preview/round-trip.</summary>
    private static string GuessMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
    }
}
