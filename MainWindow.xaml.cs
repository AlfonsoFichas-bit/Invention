using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Invention
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const uint SpiGetDesktopWallpaper = 0x0073;
        private const int DesktopWallpaperPathCapacity = 32768;

        private readonly List<ComicPage> _pages = new();
        private readonly ComicLibraryRepository _libraryRepository;
        private readonly DataTransferManager _dataTransferManager;
        private readonly AppWindow _appWindow;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _statusTimer;
        private StorageFile? _comicFile;
        private int _currentPageIndex;
        private float _zoomFactor = 1f;
        private bool _isInitialized;
        private bool _isSyncingZoom;
        private string _desktopWallpaperPath = string.Empty;

        public ObservableCollection<ComicLibraryItem> LibraryComics { get; } = new();
        public ObservableCollection<ComicLibraryItem> FilteredLibraryComics { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            _isInitialized = true;
            _libraryRepository = new ComicLibraryRepository();
            Title = "Invention Comics";
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarArea);

            var windowHandle = WindowNative.GetWindowHandle(this);
            _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            }

            RootGrid.Loaded += RootGrid_Loaded;
            RootGrid.SizeChanged += RootGrid_SizeChanged;
            RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
            _dataTransferManager = DataTransferManagerInterop.GetForWindow(windowHandle);
            _dataTransferManager.DataRequested += DataTransferManager_DataRequested;
            _statusTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(30);
            _statusTimer.IsRepeating = true;
            _statusTimer.Tick += StatusTimer_Tick;
            UpdateDeviceStatus();
            _statusTimer.Start();
            RootGrid.Focus(FocusState.Programmatic);
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateTitleBarInsets();
            await UpdateLogoThemeAsync();
            await UpdateDesktopWallpaperAsync();
            await RefreshLibraryAsync();
        }

        private async void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
        {
            await UpdateLogoThemeAsync();
        }

        private async Task UpdateLogoThemeAsync()
        {
            var isDarkTheme = RootGrid.ActualTheme == ElementTheme.Dark;
            var logoFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///logo.svg"));
            var svg = await FileIO.ReadTextAsync(logoFile);
            var fill = isDarkTheme ? "#FFFFFF" : "#000000";
            svg = svg.Replace(".cls-1{fill:#000000}", $".cls-1{{fill:{fill}}}", StringComparison.Ordinal);

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(Encoding.UTF8.GetBytes(svg));
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var imageSource = new SvgImageSource();
            await imageSource.SetSourceAsync(stream);
            LogoImage.Source = imageSource;

            var iconFileName = isDarkTheme ? "InventionDark.ico" : "InventionLight.ico";
            var iconFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{iconFileName}"));
            _appWindow.SetIcon(iconFile.Path);
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTitleBarInsets();
        }

        private void UpdateTitleBarInsets()
        {
            if (AppWindowTitleBar.IsCustomizationSupported() && RootGrid.XamlRoot is not null)
            {
                TitleBarRightInset.Width = _appWindow.TitleBar.RightInset / RootGrid.XamlRoot.RasterizationScale;
            }
        }

        private async void StatusTimer_Tick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            UpdateDeviceStatus();
            await UpdateDesktopWallpaperAsync();
        }

        private void UpdateDeviceStatus()
        {
            ClockText.Text = DateTime.Now.ToString("t");
            if (GetSystemPowerStatus(out var status) && status.BatteryFlag != 128 && status.BatteryLifePercent != 255)
            {
                BatteryText.Text = $"{status.BatteryLifePercent}%";
                BatteryPanel.Visibility = Visibility.Visible;
            }
            else
            {
                BatteryPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox.Visibility == Visibility.Collapsed)
            {
                SearchTextBox.Visibility = Visibility.Visible;
                SearchTextBox.Focus(FocusState.Programmatic);
                return;
            }

            SearchTextBox.Text = string.Empty;
            SearchTextBox.Visibility = Visibility.Collapsed;
            SearchButton.Focus(FocusState.Programmatic);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLibraryFilter();
        }

        private void UpdateLibraryFilter()
        {
            var searchText = SearchTextBox.Text.Trim();
            var matchingComics = LibraryComics
                .Where(comic => string.IsNullOrEmpty(searchText)
                    || comic.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            FilteredLibraryComics.Clear();
            foreach (var comic in matchingComics)
            {
                FilteredLibraryComics.Add(comic);
            }

            if (LibraryComics.Count == 0)
            {
                EmptyLibraryState.Text = "Tu biblioteca está vacía. Añade archivos CBR para empezar.";
                EmptyLibraryState.Visibility = Visibility.Visible;
            }
            else if (matchingComics.Count == 0)
            {
                EmptyLibraryState.Text = $"No se encontraron cómics para «{searchText}».";
                EmptyLibraryState.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyLibraryState.Visibility = Visibility.Collapsed;
            }

            LibraryItemsControl.Visibility = matchingComics.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task UpdateDesktopWallpaperAsync()
        {
            var pathBuffer = new StringBuilder(DesktopWallpaperPathCapacity);
            if (!SystemParametersInfo(SpiGetDesktopWallpaper, (uint)pathBuffer.Capacity, pathBuffer, 0))
            {
                return;
            }

            var wallpaperPath = pathBuffer.ToString();
            if (string.Equals(wallpaperPath, _desktopWallpaperPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(wallpaperPath))
            {
                DesktopWallpaperImage.Source = null;
                _desktopWallpaperPath = string.Empty;
                return;
            }

            DesktopWallpaperImage.Source = null;
            try
            {
                var wallpaperFile = await StorageFile.GetFileFromPathAsync(wallpaperPath);
                using var stream = await wallpaperFile.OpenAsync(FileAccessMode.Read);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                DesktopWallpaperImage.Source = bitmap;
                _desktopWallpaperPath = wallpaperPath;
            }
            catch (Exception)
            {
            }
        }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, StringBuilder value, uint flags);

        private void HomeNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (!_isInitialized)
            {
                return;
            }

            if (args.SelectedItemContainer?.Tag?.ToString() == "Library")
            {
                LibraryHeading.StartBringIntoView();
            }
            else
            {
                HomeScrollViewer.ChangeView(0, 0, null, true);
            }
        }

        private async Task OpenComicAsync(StorageFile file, string? accessToken = null, int lastReadPage = 0)
        {
            HomeView.Visibility = Visibility.Collapsed;
            ReaderView.Visibility = Visibility.Visible;
            ReaderFooter.Visibility = Visibility.Visible;
            HomeButton.Visibility = Visibility.Visible;
            HomeStatusPanel.Visibility = Visibility.Collapsed;
            ShareButton.Visibility = Visibility.Visible;
            _pages.Clear();
            _comicFile = null;
            ShareButton.IsEnabled = false;
            _currentPageIndex = 0;
            _zoomFactor = 1f;
            SetZoomSliderValue(100);
            PageImage.Source = null;
            PageScrollViewer.Visibility = Visibility.Collapsed;
            EmptyState.Text = "Abriendo cómic…";
            EmptyState.Visibility = Visibility.Visible;
            UpdateNavigation();
            UpdateZoomControls(false);

            try
            {
                using var archiveStream = await file.OpenStreamForReadAsync();
                ComicTitle.Text = file.Name;
                var pages = await Task.Run(() => ReadPages(archiveStream));
                if (pages.Count == 0)
                {
                    throw new InvalidDataException("El archivo no contiene páginas de imagen compatibles.");
                }

                _pages.AddRange(pages);
                _currentPageIndex = Math.Clamp(lastReadPage, 0, _pages.Count - 1);
                accessToken ??= StorageApplicationPermissions.FutureAccessList.Add(file);
                _libraryRepository.AddOrUpdate(file.Path, file.Name, accessToken);
                _libraryRepository.MarkOpened(file.Path);
                _libraryRepository.UpdateLastReadPage(file.Path, _currentPageIndex);
                _comicFile = file;
                ShareButton.IsEnabled = true;
                await ShowCurrentPageAsync();
                await RefreshLibraryAsync();
            }
            catch (Exception exception)
            {
                _pages.Clear();
                _comicFile = null;
                ShareButton.IsEnabled = false;
                _currentPageIndex = 0;
                PageImage.Source = null;
                PageScrollViewer.Visibility = Visibility.Collapsed;
                EmptyState.Text = $"No se pudo abrir el cómic: {exception.Message}";
                UpdateNavigation();
                UpdateZoomControls(false);
            }
        }

        private async void AddComicsButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".cbr");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var files = await picker.PickMultipleFilesAsync();
            if (files.Count == 0)
            {
                return;
            }

            try
            {
                foreach (var file in files)
                {
                    var accessToken = StorageApplicationPermissions.FutureAccessList.Add(file);
                    _libraryRepository.AddOrUpdate(file.Path, file.Name, accessToken);
                }

                await RefreshLibraryAsync();
            }
            catch (Exception exception)
            {
                LibrarySummary.Text = $"No se pudieron añadir los cómics: {exception.Message}";
            }
        }

        private async void LibraryComic_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: ComicLibraryItem comic } || !comic.IsAvailable)
            {
                return;
            }

            try
            {
                var file = await ResolveComicFileAsync(comic);
                await OpenComicAsync(file, comic.AccessToken, comic.LastReadPage);
            }
            catch
            {
                comic.IsAvailable = false;
            }
        }

        private void RemoveComicMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: long comicId })
            {
                return;
            }

            var comic = LibraryComics.FirstOrDefault(item => item.Id == comicId);
            if (comic is null)
            {
                return;
            }

            try
            {
                _libraryRepository.RemoveById(comic.Id);
            }
            catch (Exception exception)
            {
                LibrarySummary.Text = $"No se pudo quitar el cómic: {exception.Message}";
                return;
            }

            LibraryComics.Remove(comic);
            try
            {
                if (!string.IsNullOrWhiteSpace(comic.AccessToken))
                {
                    StorageApplicationPermissions.FutureAccessList.Remove(comic.AccessToken);
                }
            }
            catch
            {
            }

            var count = LibraryComics.Count;
            LibrarySummary.Text = count == 0
                ? "Tus cómics, siempre a mano"
                : count == 1 ? "1 cómic en tu biblioteca" : $"{count} cómics en tu biblioteca";
            UpdateLibraryFilter();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            HomeView.Visibility = Visibility.Visible;
            ReaderView.Visibility = Visibility.Collapsed;
            ReaderFooter.Visibility = Visibility.Collapsed;
            HomeButton.Visibility = Visibility.Collapsed;
            ShareButton.IsEnabled = false;
            ShareButton.Visibility = Visibility.Collapsed;
            HomeStatusPanel.Visibility = Visibility.Visible;
            ComicTitle.Text = "Invention Comics";
            UpdateDeviceStatus();
            HomeNavigationView.SelectedItem = HomeNavigationView.MenuItems[0];
        }

        private async Task RefreshLibraryAsync()
        {
            IReadOnlyList<ComicLibraryItem> comics;
            try
            {
                comics = _libraryRepository.GetAll();
            }
            catch (Exception exception)
            {
                LibrarySummary.Text = $"No se pudo cargar la biblioteca: {exception.Message}";
                return;
            }

            LibraryComics.Clear();
            foreach (var comic in comics)
            {
                LibraryComics.Add(comic);
            }

            var count = LibraryComics.Count;
            LibrarySummary.Text = count == 0
                ? "Tus cómics, siempre a mano"
                : count == 1 ? "1 cómic en tu biblioteca" : $"{count} cómics en tu biblioteca";
            UpdateLibraryFilter();

            foreach (var comic in LibraryComics)
            {
                await LoadComicCoverAsync(comic);
            }
        }

        private static async Task<StorageFile> ResolveComicFileAsync(ComicLibraryItem comic)
        {
            if (!string.IsNullOrWhiteSpace(comic.AccessToken))
            {
                try
                {
                    return await StorageApplicationPermissions.FutureAccessList.GetFileAsync(comic.AccessToken);
                }
                catch
                {
                }
            }

            return await StorageFile.GetFileFromPathAsync(comic.Path);
        }

        private static async Task LoadComicCoverAsync(ComicLibraryItem comic)
        {
            try
            {
                var file = await ResolveComicFileAsync(comic);
                var coverData = await Task.Run(() => ReadCoverImage(file.Path));
                if (coverData is null)
                {
                    comic.IsAvailable = false;
                    return;
                }

                comic.CoverImage = await CreateCoverImageAsync(coverData);
            }
            catch
            {
                comic.IsAvailable = false;
            }
        }

        private static byte[]? ReadCoverImage(string filePath)
        {
            using var archive = ArchiveFactory.OpenArchive(filePath);
            var coverEntry = archive.Entries
                .Where(entry => !entry.IsDirectory && !string.IsNullOrEmpty(entry.Key) && IsImageFile(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (coverEntry is null)
            {
                return null;
            }

            using var entryStream = coverEntry.OpenEntryStream();
            using var imageStream = new MemoryStream();
            entryStream.CopyTo(imageStream);
            return imageStream.ToArray();
        }

        private static async Task<BitmapImage> CreateCoverImageAsync(byte[] imageData)
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(imageData);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = 280 };
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            if (_comicFile is not null)
            {
                DataTransferManagerInterop.ShowShareUIForWindow(WindowNative.GetWindowHandle(this));
            }
        }

        private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_comicFile is null)
            {
                args.Request.FailWithDisplayText("No hay ningún cómic abierto.");
                return;
            }

            args.Request.Data.Properties.Title = _comicFile.Name;
            args.Request.Data.Properties.Description = "Compartir este cómic";
            args.Request.Data.SetStorageItems(new[] { _comicFile });
        }

        private static List<ComicPage> ReadPages(Stream archiveStream)
        {
            using var archive = ArchiveFactory.OpenArchive(archiveStream);
            var pages = new List<ComicPage>();

            foreach (var entry in archive.Entries
                         .Where(entry => !entry.IsDirectory && !string.IsNullOrEmpty(entry.Key) && IsImageFile(entry.Key))
                         .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                using var entryStream = entry.OpenEntryStream();
                using var imageStream = new MemoryStream();
                entryStream.CopyTo(imageStream);
                pages.Add(new ComicPage(imageStream.ToArray()));
            }

            return pages;
        }

        private static bool IsImageFile(string entryName)
        {
            return Path.GetExtension(entryName).ToLowerInvariant() is
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff";
        }

        private async Task ShowCurrentPageAsync()
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(_pages[_currentPageIndex].ImageData);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            PageImage.Source = bitmap;
            PageScrollViewer.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            UpdateZoomControls(true);
            FitPageToWindow(resetZoom: false);
            PageScrollViewer.ChangeView(0, 0, _zoomFactor, true);
            SetZoomSliderValue(_zoomFactor * 100);
            UpdateNavigation();
        }

        private void FitPageToWindow(bool resetZoom)
        {
            if (PageImage.Source is not BitmapImage bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0 ||
                PageScrollViewer.ViewportWidth <= 0 || PageScrollViewer.ViewportHeight <= 0)
            {
                return;
            }

            var scale = Math.Min(
                PageScrollViewer.ViewportWidth / bitmap.PixelWidth,
                PageScrollViewer.ViewportHeight / bitmap.PixelHeight);
            PageImage.Width = bitmap.PixelWidth * scale;
            PageImage.Height = bitmap.PixelHeight * scale;

            if (resetZoom)
            {
                _zoomFactor = 1f;
                SetZoomSliderValue(100);
                PageScrollViewer.ChangeView(null, null, _zoomFactor, true);
            }
        }

        private void UpdateZoomControls(bool enabled)
        {
            ZoomSlider.IsEnabled = enabled;
            ZoomInButton.IsEnabled = enabled && ZoomSlider.Value < ZoomSlider.Maximum;
            ZoomOutButton.IsEnabled = enabled && ZoomSlider.Value > ZoomSlider.Minimum;
            FitButton.IsEnabled = enabled;
        }

        private void PageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            FitPageToWindow(resetZoom: false);
        }

        private void PageScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            _zoomFactor = PageScrollViewer.ZoomFactor;
            var zoomPercent = Math.Clamp(Math.Round(_zoomFactor * 100, 1), ZoomSlider.Minimum, ZoomSlider.Maximum);
            SetZoomSliderValue(zoomPercent);
        }

        private void PageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (!ZoomSlider.IsEnabled || (e.KeyModifiers & VirtualKeyModifiers.Control) == 0)
            {
                return;
            }

            var pointerPoint = e.GetCurrentPoint(PageScrollViewer);
            var wheelDelta = pointerPoint.Properties.MouseWheelDelta;
            if (wheelDelta == 0)
            {
                return;
            }

            var currentZoom = PageScrollViewer.ZoomFactor;
            var minimumZoom = ZoomSlider.Minimum / 100;
            var maximumZoom = ZoomSlider.Maximum / 100;
            var nextZoom = Math.Clamp(currentZoom * Math.Pow(2, wheelDelta / 120d), minimumZoom, maximumZoom);
            if (Math.Abs(nextZoom - currentZoom) < 0.001)
            {
                e.Handled = true;
                return;
            }

            var pointerPosition = pointerPoint.Position;
            var zoomRatio = nextZoom / currentZoom;
            var horizontalOffset = (PageScrollViewer.HorizontalOffset + pointerPosition.X) * zoomRatio - pointerPosition.X;
            var verticalOffset = (PageScrollViewer.VerticalOffset + pointerPosition.Y) * zoomRatio - pointerPosition.Y;

            _zoomFactor = (float)nextZoom;
            SetZoomSliderValue(nextZoom * 100);
            PageScrollViewer.ChangeView(horizontalOffset, verticalOffset, _zoomFactor, true);
            e.Handled = true;
        }

        private void SetZoomSliderValue(double value)
        {
            if (Math.Abs(ZoomSlider.Value - value) < 0.05)
            {
                return;
            }

            _isSyncingZoom = true;
            try
            {
                ZoomSlider.Value = value;
            }
            finally
            {
                _isSyncingZoom = false;
            }
        }

        private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_isInitialized)
            {
                return;
            }

            ZoomLabel.Text = $"{e.NewValue:0} %";
            ZoomInButton.IsEnabled = ZoomSlider.IsEnabled && e.NewValue < ZoomSlider.Maximum;
            ZoomOutButton.IsEnabled = ZoomSlider.IsEnabled && e.NewValue > ZoomSlider.Minimum;

            if (_isSyncingZoom)
            {
                return;
            }

            _zoomFactor = (float)(e.NewValue / 100);
            if (PageScrollViewer.ZoomFactor != _zoomFactor)
            {
                PageScrollViewer.ChangeView(null, null, _zoomFactor, true);
            }
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Max(ZoomSlider.Minimum, ZoomSlider.Value - ZoomSlider.StepFrequency);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = Math.Min(ZoomSlider.Maximum, ZoomSlider.Value + ZoomSlider.StepFrequency);
        }

        private void FitButton_Click(object sender, RoutedEventArgs e)
        {
            FitPageToWindow(resetZoom: true);
        }

        private void UpdateNavigation()
        {
            PreviousButton.IsEnabled = _currentPageIndex > 0;
            NextButton.IsEnabled = _currentPageIndex < _pages.Count - 1;
            PageLabel.Text = _pages.Count == 0 ? "0 / 0" : $"{_currentPageIndex + 1} / {_pages.Count}";
        }

        private async void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(-1);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateAsync(1);
        }

        private async void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Left)
            {
                await NavigateAsync(-1);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Right)
            {
                await NavigateAsync(1);
                e.Handled = true;
            }
        }

        private async Task NavigateAsync(int offset)
        {
            var nextPageIndex = _currentPageIndex + offset;
            if (nextPageIndex < 0 || nextPageIndex >= _pages.Count)
            {
                return;
            }

            _currentPageIndex = nextPageIndex;
            if (_comicFile is not null)
            {
                _libraryRepository.UpdateLastReadPage(_comicFile.Path, _currentPageIndex);
            }

            await ShowCurrentPageAsync();
        }

        [DllImport("kernel32.dll", SetLastError = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        private sealed record ComicPage(byte[] ImageData);
    }
}
