using CommunityToolkit.Mvvm.ComponentModel;
using PCull.Core.Culling;
using System.Collections.ObjectModel;
using System.Linq;

namespace PCull.App.ViewModels
{
    public class SetViewModel : ObservableObject
    {
        public long Id { get; }
        public string Name { get; }

        public ObservableCollection<ImageViewModel> Members { get; } = new();
        public ObservableCollection<ClusterMatch> Matches { get; } = new();
        public string PhotoCountText => $"{Members.Count} photos";
        public string? CoverUri => Members.FirstOrDefault(m => m.IsBestShot)?.ThumbnailUri
            ?? Members.FirstOrDefault()?.ThumbnailUri;

        public SetViewModel(long id, string name)
        {
            Id = id; Name = name;
        }
    }
}