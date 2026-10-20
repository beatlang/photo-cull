using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
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

        public MainWindow(string? startupError, CullingSettingsStore settingsStore, CullingSettings cullingSettings)
        {
            _startupError = startupError;
            _settingsStore = settingsStore;
            _cullingSettings = cullingSettings;
            InitializeComponent();
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
                ? "Nothing to clean up – all sets are fine"
                : "Pick a folder to find photos to cull";
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
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
                $"Grouping: {_cullingSettings.BurstWindowSeconds:g}s · compare ≤{_cullingSettings.ComparisonWindowSeconds:g}s · pHash ≤{_cullingSettings.PHashHammingThreshold} · dHash ≤{_cullingSettings.DHashHammingThreshold} · AI ≥{_cullingSettings.EmbeddingCosineThreshold:0.00}");
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
    }
}
