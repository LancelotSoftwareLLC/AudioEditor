using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace AudioEditor.Uwp.Views
{
    public sealed partial class MainPage : Page
    {
        readonly StorageFolder _localFolder;
        private StorageFile _audioFile;
        private DateTime _timeStamp;
        private MediaStreamSource _streamSource;
        //private MediaComposition _composition;
        AudioGraph audioGraph;
        private string _audioFileName;
        private double overlayHeightDenominator = 3;
        private double overlayOpacity = 1;
        private double _volumeSliderValue = 0;
        private bool _isDirty;
        private List<string> _supportedFileTypes;
        
        public static readonly DependencyProperty ClipsProperty = DependencyProperty.Register(
            "Clips", typeof(ObservableCollection<AudioFileInputNode>), typeof(MainPage), new PropertyMetadata(default(ObservableCollection<AudioFileInputNode>)));

        public ObservableCollection<AudioFileInputNode> Clips
        {
            get => (ObservableCollection<AudioFileInputNode>)GetValue(ClipsProperty);
            set => SetValue(ClipsProperty, value);
        }
        

        public MainPage()
        {
            this.InitializeComponent();

            Clips = new ObservableCollection<AudioFileInputNode>();
            
            if (!DesignMode.DesignModeEnabled || !DesignMode.DesignMode2Enabled)
            {
                _localFolder = ApplicationData.Current.LocalFolder;
            }
            
            _supportedFileTypes = new List<string>();

            _supportedFileTypes.Add(".mp3");
            _supportedFileTypes.Add(".wav");
            _supportedFileTypes.Add(".wma");
            _supportedFileTypes.Add(".m4a");
        }

        private async Task InitAudioGraph()
        {
            AudioGraphSettings settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media);

            CreateAudioGraphResult result = await AudioGraph.CreateAsync(settings);

            if (result.Status != AudioGraphCreationStatus.Success)
            {
                await new MessageDialog("AudioGraph creation error: " + result.Status).ShowAsync();
            }

            audioGraph = result.Graph;

        }

        #region drag & drop

        private async void Grid_DragEnter(object sender, DragEventArgs e)
        {
            try
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Drop audio file to import...";
            }
            catch (Exception ex)
            {
                await new MessageDialog("There was a problem with the file you dragged in. Was it a supported audio file type? \r\n\nTry again with a different file. \r\n\nError: " + ex.Message).ShowAsync();
            }
        }

        private async void Grid_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

                var items = await e.DataView.GetStorageItemsAsync();

                if (!items.Any()) return;

                if (items.Count > 1)
                {
                    await new MessageDialog("You can only drop one video at a time").ShowAsync();
                    return;
                }

                BusyIndicator.IsActive = true;
                BusyIndicator.Content = "loading file";

                var storageFile = items[0] as StorageFile;
                string fileType = storageFile?.FileType.ToLowerInvariant();

                if (!_supportedFileTypes.Contains(fileType))
                {
                    await new MessageDialog("This file type is not yet supported. Choose mp3, wav, wma or m4a.").ShowAsync();
                    return;
                }
                
                await CreateAudioClipAsync(storageFile);
            }
            catch (Exception ex)
            {
                await new MessageDialog("There was a problem with the file being dropped in, try again. Error: " + ex.Message).ShowAsync();
                HideBusyIndicator();
            }
        }

        #endregion

        #region click handlers

        private async void SelectVideoFileButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var openPicker = new FileOpenPicker();
                openPicker.ViewMode = PickerViewMode.List;
                openPicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;

                foreach (var supportedFileType in _supportedFileTypes)
                {
                    openPicker.FileTypeFilter.Add(supportedFileType);
                }

                ShowBusyIndicator("loading file...");

                var storageFile = await openPicker.PickSingleFileAsync();

                if (storageFile == null)
                {
                    HideBusyIndicator();
                    return;
                }

                string fileType = storageFile.FileType.ToLowerInvariant();

                if (_supportedFileTypes.Contains(fileType))
                {
                    await CreateAudioClipAsync(storageFile);
                }
                else
                {
                    await new MessageDialog("Sorry, this file is not a supported video file (MP4, AVI or WMV).").ShowAsync();
                }
            }
            catch (Exception ex)
            {
                await new MessageDialog("filePicker Problem" + ex.Message).ShowAsync();
            }
        }
        
        
        
        private void AddCommandBarButtonClick(object sender, RoutedEventArgs e)
        {
            //OverlayGrid.Visibility = Visibility.Visible;
        }

        private async void SaveButtonClick(object sender, RoutedEventArgs e)
        {
            //await SaveCompositionAsync();
            await CreateFileOutputNode();
        }

        private void CancelCommandBarButton_Click(object sender, RoutedEventArgs e)
        {
            Clips.Clear();
            ResetComposition();
        }
        
        
        private async void RemoveClipButtonClick(object sender, RoutedEventArgs e)
        {
            //try
            //{
            //    var selectedClip = (sender as Button).DataContext as AudioFileInputNode;
            //    if (selectedClip == null) return;

            //    Clips.Remove(selectedClip);
                
            //    _composition.Clips.Remove(selectedClip?.Clip);

            //    await RenderToMediaElement();
            //}
            //catch (Exception ex)
            //{
            //    await new MessageDialog("Remove Clip: " + ex.Message).ShowAsync();
            //}
        }
        
        #endregion

        #region Tasks and methods
        
        private async Task ShowVolumeInputDialogAsync()
        {
            try
            {
                var cd = new ContentDialog
                {
                    PrimaryButtonText = "continue",
                    Title = "set clip volume"
                };

                var slider = new Slider
                {
                    Header = "Volume: " + _volumeSliderValue * 100 + "%",
                    Minimum = 0,
                    Value = _volumeSliderValue,
                    Maximum = 1,
                    TickFrequency = 0.1,
                    StepFrequency = 0.1,
                    SmallChange = 0.05,
                    LargeChange = 0.1,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                slider.ValueChanged += VolumeSlider_ValueChanged;

                cd.Content = slider;
                await cd.ShowAsync();

                slider.ValueChanged -= VolumeSlider_ValueChanged;
            }
            catch (Exception ex)
            {
                await new MessageDialog("Show Volume Dialog Error: " + ex.Message).ShowAsync();
                HideBusyIndicator();
            }
        }

        private async Task CreateAudioClipAsync(StorageFile file)
        {
            if (audioGraph == null)
                return;

            try
            {
                ShowBusyIndicator("creating audio clip...");

                CreateAudioFileInputNodeResult result = await audioGraph.CreateFileInputNodeAsync(file);

                if (result.Status != AudioFileNodeCreationStatus.Success)
                {
                    await new MessageDialog("Sorry, there war an error: " + result.Status.ToString()).ShowAsync();
                    HideBusyIndicator();
                }
                
                //var node = result.FileInputNode;
                
                //_volumeSliderValue = node.OutgoingGain;
                //await ShowVolumeInputDialogAsync();
                //node.OutgoingGain = _volumeSliderValue;
                
                Clips.Add(result.FileInputNode);


                
                //OverlayGrid.Visibility = Visibility.Collapsed;
                HideBusyIndicator();
                
            }
            catch (Exception ex)
            {
                await new MessageDialog("Create Audio Clip: " + ex.Message).ShowAsync();
                HideBusyIndicator();
            }
        }


        private async Task CreateFileOutputNode()
        {
            FileSavePicker saveFilePicker = new FileSavePicker();
            saveFilePicker.FileTypeChoices.Add("Pulse Code Modulation", new List<string>() { ".wav" });
            saveFilePicker.FileTypeChoices.Add("Windows Media Audio", new List<string>() { ".wma" });
            saveFilePicker.FileTypeChoices.Add("MPEG Audio Layer-3", new List<string>() { ".mp3" });
            saveFilePicker.SuggestedFileName = "New Audio Track";
            StorageFile file = await saveFilePicker.PickSaveFileAsync();

            // File can be null if cancel is hit in the file picker
            if (file == null)
            {
                return;
            }

            MediaEncodingProfile mediaEncodingProfile;

            switch (file.FileType.ToLowerInvariant())
            {
                case ".wma":
                    mediaEncodingProfile = MediaEncodingProfile.CreateWma(AudioEncodingQuality.High);
                    break;
                case ".mp3":
                    mediaEncodingProfile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
                    break;
                case ".wav":
                    mediaEncodingProfile = MediaEncodingProfile.CreateWav(AudioEncodingQuality.High);
                    break;
                default:
                    throw new ArgumentException();
            }
            
            // Operate node at the graph format, but save file at the specified format
            CreateAudioFileOutputNodeResult result = await audioGraph.CreateFileOutputNodeAsync(file, mediaEncodingProfile);
            
            result.FileOutputNode.Stop();
            await result.FileOutputNode.FinalizeAsync();
            audioGraph.Stop();

            if (result.Status != AudioFileNodeCreationStatus.Success)
            {
                // FileOutputNode creation failed
                await new MessageDialog("There was a problem rendering the composition, try again. Error: " + result.Status).ShowAsync();
                return;
            }
            
        }

        private void ResetComposition()
        {
            PlaybackElement.Source = null;
            audioGraph = null;
            _isDirty = false;

            EditingCommandBar.IsEnabled = false;
            RemoveVideoOverlayButton.Visibility = Visibility.Collapsed;
            //OverlayGrid.Visibility = Visibility.Visible;

            HideBusyIndicator();
        }

        private void ShowBusyIndicator(string message)
        {
            if (!BusyIndicator.IsActive) BusyIndicator.IsActive = true;
            BusyIndicator.Content = message;
        }

        private void HideBusyIndicator()
        {
            BusyIndicator.IsActive = false;
            BusyIndicator.Content = "";
        }

        #endregion

        #region events
        

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (sender == null || e == null) return;
            var slider = (Slider)sender;
            slider.Header = "Volume: " + e.NewValue * 100 + "%";
            _volumeSliderValue = e.NewValue;
        }

        private async void PlaybackElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            await new MessageDialog("An error occured with the media player, please try again. If this keeps happening, please contact us at video.diary@outlook.com.").ShowAsync();
        }

        private async void SaveProgress(IAsyncOperationWithProgress<TranscodeFailureReason, double> asyncInfo, double progressInfo)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                BusyIndicator.Content = string.Format("Do not leave this page until finished! {0}% complete", Math.Ceiling(progressInfo));
            });
        }

        private async void Completed(IAsyncOperationWithProgress<TranscodeFailureReason, double> asyncInfo, AsyncStatus asyncStatus)
        {
            try
            {
                //if (asyncInfo.GetResults() != TranscodeFailureReason.None)
                //{
                //    //if transcoding failed
                //    await new MessageDialog("There was a problem transcoding the audio, try again.").ShowAsync();
                //    HideBusyIndicator();
                //}

                //await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                //{
                //    BusyIndicator.Content = "adding video to collection...";



                //    ViewModel.DiaryRecordings.Enqueue(new VideoViewModel
                //    {
                //        DateRecorded = _timeStamp,
                //        FileName = _audioFileName,
                //        FileLocation = _audioFile.Path,
                //        Title = string.Format("Diary Entry {0}", _timeStamp),
                //        Duration = _composition.Duration.ToString("g"),
                //        Notes = "",
                //        ThumbFileName = _thumbFileName,
                //        ThumbnailFileUri = _thumbImageFile.Path,
                //        Emotion = App.ViewModel.SelectedEmotion,
                //        IsEncryptionEnabled = false
                //    });

                //    BusyIndicator.Content = "saving video history...";
                    
                //    BusyIndicator.Content = "successfully saved...";
                //    ResetComposition();
                //});
            }
            catch (Exception ex)
            {
                await new MessageDialog("Render Completed: " + ex.Message).ShowAsync();
                HideBusyIndicator();
            }
        }

        #endregion

        #region Navigation

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await InitAudioGraph();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            //await DisposeAllAsync();
            base.OnNavigatedFrom(e);
        }

        #endregion
    }
}
