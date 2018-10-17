using Windows.Media.Editing;
using Windows.UI.Xaml.Media.Imaging;
using CommonHelpers.Common;

namespace AudioEditor.Uwp.Models
{
    public class UserClip : BindableBase
    {
        private MediaClip _clip;
        private BitmapImage _thumbnail;

        public UserClip(MediaClip clip, BitmapImage thumb)
        {
            Clip = clip;
            Thumbnail = thumb;
        }

        public MediaClip Clip
        {
            get => _clip;
            set => SetProperty(ref _clip, value);
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }
    }
}
