using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using PCull.App.Helpers;
using PCull.App.Settings;
using PCull.App.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace PCull.App
{
    public sealed partial class MainWindow : Window
    {
        private MainWindowViewModel? VM => RootGrid?.DataContext as MainWindowViewModel;
        private readonly string? _startupError;
        private readonly CullingSettingsStore _settingsStore;
        private CullingSettings _cullingSettings;
        private ResponsiveLayoutMode _layoutMode = ResponsiveLayoutMode.Wide;
        private bool _setsOpen;
        private bool _detailsOpen;
        private readonly NativeMethods.SubclassProc _windowSubclassProc = WindowSubclassProc;

        private enum ResponsiveLayoutMode
        {
            Wide,
            Medium,
            Narrow
        }

        public MainWindow(string? startupError, CullingSettingsStore settingsStore, CullingSettings cullingSettings)
        {
            _startupError = startupError;
            _settingsStore = settingsStore;
            _cullingSettings = cullingSettings;
            InitializeComponent();
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            ConfigureWindowSize();
            ResponsiveHost.SizeChanged += ResponsiveHost_SizeChanged;
            SetsList.SelectionChanged += SetsList_SelectionChanged;
            Closed += MainWindow_Closed;
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PCull.ico"));
            RootGrid.Loaded += MainWindow_Loaded;

            try
            {
                var app = (App)Application.Current;
                RootGrid.DataContext = app.Services.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to create MainWindowViewModel");
                throw;
            }

            CancelBtn.Click += (_, __) => VM?.CancelScan();
            KeepBtn.Click += async (_, __) => await MarkDecision("Keep");
            MarkDeleteBtn.Click += async (_, __) => await MarkDecision("Delete");
            SettingsBtn.Click += async (_, __) => await ShowSettingsDialog();
            DeleteBtn.Click += async (_, __) => await ShowDeleteDialog();
            MainImage.ImageOpened += (_, _) => FitImageToViewport(ImageScroll, MainImage);

            VM?.UpdateClusterOptions(_cullingSettings.ToClusterOptions());
            UpdateScanControls();
            UpdateSettingsButton();
            UpdateResponsiveLayout(ResponsiveHost.ActualWidth);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_startupError != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "pCull cannot start",
                    Content = _startupError,
                    CloseButtonText = "Close"
                };
                await dialog.ShowAsync();
                Close();
                return;
            }

            if (VM == null) return;
            VM.PropertyChanged += VM_PropertyChanged;
            UpdateFailureInfoBar();
            await VM.LoadSetsAsync();
            UpdateSelection();
            UpdateEmptyState();
        }

        private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedImage) ||
                e.PropertyName == nameof(MainWindowViewModel.SelectedSet))
                UpdateSelection();
            if (e.PropertyName == nameof(MainWindowViewModel.Sets) ||
                e.PropertyName == nameof(MainWindowViewModel.SelectedSet))
                UpdateEmptyState();
            if (e.PropertyName == nameof(MainWindowViewModel.IsScanning) ||
                e.PropertyName == nameof(MainWindowViewModel.HasFolder) ||
                e.PropertyName == nameof(MainWindowViewModel.MarkedForDeleteCount))
                UpdateScanControls();
            if (e.PropertyName == nameof(MainWindowViewModel.AnalysisFailures))
                UpdateFailureInfoBar();
        }

        private void UpdateFailureInfoBar()
        {
            var failures = VM?.AnalysisFailures ?? string.Empty;
            StatusInfoBar.Message = failures;
            StatusInfoBar.IsOpen = !string.IsNullOrWhiteSpace(failures);
        }

        private async Task PickFolder()
        {
            var path = await FolderPickerHelper.PickFolderAsync(this);
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (VM != null) VM.FolderPath = path;
                UpdateScanControls();
            }
        }

        private async Task RunScan(string? folder = null)
        {
            if (VM == null) return;
            folder ??= VM.FolderPath;
            if (string.IsNullOrWhiteSpace(folder) || VM.IsScanning) return;
            await VM.StartScanCommand.ExecuteAsync(folder);
            UpdateSelection();
            UpdateEmptyState();
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e) => await PickFolder();
        private async void StartAnalysis_Click(object sender, RoutedEventArgs e) => await RunScan();

        private void UpdateScanControls()
        {
            if (VM == null) return;
            PickFolderBtn.IsEnabled = !VM.IsScanning;
            StartAnalysisBtn.IsEnabled = VM.HasFolder && !VM.IsScanning;
            CancelBtn.Visibility = VM.IsScanning ? Visibility.Visible : Visibility.Collapsed;
            CancelBtn.IsEnabled = VM.IsScanning;
            DeleteBtn.IsEnabled = !VM.IsScanning && VM.MarkedForDeleteCount > 0;
            DeleteBtn.Style = VM.MarkedForDeleteCount > 0
                ? Application.Current.Resources["AccentButtonStyle"] as Style
                : null;
        }

        private async Task MarkDecision(string action)
        {
            if (VM != null) await VM.MarkDecisionAsync(action);
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            var selected = VM?.SelectedImage ?? VM?.SelectedSet?.Members.FirstOrDefault();
            if (selected == null)
            {
                MainImage.Source = null;
                FileNameText.Text = "No photo selected";
                InfoText.Text = string.Empty;
                return;
            }

            ImageScroll.ChangeView(null, null, 1f, true);
            MainImage.Source = CreateBitmap(selected.Path);
            FileNameText.Text = selected.FileName;
            InfoText.Text = selected.InfoLine;
        }

        private static void FitImageToViewport(ScrollViewer viewer, Image image)
        {
            if (image.ActualWidth <= 0 || image.ActualHeight <= 0 ||
                viewer.ActualWidth <= 0 || viewer.ActualHeight <= 0)
                return;

            var fitFactor = (float)Math.Min(
                viewer.ActualWidth / image.ActualWidth,
                viewer.ActualHeight / image.ActualHeight);
            viewer.ZoomToFactor(Math.Clamp(fitFactor, viewer.MinZoomFactor, viewer.MaxZoomFactor));
        }

        private static BitmapImage? CreateBitmap(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var fullPath = Path.GetFullPath(path);
                return new BitmapImage(new Uri(fullPath, UriKind.Absolute));
            }
            catch
            {
                return null;
            }
        }

        private void UpdateEmptyState()
        {
            var isEmpty = VM?.Sets.Count == 0;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = VM?.HiddenSetCount > 0
                ? "Nothing to clean up \u2013 all sets are fine"
                : "Pick a folder to find photos to cull";
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.OriginalSource is TextBox)
                return;

            if (e.Key == VirtualKey.Escape && CloseResponsivePanels())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.S && SetsToggleBtn.Visibility == Visibility.Visible)
            {
                SetSetsPanelOpen(!_setsOpen);
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.I && DetailsToggleBtn.Visibility == Visibility.Visible)
            {
                SetDetailsPanelOpen(!_detailsOpen);
                e.Handled = true;
                return;
            }

            if (VM == null) return;
            switch (e.Key)
            {
                case VirtualKey.Left: VM.PreviousImage(); break;
                case VirtualKey.Right: VM.NextImage(); break;
                case VirtualKey.Up: VM.PreviousSet(); break;
                case VirtualKey.Down: VM.NextSet(); break;
                case VirtualKey.K: _ = MarkDecision("Keep"); break;
                case VirtualKey.D: _ = MarkDecision("Delete"); break;
                case VirtualKey.Delete: _ = MarkDecision("Delete"); break;
                default: return;
            }
            e.Handled = true;
        }

        private void ConfigureWindowSize()
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = NativeMethods.GetDpiForWindow(handle);
            var scale = dpi == 0 ? 1d : dpi / 96d;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                (int)Math.Round(1360 * scale),
                (int)Math.Round(860 * scale)));

            if (!NativeMethods.SetWindowSubclass(handle, _windowSubclassProc, new UIntPtr(1), UIntPtr.Zero))
                Serilog.Log.Warning("Could not install the minimum-window-size handler");
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeMethods.RemoveWindowSubclass(handle, _windowSubclassProc, new UIntPtr(1));
        }

        private static IntPtr WindowSubclassProc(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData)
        {
            const uint wmGetMinMaxInfo = 0x0024;
            if (message == wmGetMinMaxInfo)
            {
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                var dpi = NativeMethods.GetDpiForWindow(handle);
                var scale = dpi == 0 ? 1d : dpi / 96d;
                info.MinTrackSize.X = (int)Math.Ceiling(720 * scale);
                info.MinTrackSize.Y = (int)Math.Ceiling(520 * scale);
                Marshal.StructureToPtr(info, lParam, false);
            }

            return NativeMethods.DefSubclassProc(handle, message, wParam, lParam);
        }

        private void ResponsiveHost_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateResponsiveLayout(e.NewSize.Width);

        private void UpdateResponsiveLayout(double width)
        {
            if (width <= 0)
                width = ResponsiveHost.ActualWidth;
            if (width <= 0)
                return;

            var nextMode = width >= 1200
                ? ResponsiveLayoutMode.Wide
                : width >= 800 ? ResponsiveLayoutMode.Medium : ResponsiveLayoutMode.Narrow;
            if (_layoutMode != nextMode)
            {
                _layoutMode = nextMode;
                _setsOpen = false;
                _detailsOpen = false;
                SetsToggleBtn.IsChecked = false;
                DetailsToggleBtn.IsChecked = false;
            }

            VisualStateManager.GoToState(ResponsiveHost, nextMode.ToString(), true);
            ToolbarGrid.RowDefinitions[1].Height = nextMode == ResponsiveLayoutMode.Narrow
                ? GridLength.Auto
                : new GridLength(0);
            Grid.SetRow(StepFlow, nextMode == ResponsiveLayoutMode.Narrow ? 1 : 0);
            Grid.SetColumn(StepFlow, nextMode == ResponsiveLayoutMode.Narrow ? 0 : 1);
            Grid.SetColumnSpan(StepFlow, nextMode == ResponsiveLayoutMode.Narrow ? 3 : 1);

            var compact = nextMode != ResponsiveLayoutMode.Wide;
            StepLabel1.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            StepLabel2.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            StepLabel3.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            StepLabel4.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SummaryText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            KeyHints.Visibility = nextMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            FilmstripRow.Height = new GridLength(nextMode == ResponsiveLayoutMode.Narrow ? 108 : 120);

            switch (nextMode)
            {
                case ResponsiveLayoutMode.Wide:
                    SetsCol.Width = new GridLength(0.9, GridUnitType.Star);
                    SetsCol.MinWidth = 220;
                    SetsCol.MaxWidth = 320;
                    DetailsCol.Width = new GridLength(1.1, GridUnitType.Star);
                    DetailsCol.MinWidth = 260;
                    DetailsCol.MaxWidth = 360;
                    SetsToggleBtn.Visibility = Visibility.Collapsed;
                    DetailsToggleBtn.Visibility = Visibility.Collapsed;
                    break;
                case ResponsiveLayoutMode.Medium:
                    SetsCol.Width = _setsOpen ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                    SetsCol.MinWidth = _setsOpen ? 0 : 200;
                    SetsCol.MaxWidth = _setsOpen ? 0 : 240;
                    DetailsCol.Width = new GridLength(0);
                    DetailsCol.MinWidth = 0;
                    DetailsCol.MaxWidth = 0;
                    SetsToggleBtn.Visibility = Visibility.Visible;
                    DetailsToggleBtn.Visibility = Visibility.Visible;
                    break;
                default:
                    SetsCol.Width = new GridLength(0);
                    SetsCol.MinWidth = 0;
                    SetsCol.MaxWidth = 0;
                    DetailsCol.Width = new GridLength(0);
                    DetailsCol.MinWidth = 0;
                    DetailsCol.MaxWidth = 0;
                    SetsToggleBtn.Visibility = Visibility.Visible;
                    DetailsToggleBtn.Visibility = Visibility.Visible;
                    break;
            }

            ApplyPanelVisibility();
        }

        private void SetsToggle_Click(object sender, RoutedEventArgs e)
            => SetSetsPanelOpen(SetsToggleBtn.IsChecked == true);

        private void DetailsToggle_Click(object sender, RoutedEventArgs e)
            => SetDetailsPanelOpen(DetailsToggleBtn.IsChecked == true);

        private void SetSetsPanelOpen(bool isOpen)
        {
            _setsOpen = _layoutMode != ResponsiveLayoutMode.Wide && isOpen;
            if (_setsOpen)
            {
                _detailsOpen = false;
                DetailsToggleBtn.IsChecked = false;
            }
            SetsToggleBtn.IsChecked = _setsOpen;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SetsToggleBtn, _setsOpen ? "Hide sets" : "Show sets");
            ToolTipService.SetToolTip(SetsToggleBtn, _setsOpen ? "Hide sets (S)" : "Show sets (S)");
            ApplyPanelVisibility();
            if (_setsOpen)
                SetsList.Focus(FocusState.Programmatic);
            else if (SetsToggleBtn.Visibility == Visibility.Visible)
                SetsToggleBtn.Focus(FocusState.Programmatic);
        }

        private void SetDetailsPanelOpen(bool isOpen)
        {
            _detailsOpen = _layoutMode != ResponsiveLayoutMode.Wide && isOpen;
            if (_detailsOpen)
            {
                _setsOpen = false;
                SetsToggleBtn.IsChecked = false;
            }
            DetailsToggleBtn.IsChecked = _detailsOpen;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DetailsToggleBtn, _detailsOpen ? "Hide photo details" : "Show photo details");
            ToolTipService.SetToolTip(DetailsToggleBtn, _detailsOpen ? "Hide details (I)" : "Show details (I)");
            ApplyPanelVisibility();
            if (_detailsOpen)
                DetailsScroll.Focus(FocusState.Programmatic);
            else if (DetailsToggleBtn.Visibility == Visibility.Visible)
                DetailsToggleBtn.Focus(FocusState.Programmatic);
        }

        private bool CloseResponsivePanels()
        {
            if (!_setsOpen && !_detailsOpen)
                return false;

            var focusDetails = _detailsOpen;
            _setsOpen = false;
            _detailsOpen = false;
            SetsToggleBtn.IsChecked = false;
            DetailsToggleBtn.IsChecked = false;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SetsToggleBtn, "Show sets");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DetailsToggleBtn, "Show photo details");
            ToolTipService.SetToolTip(SetsToggleBtn, "Show sets (S)");
            ToolTipService.SetToolTip(DetailsToggleBtn, "Show details (I)");
            ApplyPanelVisibility();
            if (focusDetails && DetailsToggleBtn.Visibility == Visibility.Visible)
                DetailsToggleBtn.Focus(FocusState.Programmatic);
            else if (SetsToggleBtn.Visibility == Visibility.Visible)
                SetsToggleBtn.Focus(FocusState.Programmatic);
            return true;
        }

        private void SetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_layoutMode != ResponsiveLayoutMode.Wide && SetsList.SelectedItem != null && _setsOpen)
            {
                _setsOpen = false;
                SetsToggleBtn.IsChecked = false;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SetsToggleBtn, "Show sets");
                ToolTipService.SetToolTip(SetsToggleBtn, "Show sets (S)");
                ApplyPanelVisibility();
                KeepBtn.Focus(FocusState.Programmatic);
            }
        }

        private void ApplyPanelVisibility()
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SetsToggleBtn, _setsOpen ? "Hide sets" : "Show sets");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DetailsToggleBtn, _detailsOpen ? "Hide photo details" : "Show photo details");
            ToolTipService.SetToolTip(SetsToggleBtn, _setsOpen ? "Hide sets (S)" : "Show sets (S)");
            ToolTipService.SetToolTip(DetailsToggleBtn, _detailsOpen ? "Hide details (I)" : "Show details (I)");
            var wide = _layoutMode == ResponsiveLayoutMode.Wide;
            var narrow = _layoutMode == ResponsiveLayoutMode.Narrow;
            if (_layoutMode == ResponsiveLayoutMode.Medium)
            {
                SetsCol.Width = _setsOpen ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                SetsCol.MinWidth = _setsOpen ? 0 : 200;
                SetsCol.MaxWidth = _setsOpen ? 0 : 240;
            }
            var setsOverlay = !wide && _setsOpen;
            var detailsOverlay = !wide && _detailsOpen;

            SetsCard.Visibility = wide || !narrow || _setsOpen ? Visibility.Visible : Visibility.Collapsed;
            DetailsCard.Visibility = wide || _detailsOpen ? Visibility.Visible : Visibility.Collapsed;
            ConfigureCardPosition(SetsCard, 0, setsOverlay, true);
            ConfigureCardPosition(DetailsCard, 2, detailsOverlay, false);
        }

        private void ConfigureCardPosition(Border card, int normalColumn, bool overlay, bool alignLeft)
        {
            Grid.SetColumn(card, overlay ? 1 : normalColumn);
            card.HorizontalAlignment = overlay ? (alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right) : HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(card, overlay ? 5 : 0);
            if (overlay)
            {
                var maximum = Math.Max(0, StageGrid.ActualWidth * 0.9);
                card.MaxWidth = maximum;
                card.Width = Math.Min(alignLeft ? 280 : 300, maximum);
            }
            else
            {
                card.Width = double.NaN;
                card.MaxWidth = double.PositiveInfinity;
            }
        }

        private async Task ShowDeleteDialog()
        {
            if (VM == null) return;
            var preview = await VM.PreviewDeleteAsync();
            if (preview.Count == 0) return;

            var dlg = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Confirm delete",
                Content = $"You are about to delete {preview.Count} files ({FormatBytes(preview.TotalBytesSaved)}). Use Recycle bin?",
                PrimaryButtonText = "Recycle",
                SecondaryButtonText = "Permanent",
                CloseButtonText = "Cancel"
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await VM.ExecuteDeleteCommand.ExecuteAsync(true);
            else if (result == ContentDialogResult.Secondary)
            {
                var confirm = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "Permanent delete",
                    Content = "This will permanently delete files. Continue?",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel"
                };
                if (await confirm.ShowAsync() == ContentDialogResult.Primary)
                    await VM.ExecuteDeleteCommand.ExecuteAsync(false);
            }
            UpdateSelection();
            UpdateEmptyState();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):F1} MB";
            return $"{bytes / (1024d * 1024 * 1024):F1} GB";
        }

        private async Task ShowSettingsDialog()
        {
            var burstWindow = CreateNumberBox(_cullingSettings.BurstWindowSeconds, 0, 600, 0.5, 5);
            var comparisonWindow = CreateNumberBox(_cullingSettings.ComparisonWindowSeconds, 0, 86400, 5, 30);
            var pHashThreshold = CreateNumberBox(_cullingSettings.PHashHammingThreshold, 0, 64, 1, 4);
            var dHashThreshold = CreateNumberBox(_cullingSettings.DHashHammingThreshold, 0, 64, 1, 4);
            var embeddingThreshold = CreateNumberBox(_cullingSettings.EmbeddingCosineThreshold, 0, 1, 0.01, 0.05);

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = "Changes apply to the next scan. A match on any signal joins photos into a set, and similarity links can chain across a set.",
                TextWrapping = TextWrapping.Wrap
            });
            AddSettingsField(content, "Capture-time window (seconds)", "Join photos taken within this time. 0 matches only identical timestamps.", burstWindow);
            AddSettingsField(content, "Comparison window (seconds)", "Only photos taken within this time of each other are compared at all. Faster scans. 0 compares every photo with every other photo; photos without capture time are always compared.", comparisonWindow);
            AddSettingsField(content, "pHash maximum bit distance", "Lower values require more visually similar image structure.", pHashThreshold);
            AddSettingsField(content, "dHash maximum bit distance", "Lower values require more similar horizontal image gradients.", dHashThreshold);
            AddSettingsField(content, "AI embedding cosine similarity", "Higher values require more semantically similar images.", embeddingThreshold);

            var resetButton = new Button { Content = "Reset defaults" };
            resetButton.Click += (_, _) =>
            {
                var defaults = new CullingSettings();
                burstWindow.Value = defaults.BurstWindowSeconds;
                comparisonWindow.Value = defaults.ComparisonWindowSeconds;
                pHashThreshold.Value = defaults.PHashHammingThreshold;
                dHashThreshold.Value = defaults.DHashHammingThreshold;
                embeddingThreshold.Value = defaults.EmbeddingCosineThreshold;
            };
            content.Children.Add(resetButton);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Grouping settings",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            if (pHashThreshold.Value != Math.Truncate(pHashThreshold.Value) ||
                dHashThreshold.Value != Math.Truncate(dHashThreshold.Value))
            {
                await ShowMessageAsync("Invalid grouping settings", "Hash distance values must be whole numbers.");
                return;
            }

            var updatedSettings = new CullingSettings
            {
                BurstWindowSeconds = burstWindow.Value,
                ComparisonWindowSeconds = comparisonWindow.Value,
                PHashHammingThreshold = (int)pHashThreshold.Value,
                DHashHammingThreshold = (int)dHashThreshold.Value,
                EmbeddingCosineThreshold = embeddingThreshold.Value
            };

            try
            {
                updatedSettings.Validate();
                await _settingsStore.SaveAsync(updatedSettings);
                _cullingSettings = updatedSettings;
                VM?.UpdateClusterOptions(updatedSettings.ToClusterOptions());
                UpdateSettingsButton();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Could not save grouping settings", ex.Message);
            }
        }

        private static NumberBox CreateNumberBox(double value, double minimum, double maximum, double smallChange, double largeChange)
        {
            return new NumberBox
            {
                Value = value,
                Minimum = minimum,
                Maximum = maximum,
                SmallChange = smallChange,
                LargeChange = largeChange,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
        }

        private void UpdateSettingsButton()
        {
            ToolTipService.SetToolTip(SettingsBtn,
                $"Grouping: {_cullingSettings.BurstWindowSeconds:g}s \u00B7 compare \u2264{_cullingSettings.ComparisonWindowSeconds:g}s \u00B7 pHash \u2264{_cullingSettings.PHashHammingThreshold} \u00B7 dHash \u2264{_cullingSettings.DHashHammingThreshold} \u00B7 AI \u2265{_cullingSettings.EmbeddingCosineThreshold:0.00}");
        }

        private static void AddSettingsField(StackPanel panel, string label, string description, NumberBox input)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(input, description);
            panel.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(input);
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = "Close"
            };
            await dialog.ShowAsync();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        private static class NativeMethods
        {
            [UnmanagedFunctionPointer(CallingConvention.Winapi)]
            public delegate IntPtr SubclassProc(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData);

            [DllImport("user32.dll")]
            public static extern uint GetDpiForWindow(IntPtr handle);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetWindowSubclass(IntPtr handle, SubclassProc callback, UIntPtr subclassId, UIntPtr refData);

            [DllImport("comctl32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool RemoveWindowSubclass(IntPtr handle, SubclassProc callback, UIntPtr subclassId);

            [DllImport("comctl32.dll")]
            public static extern IntPtr DefSubclassProc(IntPtr handle, uint message, UIntPtr wParam, IntPtr lParam);
        }
    }

    public sealed class StepGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out var step))
                return currentStep > step ? "\u2713" : step.ToString(CultureInfo.InvariantCulture);
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class ScoreTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is IFormattable score ? score.ToString("0.0", CultureInfo.CurrentCulture) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class ObjectToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
