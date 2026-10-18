using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace PhotoCull.App.ViewModels
{
    public class SetViewModel : ObservableObject
    {
        public long Id { get; }
        public string Name { get; }

        public ObservableCollection<ImageViewModel> Members { get; } = new();
        public string? CoverUri => Members.FirstOrDefault(m => m.IsBestShot)?.ThumbnailUri
            ?? Members.FirstOrDefault()?.ThumbnailUri;

        public SetViewModel(long id, string name)
        {
            Id = id; Name = name;
        }
    }
}