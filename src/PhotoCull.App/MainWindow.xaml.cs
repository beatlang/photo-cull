using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoCull.App.Helpers;
using PhotoCull.App.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace PhotoCull.App
{
    public sealed partial class MainWindow : Window
    {
        private MainWindowViewModel? VM => RootGrid?.DataContext as MainWindowViewModel;
        private bool _syncing;

        public MainWindow()
        {
            InitializeComponent();
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

            PickFolderBtn.Click += async (_, __) => await PickFolder();
            CancelBtn.Click += (_, __) => VM?.CancelScan();
            KeepBtn.Click += async (_, __) => await MarkDecision("Keep");
            ReviewBtn.Click += async (_, __) => await MarkDecision("Review");
            MarkDeleteBtn.Click += async (_, __) => await MarkDecision("Delete");
            FavBtn.Click += async (_, __) => await MarkDecision("Favorite");
            AcceptBtn.Click += async (_, __) => { if (VM != null) await VM.AcceptRecommendationsForSet(); };
            CompareToggle.Checked += (_, __) => SetCompareVisibility(true);
            CompareToggle.Unchecked += (_, __) => SetCompareVisibility(false);
            DeleteBtn.Click += async (_, __) => await ShowDeleteDialog();
            CompareLeft.ViewChanged += Compare_ViewChanged_Left;
            CompareRight.ViewChanged += Compare_ViewChanged_Right;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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
        }

        private async Task PickFolder()
        {
            var path = await FolderPickerHelper.PickFolderAsync(this);
            if (!string.IsNullOrWhiteSpace(path))
                await RunScan(path);
        }

        private async Task RunScan(string? folder = null)
        {
            folder ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (VM == null) return;
            await VM.StartScanCommand.ExecuteAsync(folder);
            await VM.LoadSetsAsync();
            UpdateSelection();
            UpdateEmptyState();
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e) => await PickFolder();

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
                CompareLeftImage.Source = null;
                CompareRightImage.Source = null;
                FileNameText.Text = "No photo selected";
                InfoText.Text = string.Empty;
                return;
            }

            MainImage.Source = CreateBitmap(selected.ThumbnailUri);
            var best = VM?.SelectedSet?.Members.FirstOrDefault(m => m.IsBestShot) ?? selected;
            CompareLeftImage.Source = CreateBitmap(best.ThumbnailUri);
            CompareRightImage.Source = CreateBitmap(selected.ThumbnailUri);
            FileNameText.Text = selected.FileName;
            InfoText.Text = selected.InfoLine;
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

        private void SetCompareVisibility(bool visible)
        {
            CompareGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ImageScroll.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            if (visible) UpdateSelection();
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
                case VirtualKey.R: _ = MarkDecision("Review"); break;
                case VirtualKey.D: _ = MarkDecision("Delete"); break;
                case VirtualKey.F: _ = MarkDecision("Favorite"); break;
                case VirtualKey.C: CompareToggle.IsChecked = !CompareToggle.IsChecked; break;
                default: return;
            }
            e.Handled = true;
        }

        private void Compare_ViewChanged_Left(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_syncing) return;
            try { _syncing = true; CompareRight.ChangeView(CompareLeft.HorizontalOffset, CompareLeft.VerticalOffset, null, true); }
            finally { _syncing = false; }
        }

        private void Compare_ViewChanged_Right(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_syncing) return;
            try { _syncing = true; CompareLeft.ChangeView(CompareRight.HorizontalOffset, CompareRight.VerticalOffset, null, true); }
            finally { _syncing = false; }
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
                Content = $"You are about to delete {preview.Count} files ({preview.TotalBytesSaved} bytes). Use Recycle bin?",
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
    }
}
