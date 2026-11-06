using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Threading;
using Flyweight.Core;

namespace Flyweight.UI
{
    public partial class MainWindow : Window
    {
        private const int AutoCloseSeconds = 8;
        private const int StayOnTopSeconds = 4;
        private const int MaxQualityAv1 = 63;
        private const int MaxQualityOther = 51;
        private const double FullWindowMinWidth = 480;
        private const double FullWindowMinHeight = 300;
        private const double WideLayoutWidth = 920;
        private const double SidePanelWidth = 460;
        private const double UsualQueueHeight = 290;
        private const double TitleBarAllowance = 64;

        private readonly AppSettings _settings;
        private readonly string _settingsPath;
        private readonly DispatcherTimer _autoClose;
        private readonly DispatcherTimer _stayOnTop;
        private readonly Action<AppSettings> _presetsChanged;

        private Compressor _compressor;
        private bool _isCompact;
        private bool _isQueueRunning;
        private bool _isLoadingLevel;
        private bool _closeWhenQueueStops;
        private int _autoCloseSecondsLeft;
        private bool _deleteArmed;
        private double _arrangedForWidth;

        /// <param name="presetsChanged">Called when custom presets come, go or get renamed, so the right-click menu can follow.</param>
        public MainWindow(AppSettings settings, string settingsPath, FFmpegTools tools, bool compact, bool dark, Action<AppSettings> presetsChanged = null)
        {
            _settings = settings;
            _settingsPath = settingsPath;
            _presetsChanged = presetsChanged;

            Theme.Apply(this, dark);
            _isLoadingLevel = true;
            InitializeComponent();

            _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoClose.Tick += AutoClose_Tick;

            _stayOnTop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(StayOnTopSeconds) };
            _stayOnTop.Tick += (s, e) => StopStayingOnTop();

            // A right-click job runs alongside whatever the user is doing, so it must not take focus.
            ShowActivated = !compact;
            Topmost = compact;
            ContentRendered += (s, e) =>
            {
                if (_isCompact) ShowWithoutTakingFocus();
            };

            QueueList.ItemsSource = Jobs;
            FillOptions();
            RebuildCustomCards();
            SyncCards();
            _isLoadingLevel = false;
            LoadLevelIntoPanel();
            RefreshOutputFolder();

            SetCompact(compact);
            if (!compact) RestoreSize();
            WindowStartupLocation = compact ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
            SetTools(tools);
        }

        public ObservableCollection<JobViewModel> Jobs { get; } = new ObservableCollection<JobViewModel>();

        private LevelSettings CurrentLevel => _settings.Selected;

        private CustomPreset CurrentCustom => _settings.FindCustom(_settings.SelectedCustomId);

