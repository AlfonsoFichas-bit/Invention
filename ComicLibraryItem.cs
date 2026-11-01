using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Invention;

public sealed class ComicLibraryItem : INotifyPropertyChanged
{
    private ImageSource? _coverImage;
    private bool _isAvailable = true;

    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public long AddedUtc { get; set; }
    public long? LastOpenedUtc { get; set; }
    public int LastReadPage { get; set; }

    public ImageSource? CoverImage
    {
        get => _coverImage;
        set
        {
            if (_coverImage == value)
            {
                return;
            }

            _coverImage = value;
            OnPropertyChanged();
        }
    }

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (_isAvailable == value)
            {
                return;
            }

            _isAvailable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DetailText));
        }
    }

    public string DetailText => !IsAvailable
        ? "Archivo no disponible"
        : LastOpenedUtc is long lastOpened
            ? $"Leído {DateTimeOffset.FromUnixTimeMilliseconds(lastOpened).ToLocalTime():g}"
            : $"Añadido {DateTimeOffset.FromUnixTimeMilliseconds(AddedUtc).ToLocalTime():d}";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
