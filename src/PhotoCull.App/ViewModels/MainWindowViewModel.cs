using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCull.Core;
using PhotoCull.Core.Abstractions;
using PhotoCull.Infrastructure.Pipeline;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace PhotoCull.App.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly CullingPipeline _pipeline;
        private readonly IImageRepository _repo;
        private readonly IThumbnailService _thumb;
        private readonly IDeletionService _deleter;
        private CancellationTokenSource? _cts;

        public ObservableCollection<SetViewModel> Sets { get; } = new();

        private SetViewModel? _selectedSet;
        public SetViewModel? SelectedSet { get => _selectedSet; set => SetProperty(ref _selectedSet, value); }

        private ImageViewModel? _selectedImage;
        public ImageViewModel? SelectedImage { get => _selectedImage; set => SetProperty(ref _selectedImage, value); }

        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

        public MainWindowViewModel(CullingPipeline pipeline, IImageRepository repo, IThumbnailService thumb, IDeletionService deleter)
        {
            _pipeline = pipeline; _repo = repo; _thumb = thumb; _deleter = deleter;
        }

        [RelayCommand]
        public async Task StartScanAsync(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            try
            {
                IsScanning = true;
                _cts = new CancellationTokenSource();
                var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCull","cache");
                var progress = new Progress<PhotoCull.Infrastructure.Pipeline.PipelineProgress>(p => {
                    // simple progress estimate
                    Progress = p.Processed + p.Errors;
                });

                await _pipeline.RunAsync(folder, cache, progress, _cts.Token);
                await LoadSetsAsync();
            }
            catch (OperationCanceledException) { }
            finally { IsScanning = false; _cts = null; Progress = 0; }
        }

        [RelayCommand]
        public void CancelScan()
        {
            _cts?.Cancel();
        }

        public async Task LoadSetsAsync()
        {
            Sets.Clear();
            var sets = await _repo.ListSetsAsync();
            var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCull","cache");
            foreach (var s in sets)
            {
                var svm = new SetViewModel(s.Id, s.Name);
                var members = await _repo.LoadSetMembersAsync(s.Id);
                foreach (var m in members)
                {
                    try
                    {
                        var rec = await _repo.GetImageByIdAsync(m.ImageId);
                        if (rec == null) continue;
                        var ivm = new ImageViewModel(rec);
                        await ivm.LoadExtrasAsync(_thumb, _repo, cache);
                        svm.Members.Add(ivm);
                    }
                    catch { }
                }
                Sets.Add(svm);
            }

            SelectedSet = Sets.FirstOrDefault();
            SelectedImage = SelectedSet?.Members.FirstOrDefault();
        }

        [RelayCommand]
        public async Task MarkDecisionAsync(string action)
        {
            if (SelectedImage == null) return;
            await _repo.SaveUserDecisionAsync(SelectedImage.Id, action);
            SelectedImage.UserDecision = action;
        }

        public async Task<PhotoCull.Core.Abstractions.DeletionPreview> PreviewDeleteAsync()
        {
            // gather all images marked as Delete by user decision in selected set
            if (SelectedSet == null) return new PhotoCull.Core.Abstractions.DeletionPreview(0,0);
            var toDelete = SelectedSet.Members.Where(m => m.UserDecision == "Delete").Select(m => m.Path).ToList();
            var preview = await _deleter.PreviewAsync(toDelete);
            // return to caller
            return preview;
        }

        [RelayCommand]
        public async Task ExecuteDeleteAsync(bool recycle)
        {
            if (SelectedSet == null) return;
            var toDelete = SelectedSet.Members.Where(m => m.UserDecision == "Delete").Select(m => m.Path).ToList();
            var mode = recycle ? PhotoCull.Core.Abstractions.DeletionMode.Recycle : PhotoCull.Core.Abstractions.DeletionMode.Permanent;
            var report = await _deleter.ExecuteAsync(toDelete, mode);
            // remove succeeded from UI
            foreach (var p in report.Succeeded)
            {
                foreach (var s in Sets)
                {
                    var found = s.Members.FirstOrDefault(x => x.Path == p);
                    if (found != null) s.Members.Remove(found);
                }
            }
        }
    }
}