        /// <summary>A request from the command line or from a second copy of the app.</summary>
        public void HandleRequest(CliArgs request)
        {
            if (request.Files.Count > 0)
            {
                AddPaths(request.Files, request.Level, request.PresetId);
                ShowWithoutTakingFocus();
                return;
            }

            // Opening the app on purpose is the one time it should come forward.
            SetCompact(false);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>Lifts the window into view for a moment, then lets other windows cover it again.</summary>
        private void ShowWithoutTakingFocus()
        {
            if (WindowState == WindowState.Minimized) return;

            Topmost = true;
            _stayOnTop.Stop();
            _stayOnTop.Start();
        }

        private void StopStayingOnTop()
        {
            _stayOnTop.Stop();
            Topmost = false;
        }

        /// <summary>Queues videos using the given preset or level, or whichever card is selected if neither.</summary>
        public void AddPaths(IEnumerable<string> paths, LevelId? level, string presetId = null)
        {
            var files = VideoFiles.Expand(paths);
            if (files.Count == 0)
            {
                if (Jobs.Count == 0) SetCompact(false);
                DropHint.Visibility = Visibility.Visible;
                return;
            }

            DropHint.Visibility = Visibility.Collapsed;
            var settings = _settings.FindCustom(presetId)?.Settings ?? (level.HasValue ? _settings.Get(level.Value) : _settings.Selected);
            foreach (string file in files)
            {
                // A copy, so changing settings later doesn't alter videos already in the queue.
                Jobs.Add(new JobViewModel(file, settings.Clone()));
            }

            RefreshQueueArea();
            RunQueue();
        }

        public void RefreshQueueArea()
        {
            QueueArea.Visibility = Jobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- running the queue ----

        private async void RunQueue()
        {
            if (_isQueueRunning || _compressor == null) return;

            _isQueueRunning = true;
            StopAutoClose();
            var compressor = _compressor;

            JobViewModel job;
            while ((job = Jobs.FirstOrDefault(j => !j.IsFinished)) != null)
            {
                var current = job;
                current.MarkStarted();
                UpdateTaskbar();

                string folder = _settings.OutputFolder;
                var progress = new Progress<CompressionProgress>(p =>
                {
                    current.Apply(p);
                    UpdateTaskbar();
                });

                CompressionResult result;
                try
                {
                    result = await Task.Run(() => compressor.Run(current.InputPath, current.Settings, folder, progress, current.CancelToken));
                }
                catch (Exception ex)
                {
                    // Whatever went wrong with this video, the rest of the queue should still run.
                    result = new CompressionResult { Outcome = JobOutcome.Failed, Message = "Something went wrong with this video.", Details = ex.ToString() };
                }
                current.Apply(result);
            }

            _isQueueRunning = false;
            UpdateTaskbar();
            OnQueueStopped();
        }

        private void OnQueueStopped()
        {
            if (_closeWhenQueueStops)
            {
                Close();
            }
            else if (_isCompact && Jobs.All(j => !j.Failed))
            {
                StartAutoClose();
            }
        }

        private void UpdateTaskbar()
        {
            if (!_isQueueRunning)
            {
                Taskbar.ProgressState = TaskbarItemProgressState.None;
                return;
            }

            double percentSum = Jobs.Sum(j => j.IsFinished ? 100 : j.Percent);
            Taskbar.ProgressState = TaskbarItemProgressState.Normal;
            Taskbar.ProgressValue = percentSum / (Jobs.Count * 100.0);
        }

        // ---- closing by itself after a right-click job ----

        private void StartAutoClose()
        {
            _autoCloseSecondsLeft = AutoCloseSeconds;
            ShowClosingNote();
            _autoClose.Start();
        }

        private void StopAutoClose()
        {
            _autoClose.Stop();
            ClosingNote.Visibility = Visibility.Collapsed;
        }

        private void AutoClose_Tick(object sender, EventArgs e)
        {
            // Holding the pointer over the window keeps it open for as long as it's needed.
            _autoCloseSecondsLeft = IsMouseOver ? AutoCloseSeconds : _autoCloseSecondsLeft - 1;
            if (_autoCloseSecondsLeft <= 0)
            {
                _autoClose.Stop();
                Close();
                return;
            }
            ShowClosingNote();
        }

        private void ShowClosingNote()
        {
            ClosingNote.Text = $"All done. Closing in {_autoCloseSecondsLeft}s.";
            ClosingNote.Visibility = Visibility.Visible;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            RememberSize();

            var unfinished = Jobs.Where(j => !j.IsFinished).ToList();
            if (unfinished.Count == 0) return;

            var answer = MessageBox.Show(this, "Stop compressing and close?\n\nYour original videos aren't affected.",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }

            foreach (var job in unfinished) job.Cancel();
            if (_isQueueRunning)
            {
                // Stay open a moment so the half-written file gets cleaned up.
                _closeWhenQueueStops = true;
                e.Cancel = true;
            }
        }

        // ---- ffmpeg ----

        private void SetTools(FFmpegTools tools)
        {
            _compressor = tools == null ? null : new Compressor(tools);
            MissingToolsBanner.Visibility = tools == null ? Visibility.Visible : Visibility.Collapsed;
            if (tools != null && Jobs.Count > 0) RunQueue();
        }

        private void InstallToolsButton_Click(object sender, RoutedEventArgs e)
        {
            InstallToolsButton.IsEnabled = false;
            MissingToolsDetail.Text = "Installing in the window that just opened. Come back here when it's finished.";
            try
            {
                var process = Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c winget install -e --id Gyan.FFmpeg --accept-source-agreements --accept-package-agreements & pause") { UseShellExecute = true });
                process.EnableRaisingEvents = true;
                process.Exited += (s, args) => Dispatcher.BeginInvoke(new Action(OnToolsInstallerExited));
            }
            catch (Win32Exception)
            {
                OnToolsInstallerExited();
            }
        }

        private void OnToolsInstallerExited()
        {
            InstallToolsButton.IsEnabled = true;
            var tools = FFmpegLocator.Find();
            if (tools == null) MissingToolsDetail.Text = "It still can't be found. You can install it yourself from ffmpeg.org, then reopen this app.";
            SetTools(tools);
        }

        // ---- layout ----

        private void SetCompact(bool compact)
        {
            bool wasCompact = _isCompact;
            _isCompact = compact;
            MainControls.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            QueueScroller.Margin = new Thickness(0, compact ? 0 : 12, 0, 0);

            // The corner window is just a progress readout, so it stays the size it is.
            ResizeMode = compact ? ResizeMode.CanMinimize : ResizeMode.CanResize;
            MinWidth = compact ? 0 : FullWindowMinWidth;
            MinHeight = compact ? 0 : FullWindowMinHeight;

            if (compact)
            {
                PlaceCompactWindow();
            }
            else if (wasCompact)
            {
                RestoreSize();
                StopAutoClose();
                StopStayingOnTop();
                Dispatcher.BeginInvoke(new Action(CenterOnScreen), DispatcherPriority.Loaded);
            }
            UpdateHeightLimit();
            MakeRoomForQueue();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Fitting its contents only ever changes the height. A new width means someone dragged or
            // snapped the window, and from then on the size they chose stands.
            if (!_isCompact && e.WidthChanged && e.PreviousSize.Width > 0) SizeToContent = SizeToContent.Manual;

            if (_isCompact) PlaceCompactWindow();
            UpdateHeightLimit();
            ArrangeForWidth(e.NewSize.Width);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            UpdateHeightLimit();
        }

        private void Layout_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MakeRoomForQueue();
        }

        private void RestoreSize()
        {
            var area = SystemParameters.WorkArea;
            var size = WindowSizing.Fit(_settings.WindowWidth, _settings.WindowHeight, MinWidth, MinHeight, area.Width, area.Height);
            if (size == null) return;

            SizeToContent = SizeToContent.Manual;
            Width = size.Value.Width;
            Height = size.Value.Height;
            UpdateHeightLimit();
        }

        /// <summary>Only a size someone chose gets remembered. Otherwise the window goes on fitting its contents.</summary>
        internal void RememberSize()
        {
            if (_isCompact || SizeToContent != SizeToContent.Manual) return;

            // RestoreBounds is the size to come back to if it's maximized now, and empty if it was never shown.
            var size = RestoreBounds.IsEmpty ? new Size(Width, Height) : RestoreBounds.Size;
            _settings.WindowWidth = size.Width;
            _settings.WindowHeight = size.Height;
            Save();
        }

        /// <summary>
        /// A wide window puts Advanced options beside everything else rather than underneath. That's what
        /// actually frees up height, since a window can only get as tall as the screen.
        /// </summary>
        internal void ArrangeForWidth(double width)
        {
            _arrangedForWidth = width;
            bool wide = !_isCompact && width >= WideLayoutWidth;
            bool showSide = wide && AdvancedToggle.IsChecked == true;

            var home = wide ? SideSlot : StackedSlot;
            if (home.Child != AdvancedPanel)
            {
                (wide ? StackedSlot : SideSlot).Child = null;
                home.Child = AdvancedPanel;
            }

            AdvancedPanel.Margin = new Thickness(0, wide ? 0 : 10, 0, 0);
            SideSlot.Visibility = showSide ? Visibility.Visible : Visibility.Collapsed;
            SideColumn.Width = new GridLength(showSide ? SidePanelWidth : 0);
        }

        /// <summary>
        /// Fitting its contents, the window has to stop at the bottom of the screen. Once someone has
        /// chosen a size or maximized it, what they chose is the limit.
        /// </summary>
        private void UpdateHeightLimit()
        {
            bool fitsContents = SizeToContent != SizeToContent.Manual && WindowState == WindowState.Normal;
            Root.MaxHeight = fitsContents ? SystemParameters.WorkArea.Height - TitleBarAllowance : double.PositiveInfinity;
        }

        /// <summary>In a window of someone's chosen size, the queue gets whatever the rest leaves over.</summary>
        private void MakeRoomForQueue()
        {
            double limit = UsualQueueHeight;
            if (!_isCompact && SizeToContent == SizeToContent.Manual)
            {
                double everythingElse = MainColumn.ActualHeight - QueueScroller.ActualHeight + Page.Margin.Top + Page.Margin.Bottom;
                limit = Math.Max(UsualQueueHeight, Math.Floor(Root.ActualHeight - everythingElse));
            }
            QueueScroller.MaxHeight = limit;
        }

        /// <summary>Bottom-right, out of the way, like a copy-progress window.</summary>
        private void PlaceCompactWindow()
        {
            var area = SystemParameters.WorkArea;
            double height = ActualHeight > 0 ? ActualHeight : 120;
            Left = area.Right - Width - 16;
            Top = area.Bottom - height - 16;
        }

        private void CenterOnScreen()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + (area.Height - ActualHeight) / 2;
        }

