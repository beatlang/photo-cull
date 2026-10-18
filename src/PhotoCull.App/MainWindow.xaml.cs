using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoCull.App.ViewModels;
using PhotoCull.App.Helpers;
using System;
using Microsoft.UI.Xaml.Input;
using System.Linq;
using Windows.Storage;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace PhotoCull.App
{
    public sealed partial class MainWindow : Window
    {
        private MainWindowViewModel? VM => RootGrid?.DataContext as MainWindowViewModel;

        public MainWindow()
        {
            this.InitializeComponent();
            RootGrid.Loaded += MainWindow_Loaded;

            PickFolderBtn.Click += async (_, __) => await PickFolder();
            RunScanBtn.Click += async (_, __) => await RunScan();
            CancelBtn.Click += (_, __) => VM?.CancelScan();

            SetsList.SelectionChanged += SetsList_SelectionChanged;
            KeepBtn.Click += async (_, __) => { if (VM!=null) await VM.MarkDecisionAsync("Keep"); };
            ReviewBtn.Click += async (_, __) => { if (VM!=null) await VM.MarkDecisionAsync("Review"); };
            MarkDeleteBtn.Click += async (_, __) => { if (VM!=null) await VM.MarkDecisionAsync("Delete"); };
            FavBtn.Click += async (_, __) => { if (VM!=null) await VM.MarkDecisionAsync("Favorite"); };

            CompareToggle.Checked += (_, __) => CompareGrid.Visibility = Visibility.Visible;
            CompareToggle.Unchecked += (_, __) => CompareGrid.Visibility = Visibility.Collapsed;

            CompareLeft.ViewChanged += Compare_ViewChanged_Left;
            CompareRight.ViewChanged += Compare_ViewChanged_Right;

            DeleteBtn.Click += async (_, __) => await ShowDeleteDialog();

            // attach DataContext from App services
            try
            {
                var app = (App)Application.Current;
                var vm = app.Services.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
                if (vm != null) RootGrid.DataContext = vm;
            }
            catch { }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (VM != null)
            {
                await VM.LoadSetsAsync();
                UpdateSelection();
            }
        }

        private async Task PickFolder()
        {
            var path = await FolderPickerHelper.PickFolderAsync(this);
            if (!string.IsNullOrEmpty(path))
            {
                // store last path or pass to VM
                await RunScan(path);
            }
        }

        private async Task RunScan(string folder = null)
        {
            folder ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (VM != null)
            {
                await VM.StartScanCommand.ExecuteAsync(folder);
                await VM.LoadSetsAsync();
                UpdateSelection();
            }
        }

        private void SetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            var selected = VM?.SelectedImage ?? VM?.SelectedSet?.Members.FirstOrDefault();
            if (selected != null)
            {
                // main image
                try
                {
                    var bmp = new BitmapImage(new Uri("file:///" + selected.Path));
                    MainImage.Source = bmp;

                    // compare images
                    CompareLeftImage.Source = bmp;
                    // second image use next member for demonstration
                    var other = VM?.SelectedSet?.Members.Skip(1).FirstOrDefault();
                    if (other != null) CompareRightImage.Source = new BitmapImage(new Uri("file:///" + other.Path));

                    InfoText.Text = $"{System.IO.Path.GetFileName(selected.Path)}\n{selected.Width}x{selected.Height} - {selected.SizeBytes} bytes";
                }
                catch { }

                if (selected.Scores != null)
                {
                    var s = selected.Scores;
                    ScoresText.Text = $"Sharpness: {s.Sharpness:F2} Exposure: {s.Exposure:F2} Noise: {s.Noise:F2} Quality: {s.Quality:F2}";
                    // update hist widths proportionally
                    RHist.Width = Math.Max(10, 200 * Clamp(s.Sharpness / 10.0, 0, 1));
                    GHist.Width = Math.Max(10, 200 * Clamp(s.Exposure / 10.0, 0, 1));
                    BHist.Width = Math.Max(10, 200 * Clamp(s.Noise / 10.0, 0, 1));
                    LHist.Width = Math.Max(10, 200 * Clamp(s.Brightness / 255.0, 0, 1));
                }
            }
        }

        private double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);

        private bool _syncing = false;
        private void Compare_ViewChanged_Left(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_syncing) return;
            try { _syncing = true; CompareRight.ChangeView(CompareLeft.HorizontalOffset, CompareLeft.VerticalOffset, null, true); } finally { _syncing = false; }
        }

        private void Compare_ViewChanged_Right(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_syncing) return;
            try { _syncing = true; CompareLeft.ChangeView(CompareRight.HorizontalOffset, CompareRight.VerticalOffset, null, true); } finally { _syncing = false; }
        }

        private async Task ShowDeleteDialog()
        {
            if (VM == null || VM.SelectedSet == null) return;
            var toDelete = VM.SelectedSet.Members.Where(m => m.UserDecision == "Delete").ToList();
            if (!toDelete.Any()) return;

            var preview = await VM.PreviewDeleteAsync();

            var dlg = new ContentDialog()
            {
                Title = "Confirm delete",
                Content = $"You are about to delete {preview.Count} files ({preview.TotalBytesSaved} bytes). Use Recycle bin?",
                PrimaryButtonText = "Recycle",
                SecondaryButtonText = "Permanent",
                CloseButtonText = "Cancel"
            };

            var res = await dlg.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                await VM.ExecuteDeleteCommand.ExecuteAsync(true);
            }
            else if (res == ContentDialogResult.Secondary)
            {
                var confirm = new ContentDialog() { Title = "Permanent delete", Content = "This will permanently delete files. Continue?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel" };
                var c = await confirm.ShowAsync();
                if (c == ContentDialogResult.Primary) await VM.ExecuteDeleteCommand.ExecuteAsync(false);
            }
        }
    }
}