        // ---- drag and drop ----

        private static bool HasFiles(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (!HasFiles(e)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            DropZone.Tag = "over";
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.Tag = null;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropZone.Tag = null;
            if (!HasFiles(e)) return;
            e.Handled = true;
            AddPaths((string[])e.Data.GetData(DataFormats.FileDrop), null);
        }

        private void DropZone_Click(object sender, RoutedEventArgs e)
        {
            var patterns = string.Join(";", VideoFiles.Extensions.Select(x => "*" + x));
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose videos to compress",
                Multiselect = true,
                Filter = $"Videos|{patterns}|All files|*.*",
            };
            if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames, null);
        }

        // ---- queue row buttons ----

        private static JobViewModel JobOf(object sender) => (JobViewModel)((FrameworkElement)sender).DataContext;

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            JobOf(sender).Cancel();
        }

        private void ShowFile_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer.exe", "/select,\"" + JobOf(sender).ResultPath + "\"");
        }

        private void Details_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, JobOf(sender).Details, "What ffmpeg said", MessageBoxButton.OK, MessageBoxImage.None);
        }

        // ---- levels ----

        private RadioButton RadioFor(LevelId level)
        {
            switch (level)
            {
                case LevelId.Light: return LevelLight;
                case LevelId.Balanced: return LevelBalanced;
                default: return LevelMaximum;
            }
        }

        private void Level_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingLevel) return;
            _settings.SelectLevel(sender == LevelLight ? LevelId.Light : sender == LevelBalanced ? LevelId.Balanced : LevelId.Maximum);
            AfterSelectionChanged();
        }

        private void AfterSelectionChanged()
        {
            DisarmDelete();
            SyncCards();
            Save();
            LoadLevelIntoPanel();
        }

        /// <summary>Makes exactly one card look selected, across the built-in row and the custom ones.</summary>
        private void SyncCards()
        {
            bool wasLoading = _isLoadingLevel;
            _isLoadingLevel = true;

            foreach (LevelId level in Enum.GetValues(typeof(LevelId)))
            {
                RadioFor(level).IsChecked = _settings.SelectedCustomId == null && _settings.SelectedLevel == level;
            }
            foreach (var card in CustomCards.Children.Cast<RadioButton>())
            {
                card.IsChecked = (string)card.Tag == _settings.SelectedCustomId;
            }

            _isLoadingLevel = wasLoading;
        }

        // ---- presets the user made ----

        private void RebuildCustomCards()
        {
            CustomCards.Children.Clear();
            foreach (var preset in _settings.CustomPresets)
            {
                var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                var caption = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
                caption.SetResourceReference(StyleProperty, "Muted");

                var content = new StackPanel();
                content.Children.Add(title);
                content.Children.Add(caption);

                var card = new RadioButton { GroupName = "Level", Tag = preset.Id, Content = content, Margin = new Thickness(4, 8, 4, 0) };
                card.SetResourceReference(StyleProperty, "LevelCard");
                card.Checked += CustomCard_Checked;

                CustomCards.Children.Add(card);
                RefreshCard(preset);
            }
        }

        private void RefreshCard(CustomPreset preset)
        {
            var card = CustomCards.Children.Cast<RadioButton>().First(c => (string)c.Tag == preset.Id);
            var lines = ((StackPanel)card.Content).Children;
            string caption = LevelText.Caption(preset.Settings);

            ((TextBlock)lines[0]).Text = preset.Name;
            ((TextBlock)lines[1]).Text = caption;
            card.ToolTip = preset.Name;
            System.Windows.Automation.AutomationProperties.SetName(card, $"{preset.Name}: {caption}");
        }

        private void CustomCard_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingLevel) return;
            _settings.SelectCustom((string)((RadioButton)sender).Tag);
            AfterSelectionChanged();
        }

        private void NewPresetButton_Click(object sender, RoutedEventArgs e)
        {
            // Starts as a copy, so tweaking it can't change the level it came from.
            var preset = _settings.AddCustom(_settings.SuggestPresetName(), CurrentLevel);
            _settings.SelectCustom(preset.Id);

            RebuildCustomCards();
            AfterSelectionChanged();
            _presetsChanged?.Invoke(_settings);

            AdvancedToggle.IsChecked = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PresetNameBox.Focus();
                PresetNameBox.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void PresetNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var preset = CurrentCustom;
            if (_isLoadingLevel || preset == null) return;

            string typed = PresetNameBox.Text.Trim();
            preset.Name = typed.Length > 0 ? typed : "Untitled";
            Save();
            RefreshCard(preset);
            _presetsChanged?.Invoke(_settings);
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_deleteArmed)
            {
                // Two clicks, so a stray one can't lose a preset, without a dialog in the way.
                _deleteArmed = true;
                DeletePresetButton.Content = "Click again to delete";
                return;
            }

            _settings.RemoveCustom(_settings.SelectedCustomId);
            RebuildCustomCards();
            AfterSelectionChanged();
            _presetsChanged?.Invoke(_settings);
        }

        private void DisarmDelete()
        {
            _deleteArmed = false;
            DeletePresetButton.Content = "Delete this preset";
        }

        private void LoadLevelIntoPanel()
        {
            var level = CurrentLevel;
            _isLoadingLevel = true;

            PresetNameBox.Text = CurrentCustom?.Name ?? "";

            SelectTag(CodecBox, (int)level.Codec);
            SelectTag(EncoderBox, (int)level.Encoder);
            QualitySlider.Maximum = level.Codec == VideoCodec.Av1 ? MaxQualityAv1 : MaxQualityOther;
            QualitySlider.Value = level.Quality;
            SelectTag(ResolutionBox, level.MaxShortSide);
            SelectTag(FrameRateBox, level.MaxFps);
            SelectTag(AudioBox, level.AudioKbps);
            TargetSizeBox.Text = level.TargetSizeMb > 0 ? level.TargetSizeMb.ToString() : "";
            ExtraArgsBox.Text = level.ExtraArgs ?? "";

            _isLoadingLevel = false;
            RefreshLevelText();
        }

        private void RefreshLevelText()
        {
            var custom = CurrentCustom;
            if (custom != null) RefreshCard(custom);

            LevelSummary.Text = custom != null ? LevelText.Describe(custom.Settings) : LevelText.Summary(_settings.SelectedLevel, CurrentLevel);
            ResetButton.Visibility = custom != null || _settings.IsDefault(_settings.SelectedLevel) ? Visibility.Collapsed : Visibility.Visible;
            DeletePresetButton.Visibility = custom != null ? Visibility.Visible : Visibility.Collapsed;
            PresetNameRow.Visibility = custom != null ? Visibility.Visible : Visibility.Collapsed;
            QualityValue.Text = ((int)QualitySlider.Value).ToString();
            QualitySlider.IsEnabled = CurrentLevel.TargetSizeMb <= 0;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.Reset(_settings.SelectedLevel);
            Save();
            LoadLevelIntoPanel();
        }

        // ---- advanced options ----

        private void FillOptions()
        {
            Fill(CodecBox,
                ("H.265: smaller files, plays on most devices", (int)VideoCodec.Hevc),
                ("H.264: bigger files, plays on everything", (int)VideoCodec.H264),
                ("AV1: smallest files, newer devices only", (int)VideoCodec.Av1));
            Fill(EncoderBox,
                ("Graphics card: fast, leaves your PC free", (int)EncoderChoice.Auto),
                ("Processor: smaller files, but slower and busier", (int)EncoderChoice.Cpu));
            Fill(ResolutionBox,
                ("Same as original", 0), ("4K (2160p)", 2160), ("1440p", 1440), ("1080p", 1080), ("720p", 720), ("480p", 480), ("360p", 360));
            Fill(FrameRateBox,
                ("Same as original", 0), ("60 fps", 60), ("30 fps", 30), ("24 fps", 24));
            Fill(AudioBox,
                ("Keep original", LevelSettings.AudioKeep), ("192 kbps", 192), ("128 kbps", 128), ("96 kbps", 96), ("64 kbps", 64), ("Remove audio", LevelSettings.AudioRemove));
        }

        private static void Fill(ComboBox box, params (string Label, int Value)[] options)
        {
            foreach (var option in options)
            {
                box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            }
        }

        private static void SelectTag(ComboBox box, int value)
        {
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == value) ?? box.Items[0];
        }

        private static int TagOf(ComboBox box) => (int)((ComboBoxItem)box.SelectedItem).Tag;

        private void AdvancedToggle_Changed(object sender, RoutedEventArgs e)
        {
            AdvancedPanel.Visibility = AdvancedToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ArrangeForWidth(_arrangedForWidth);
        }

        private void CodecBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingLevel) return;

            var level = CurrentLevel;
            level.Codec = (VideoCodec)TagOf(CodecBox);
            level.Quality = Presets.DefaultQuality(level.Codec);
            Save();
            LoadLevelIntoPanel();
        }

        private void Advanced_Changed(object sender, SelectionChangedEventArgs e)
        {
            ReadPanel();
        }

        private void Advanced_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReadPanel();
        }

        private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReadPanel();
        }

        private void ReadPanel()
        {
            if (_isLoadingLevel) return;

            var level = CurrentLevel;
            level.Encoder = (EncoderChoice)TagOf(EncoderBox);
            level.Quality = (int)QualitySlider.Value;
            level.MaxShortSide = TagOf(ResolutionBox);
            level.MaxFps = TagOf(FrameRateBox);
            level.AudioKbps = TagOf(AudioBox);
            level.TargetSizeMb = int.TryParse(TargetSizeBox.Text.Trim(), out int megabytes) && megabytes > 0 ? megabytes : 0;
            level.ExtraArgs = ExtraArgsBox.Text.Trim();

            Save();
            RefreshLevelText();
        }

        // ---- where results go ----

        private void RefreshOutputFolder()
        {
            bool hasFolder = !string.IsNullOrEmpty(_settings.OutputFolder);
            OutputFolderText.Text = hasFolder ? _settings.OutputFolder : "Same folder as the original";
            OutputFolderText.ToolTip = hasFolder ? _settings.OutputFolder : null;
            OutputFolderClear.Visibility = hasFolder ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OutputFolderChange_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Where should compressed videos be saved?", SelectedPath = _settings.OutputFolder ?? "" })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _settings.OutputFolder = dialog.SelectedPath;
            }
            Save();
            RefreshOutputFolder();
        }

        private void OutputFolderClear_Click(object sender, RoutedEventArgs e)
        {
            _settings.OutputFolder = null;
            Save();
            RefreshOutputFolder();
        }

        private void Save()
        {
            try
            {
                _settings.Save(_settingsPath);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                // Losing a preference isn't worth interrupting anyone over.
            }
        }
    }
}
