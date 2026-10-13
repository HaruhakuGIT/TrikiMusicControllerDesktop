using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;

namespace TrikiMusicController;

public partial class MainWindow : Window
{
    private readonly TrikiBleService _bleService = new();
    private readonly GestureLearningEngine _learningEngine;
    private readonly ObservableCollection<ActivityLogEntry> _logEntries = new();
    private readonly ObservableCollection<TrainingStatItem> _trainingStats = new();

    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Icon _iconConnected;
    private readonly Icon _iconBusy;
    private readonly Icon _iconDisconnected;
    private readonly Forms.ToolStripMenuItem _trayMenuOpen;
    private readonly Forms.ToolStripMenuItem _trayMenuExit;

    private readonly DispatcherTimer _flashTimer;
    private Border? _activeFlashingCard;
    private Color _activeFlashOriginalBorder;
    private Color _activeFlashOriginalBg;

    private DateTime _lastUiSensorUpdate = DateTime.MinValue;
    private bool _isRealExit;
    private bool _isUpdatingLanguageSelection;

    // Push-to-Gesture (Active Listening) & Double-Click State Machine
    private bool _isButtonPressed;
    private DateTimeOffset _buttonPressTime = DateTimeOffset.MinValue;
    private int _clickCount;
    private readonly DispatcherTimer _doubleClickTimer;
    private bool _gestureTriggeredDuringHold;

    // Recording Session State Machine
    private bool _isRecordingSession;
    private bool _isInCountdown;
    private double _sessionDurationSeconds;
    private DateTime _sessionStartTime;
    private readonly DispatcherTimer _recordingTimer;
    private readonly List<ImuSample> _recordingBuffer = new();
    private MusicAction _recordingTargetAction;
    private string _recordingActionName = "";

    public MainWindow()
    {
        var gesturesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrikiMusicController",
            "Gestures");

        _learningEngine = new GestureLearningEngine(gesturesFolder);

        InitializeComponent();
        Title = Loc.Tr("App_Title");

        GridLog.ItemsSource = _logEntries;
        GridTrainingStats.ItemsSource = _trainingStats;

        _iconConnected = LoadIcon("256g.ico");
        _iconBusy = LoadIcon("256.ico");
        _iconDisconnected = LoadIcon("256r.ico");

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _iconDisconnected,
            Visible = true,
            Text = "Triki Music Controller"
        };

        var contextMenu = new Forms.ContextMenuStrip();
        _trayMenuOpen = new Forms.ToolStripMenuItem(Loc.Tr("Tray_Open"), null, (_, _) => ShowFromTray());
        _trayMenuExit = new Forms.ToolStripMenuItem(Loc.Tr("Tray_Exit"), null, (_, _) => ExitApplication());
        contextMenu.Items.Add(_trayMenuOpen);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_trayMenuExit);
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _flashTimer.Tick += FlashTimer_Tick;

        _doubleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        _doubleClickTimer.Tick += DoubleClickTimer_Tick;

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _recordingTimer.Tick += RecordingTimer_Tick;

        // Wire BLE service events
        _bleService.StateChanged += BleService_StateChanged;
        _bleService.DeviceInfoReceived += BleService_DeviceInfoReceived;
        _bleService.SampleReceived += BleService_SampleReceived;
        _bleService.ButtonClicked += BleService_ButtonClicked;
        _bleService.ButtonStateChanged += BleService_ButtonStateChanged;
        _bleService.LogMessage += BleService_LogMessage;

        // Wire Learning Engine events
        _learningEngine.GestureRecognized += LearningEngine_GestureRecognized;
        _learningEngine.HandStateChanged += LearningEngine_HandStateChanged;

        // Listen for language changes
        Loc.LanguageChanged += OnLanguageChanged;

        // Set Language selector combo box
        InitLanguageSelector();

        UpdateTrainingStatsTable();
        UpdateModelStatusBadge();

        AddLog("System", Loc.CurrentLanguage switch
        {
            AppLanguage.En => "Ready. Turn on Triki and click 'Connect'.",
            AppLanguage.Pl => "Gotowy do pracy. Włącz Triki i kliknij «Połącz».",
            _ => "Готов к работе. Включите Triki и нажмите 'Подключить'."
        });
    }

    private void InitLanguageSelector()
    {
        _isUpdatingLanguageSelection = true;
        foreach (ComboBoxItem item in CmbLanguage.Items)
        {
            if (item.Tag?.ToString()?.Equals(Loc.CurrentLanguage.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            {
                CmbLanguage.SelectedItem = item;
                break;
            }
        }
        _isUpdatingLanguageSelection = false;
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingLanguageSelection) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem selected && selected.Tag is string tag)
        {
            if (Enum.TryParse<AppLanguage>(tag, true, out var lang))
            {
                Loc.SetLanguage(lang);
            }
        }
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            Title = Loc.Tr("App_Title");

            // Update tray context menu
            _trayMenuOpen.Text = Loc.Tr("Tray_Open");
            _trayMenuExit.Text = Loc.Tr("Tray_Exit");

            // Update connection UI state texts
            UpdateConnectionUiState(_bleService.State);

            // Update badges & action cards
            UpdateModelStatusBadge();
            UpdateTrainingStatsTable();

            // Update button badge
            if (_isButtonPressed)
            {
                TxtBtnState.Text = Loc.Tr("Button_State_Held");
            }
            else
            {
                TxtBtnState.Text = Loc.Tr("Button_State_Released");
            }

            // Update Hand State banner if not holding
            if (!_isButtonPressed && TxtHandState != null)
            {
                TxtHandState.Text = Loc.Tr("HandState_ButtonIdle");
            }

            // Update units
            var gyroUnit = Loc.Tr("Gyro_Unit");
            TxtGyroTotal.Text = $"0 {gyroUnit}";
            TxtGyroX.Text = $"0.0 {gyroUnit}";
            TxtGyroY.Text = $"0.0 {gyroUnit}";
            TxtGyroZ.Text = $"0.0 {gyroUnit}";
        });
    }

    private void UpdateConnectionUiState(ConnectionState state)
    {
        switch (state)
        {
            case ConnectionState.Connected:
                BadgeConnection.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
                TxtStatus.Text = Loc.Tr("Status_Connected");
                BtnConnect.Content = Loc.Tr("Btn_Disconnect");
                BtnConnect.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
                _trayIcon.Icon = _iconConnected;
                _trayIcon.Text = $"Triki: {Loc.Tr("Status_Connected")} ({_bleService.DeviceInfo.BatteryLevelPercent}%)";
                break;

            case ConnectionState.Scanning:
            case ConnectionState.Connecting:
                BadgeConnection.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17));
                TxtStatus.Text = state == ConnectionState.Scanning ? Loc.Tr("Status_Scanning") : Loc.Tr("Status_Connecting");
                BtnConnect.Content = Loc.Tr("Btn_Stop");
                BtnConnect.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x5A, 0x64));
                _trayIcon.Icon = _iconBusy;
                _trayIcon.Text = $"Triki: {Loc.Tr("Status_Connecting")}";
                break;

            default:
                BadgeConnection.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
                TxtStatus.Text = Loc.Tr("Status_Disconnected");
                BtnConnect.Content = Loc.Tr("Btn_Connect");
                BtnConnect.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                _trayIcon.Icon = _iconDisconnected;
                _trayIcon.Text = $"Triki: {Loc.Tr("Status_Disconnected")}";

                // Reset sensor displays to neutral when connection drops
                PbLiveTotalG.Value = 0;
                TxtLiveTotalG.Text = "0.00 G";
                PbAccelX.Value = 0; TxtAccelX.Text = "0.00 G";
                PbAccelY.Value = 0; TxtAccelY.Text = "0.00 G";
                PbAccelZ.Value = 0; TxtAccelZ.Text = "0.00 G";
                var gUnit = Loc.Tr("Gyro_Unit");
                TxtGyroTotal.Text = $"0 {gUnit}";
                PbGyroX.Value = 0; TxtGyroX.Text = $"0.0 {gUnit}";
                PbGyroY.Value = 0; TxtGyroY.Text = $"0.0 {gUnit}";
                PbGyroZ.Value = 0; TxtGyroZ.Text = $"0.0 {gUnit}";
                BadgeBtnState.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
                TxtBtnState.Text = Loc.Tr("Button_State_Released");
                TxtBtnState.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1));
                break;
        }
    }

    private void BleService_StateChanged(ConnectionState state, string message)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateConnectionUiState(state);
            AddLog("Connection", message);
        });
    }

    private void BleService_DeviceInfoReceived(TrikiDeviceInfo info)
    {
        Dispatcher.Invoke(() =>
        {
            TxtBattery.Text = info.BatteryLevelPercent.HasValue ? $"{info.BatteryLevelPercent}%" : "--%";
            AddLog("Info", $"Battery: {(info.BatteryLevelPercent.HasValue ? $"{info.BatteryLevelPercent}%" : "--")}");
        });
    }

    private void DoubleClickTimer_Tick(object? sender, EventArgs e)
    {
        _doubleClickTimer.Stop();
        _clickCount = 0;
    }

    private void BleService_ButtonStateChanged(bool isPressed)
    {
        _isButtonPressed = isPressed;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (isPressed)
            {
                _buttonPressTime = DateTimeOffset.UtcNow;
                _gestureTriggeredDuringHold = false;

                BadgeBtnState.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
                TxtBtnState.Text = Loc.Tr("Button_State_Held");
                TxtBtnState.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));

                if (TxtHandState != null && BannerHandState != null)
                {
                    BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0x00, 0x83, 0x8F));
                    TxtHandState.Text = Loc.Tr("HandState_ButtonHeld");
                    TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xF7, 0xFA));
                }
            }
            else
            {
                BadgeBtnState.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
                TxtBtnState.Text = Loc.Tr("Button_State_Released");
                TxtBtnState.Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1));

                if (TxtHandState != null && BannerHandState != null)
                {
                    BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
                    TxtHandState.Text = Loc.Tr("HandState_ButtonIdle");
                    TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
                }

                if (MainTabControl.SelectedItem == TabTrainingStudio)
                {
                    return;
                }

                if (_gestureTriggeredDuringHold)
                {
                    return;
                }

                var pressDuration = (DateTimeOffset.UtcNow - _buttonPressTime).TotalMilliseconds;
                if (pressDuration < 450)
                {
                    _clickCount++;

                    if (_clickCount == 1)
                    {
                        _doubleClickTimer.Stop();
                        _doubleClickTimer.Start();
                    }
                    else if (_clickCount >= 2)
                    {
                        _doubleClickTimer.Stop();
                        _clickCount = 0;

                        AddLog("Button", Loc.Tr("DoubleClick_Log"), "Double-Click");
                        TrikiLogger.Log("[BUTTON] Triki Double-Click -> MediaKeyController.PlayPause()");

                        FlashCard(CardPlayPause, Color.FromRgb(0x00, 0xE6, 0x76));

                        if (ChkSendMediaKeys.IsChecked == true)
                        {
                            MediaKeyController.PlayPause();
                        }
                    }
                }
            }
        });
    }

    private void BleService_ButtonClicked()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            AddLog("Button", "Triki button press", "Packet 0x22, byte[1]=1");

            if (MainTabControl.SelectedItem == TabTrainingStudio)
            {
                if (!_isRecordingSession)
                {
                    StartRecordingSession();
                }
            }
        });
    }

    private void BleService_SampleReceived(ImuSample sample)
    {
        if (_isRecordingSession && !_isInCountdown)
        {
            lock (_recordingBuffer)
            {
                _recordingBuffer.Add(sample);
            }
        }
        else
        {
            if (_isButtonPressed)
            {
                _learningEngine.ProcessSample(sample);
            }
        }

        var now = DateTime.UtcNow;
        if ((now - _lastUiSensorUpdate).TotalMilliseconds < 33) return;
        _lastUiSensorUpdate = now;

        var aMag = Math.Sqrt(sample.AccelX * sample.AccelX + sample.AccelY * sample.AccelY + sample.AccelZ * sample.AccelZ);
        var gMag = Math.Sqrt(sample.GyroX * sample.GyroX + sample.GyroY * sample.GyroY + sample.GyroZ * sample.GyroZ);

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            // Accel
            PbLiveTotalG.Value = Math.Clamp(aMag, 0, 5);
            TxtLiveTotalG.Text = $"{aMag:F2} G";

            PbAccelX.Value = Math.Clamp(sample.AccelX, -3, 3);
            PbAccelY.Value = Math.Clamp(sample.AccelY, -3, 3);
            PbAccelZ.Value = Math.Clamp(sample.AccelZ, -3, 3);
            TxtAccelX.Text = $"{sample.AccelX:F2} G";
            TxtAccelY.Text = $"{sample.AccelY:F2} G";
            TxtAccelZ.Text = $"{sample.AccelZ:F2} G";

            // Gyro
            var gUnit = Loc.Tr("Gyro_Unit");
            TxtGyroTotal.Text = $"{gMag:F0} {gUnit}";
            PbGyroX.Value = Math.Clamp(sample.GyroX, -500, 500);
            PbGyroY.Value = Math.Clamp(sample.GyroY, -500, 500);
            PbGyroZ.Value = Math.Clamp(sample.GyroZ, -500, 500);
            TxtGyroX.Text = $"{sample.GyroX:F1} {gUnit}";
            TxtGyroY.Text = $"{sample.GyroY:F1} {gUnit}";
            TxtGyroZ.Text = $"{sample.GyroZ:F1} {gUnit}";
        });
    }

    private void LearningEngine_GestureRecognized(GestureMatchResult res)
    {
        _gestureTriggeredDuringHold = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Border? targetCard = null;
            Color glowColor = Color.FromRgb(0x00, 0xE6, 0x76);
            TextBlock? confBlock = null;

            switch (res.Action)
            {
                case MusicAction.PlayPause:
                    targetCard = CardPlayPause;
                    glowColor = Color.FromRgb(0x00, 0xE6, 0x76);
                    confBlock = TxtPlayConf;
                    break;
                case MusicAction.NextTrack:
                    targetCard = CardNext;
                    glowColor = Color.FromRgb(0x00, 0xE5, 0xFF);
                    confBlock = TxtNextConf;
                    break;
                case MusicAction.PreviousTrack:
                    targetCard = CardPrev;
                    glowColor = Color.FromRgb(0x00, 0xE5, 0xFF);
                    confBlock = TxtPrevConf;
                    break;
                case MusicAction.VolumeUp:
                    targetCard = CardVolUp;
                    glowColor = Color.FromRgb(0x29, 0x79, 0xFF);
                    confBlock = TxtVolUpConf;
                    break;
                case MusicAction.VolumeDown:
                    targetCard = CardVolDown;
                    glowColor = Color.FromRgb(0x29, 0x79, 0xFF);
                    confBlock = TxtVolDownConf;
                    break;
            }

            if (confBlock != null)
            {
                confBlock.Text = $"{res.Confidence * 100:F0}%";
            }

            if (targetCard != null)
            {
                FlashCard(targetCard, glowColor);
            }

            AddLog(res.Name, res.Details, $"{res.Confidence * 100:F0}%");
        });
    }

    private void LearningEngine_HandStateChanged(HandGestureState state, string desc)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (TxtHandState != null && BannerHandState != null)
            {
                TxtHandState.Text = desc;
                switch (state)
                {
                    case HandGestureState.Ready:
                        BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
                        TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9));
                        break;
                    case HandGestureState.Cooldown:
                        BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0xBF, 0x36, 0x0C));
                        TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xBC));
                        break;
                    case HandGestureState.WaitingForHandToRest:
                        BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0x7F, 0x17));
                        TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xC4));
                        break;
                    default:
                        BannerHandState.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
                        TxtHandState.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
                        break;
                }
            }
        });
    }

    private void FlashCard(Border card, Color color)
    {
        _flashTimer.Stop();

        if (_activeFlashingCard != null)
        {
            _activeFlashingCard.Background = new SolidColorBrush(_activeFlashOriginalBg);
            _activeFlashingCard.BorderBrush = new SolidColorBrush(_activeFlashOriginalBorder);
        }

        _activeFlashingCard = card;
        _activeFlashOriginalBg = ((SolidColorBrush)card.Background).Color;
        _activeFlashOriginalBorder = ((SolidColorBrush)card.BorderBrush).Color;

        card.Background = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
        card.BorderBrush = new SolidColorBrush(color);

        _flashTimer.Start();
    }

    private void FlashTimer_Tick(object? sender, EventArgs e)
    {
        _flashTimer.Stop();
        if (_activeFlashingCard != null)
        {
            _activeFlashingCard.Background = new SolidColorBrush(_activeFlashOriginalBg);
            _activeFlashingCard.BorderBrush = new SolidColorBrush(_activeFlashOriginalBorder);
            _activeFlashingCard = null;
        }
    }

    // RECORDING SESSION LOGIC
    private void BtnRecord_Click(object sender, RoutedEventArgs e)
    {
        StartRecordingSession();
    }

    private void StartRecordingSession()
    {
        if (_isRecordingSession) return;

        if (_bleService.State != ConnectionState.Connected)
        {
            System.Windows.MessageBox.Show(
                Loc.Tr("Msg_ConnectFirst"),
                "Triki Music Controller",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Determine target action
        if (RbPlayPause.IsChecked == true) { _recordingTargetAction = MusicAction.PlayPause; _recordingActionName = Loc.Tr("Action_PlayPause"); }
        else if (RbNext.IsChecked == true) { _recordingTargetAction = MusicAction.NextTrack; _recordingActionName = Loc.Tr("Action_Next"); }
        else if (RbPrev.IsChecked == true) { _recordingTargetAction = MusicAction.PreviousTrack; _recordingActionName = Loc.Tr("Action_Prev"); }
        else if (RbVolUp.IsChecked == true) { _recordingTargetAction = MusicAction.VolumeUp; _recordingActionName = Loc.Tr("Action_VolUp"); }
        else if (RbVolDown.IsChecked == true) { _recordingTargetAction = MusicAction.VolumeDown; _recordingActionName = Loc.Tr("Action_VolDown"); }
        else { _recordingTargetAction = MusicAction.None; _recordingActionName = Loc.Tr("Action_Idle"); }

        // Determine session duration
        _sessionDurationSeconds = RbDur5.IsChecked == true ? 5.0 : (RbDur10.IsChecked == true ? 10.0 : 7.0);

        _isRecordingSession = true;
        _isInCountdown = true;
        _recordingBuffer.Clear();

        BtnRecord.IsEnabled = false;
        BtnRecord.Background = new SolidColorBrush(Color.FromRgb(0x45, 0x5A, 0x64));
        TxtRecordStatus.Text = Loc.Tr("Msg_CountdownReady", 3);
        TxtRecordCountdown.Text = "3...";
        PbRecordProgress.Value = 0;

        _sessionStartTime = DateTime.Now.AddSeconds(3);
        _recordingTimer.Start();
    }

    private void RecordingTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;

        if (_isInCountdown)
        {
            var timeLeft = (_sessionStartTime - now).TotalSeconds;
            if (timeLeft > 0)
            {
                var seconds = (int)Math.Ceiling(timeLeft);
                TxtRecordStatus.Text = Loc.Tr("Msg_CountdownReady", seconds);
                TxtRecordCountdown.Text = $"{seconds}...";
                PbRecordProgress.Value = (3.0 - timeLeft) / 3.0 * 20.0;
                return;
            }

            // Countdown finished, START RECORDING
            _isInCountdown = false;
            _sessionStartTime = now;
            TxtRecordStatus.Text = _recordingTargetAction == MusicAction.None
                ? Loc.Tr("Msg_RecordingRest")
                : Loc.Tr("Msg_RecordingAction", _recordingActionName);
        }

        var elapsed = (now - _sessionStartTime).TotalSeconds;
        var progressPercent = Math.Clamp(elapsed / _sessionDurationSeconds * 100.0, 0, 100);
        PbRecordProgress.Value = progressPercent;
        var rem = Math.Max(0, _sessionDurationSeconds - elapsed);
        var secUnit = Loc.CurrentLanguage switch { AppLanguage.En => "sec", AppLanguage.Pl => "sek", _ => "сек" };
        TxtRecordCountdown.Text = $"{rem:F1} {secUnit}";

        if (elapsed >= _sessionDurationSeconds)
        {
            _recordingTimer.Stop();
            _isRecordingSession = false;
            BtnRecord.IsEnabled = true;
            BtnRecord.Background = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
            TxtRecordCountdown.Text = Loc.Tr("Step2_StatusReady");
            PbRecordProgress.Value = 100;

            FinishRecordingSession();
        }
    }

    private void FinishRecordingSession()
    {
        List<ImuSample> samplesCopy;
        lock (_recordingBuffer)
        {
            samplesCopy = new List<ImuSample>(_recordingBuffer);
        }

        if (samplesCopy.Count < 15)
        {
            TxtRecordStatus.Text = Loc.Tr("Msg_FewSamples");
            return;
        }

        var extractedGestures = RecordedGesture.SegmentRepetitions(_recordingActionName, _recordingTargetAction, samplesCopy);

        foreach (var g in extractedGestures)
        {
            _learningEngine.Storage.SaveRecording(g);
        }

        var peakG = extractedGestures.Count > 0 ? extractedGestures.Max(g => g.PeakAccelG) : 0;
        var peakGyro = extractedGestures.Count > 0 ? extractedGestures.Max(g => g.PeakGyroDegPerSec) : 0;

        TxtLastRecordedInfo.Text = Loc.Tr("Msg_ExtractedReps", extractedGestures.Count, peakG, peakGyro, Loc.Tr("Gyro_Unit"));
        TxtRecordStatus.Text = Loc.Tr("Msg_RecordingDone", extractedGestures.Count);

        AddLog("Training", $"Saved {extractedGestures.Count} reps for '{_recordingActionName}' (Peak: {peakG:F2} G)");

        _learningEngine.TrainFromSavedRecordings();
        UpdateTrainingStatsTable();
        UpdateModelStatusBadge();
    }

    private void BtnTrainModel_Click(object sender, RoutedEventArgs e)
    {
        _learningEngine.TrainFromSavedRecordings();
        UpdateTrainingStatsTable();
        UpdateModelStatusBadge();

        AddLog("Model", $"Model retrained on {_learningEngine.TotalRecordingsCount} samples!");
        System.Windows.MessageBox.Show(
            Loc.Tr("Msg_TrainSuccess", _learningEngine.TotalRecordingsCount),
            Loc.Tr("Msg_TrainTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnClearRecordings_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(
            Loc.Tr("Msg_ClearConfirm"),
            Loc.Tr("Msg_ClearTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _learningEngine.Storage.ClearAllRecordings();
            _learningEngine.TrainFromSavedRecordings();
            UpdateTrainingStatsTable();
            UpdateModelStatusBadge();
            TxtLastRecordedInfo.Text = Loc.Tr("Msg_Cleared");
            AddLog("Training", "All recordings cleared. Model reset to default.");
        }
    }

    private void UpdateTrainingStatsTable()
    {
        var recordings = _learningEngine.Storage.LoadAllRecordings();
        _trainingStats.Clear();

        var actions = new[]
        {
            (MusicAction.PlayPause, Loc.Tr("Action_PlayPause")),
            (MusicAction.NextTrack, Loc.Tr("Action_Next")),
            (MusicAction.PreviousTrack, Loc.Tr("Action_Prev")),
            (MusicAction.VolumeUp, Loc.Tr("Action_VolUp")),
            (MusicAction.VolumeDown, Loc.Tr("Action_VolDown")),
            (MusicAction.None, Loc.Tr("Action_Idle"))
        };

        var gyroUnit = Loc.Tr("Gyro_Unit");
        foreach (var (act, name) in actions)
        {
            var matching = recordings.Where(r => r.TargetAction == act).ToList();
            var count = matching.Count;
            var maxG = count > 0 ? matching.Max(r => r.PeakAccelG) : 0;
            var maxGyro = count > 0 ? matching.Max(r => r.PeakGyroDegPerSec) : 0;

            _trainingStats.Add(new TrainingStatItem
            {
                ActionName = name,
                SampleCount = count,
                PeakGFormatted = count > 0 ? $"{maxG:F2} G" : "--",
                PeakGyroFormatted = count > 0 ? $"{maxGyro:F0} {gyroUnit}" : "--",
                Status = count >= 3 ? Loc.Tr("Status_Trained_Good") : (count > 0 ? Loc.Tr("Status_Trained_NeedMore") : Loc.Tr("Status_Trained_None"))
            });
        }
    }

    private void UpdateModelStatusBadge()
    {
        var trainedCount = _learningEngine.TrainedActionsCount;
        if (trainedCount > 0)
        {
            BadgeModelStatus.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20));
            var idleText = _learningEngine.HasIdleCalibration
                ? (Loc.CurrentLanguage switch { AppLanguage.Pl => "szum dłoni skalibrowany", AppLanguage.En => "hand tremor calibrated", _ => "шум руки откалиброван" })
                : (Loc.CurrentLanguage switch { AppLanguage.Pl => "szum dłoni nie nagrany", AppLanguage.En => "hand tremor not recorded", _ => "шум руки не записан" });

            var trainedLabel = Loc.CurrentLanguage switch
            {
                AppLanguage.Pl => $"🧠 Wyuczono: {trainedCount}/5 gestów ({_learningEngine.TotalRecordingsCount} próbek, {idleText})",
                AppLanguage.En => $"🧠 Trained: {trainedCount}/5 gestures ({_learningEngine.TotalRecordingsCount} samples, {idleText})",
                _ => $"🧠 Обучено: {trainedCount}/5 жестов ({_learningEngine.TotalRecordingsCount} образцов, {idleText})"
            };
            TxtModelStatus.Text = trainedLabel;
            TxtModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9));
        }
        else
        {
            BadgeModelStatus.Background = new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F));
            TxtModelStatus.Text = Loc.Tr("Model_Status_Clean");
            TxtModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC));
        }

        SetCardTrainedVisual(CardPlayPause, TxtPlayDesc, MusicAction.PlayPause, Loc.Tr("Action_PlayPause_Desc"));
        SetCardTrainedVisual(CardNext, TxtNextDesc, MusicAction.NextTrack, Loc.Tr("Action_Next_Desc"));
        SetCardTrainedVisual(CardPrev, TxtPrevDesc, MusicAction.PreviousTrack, Loc.Tr("Action_Prev_Desc"));
        SetCardTrainedVisual(CardVolUp, TxtVolUpDesc, MusicAction.VolumeUp, Loc.Tr("Action_VolUp_Desc"));
        SetCardTrainedVisual(CardVolDown, TxtVolDownDesc, MusicAction.VolumeDown, Loc.Tr("Action_VolDown_Desc"));
    }

    private void SetCardTrainedVisual(Border? card, TextBlock? desc, MusicAction action, string actionName)
    {
        if (card == null || desc == null) return;
        var isTrained = _learningEngine.IsActionTrained(action);
        if (isTrained)
        {
            card.Opacity = 1.0;
            desc.Text = $"🟢 {actionName} {Loc.Tr("Action_TrainedSuffix")}";
            desc.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        }
        else
        {
            card.Opacity = 0.35;
            desc.Text = $"⚪ {actionName} {Loc.Tr("Action_DisabledSuffix")}";
            desc.Foreground = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C));
        }
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_bleService.State == ConnectionState.Connected ||
            _bleService.State == ConnectionState.Scanning ||
            _bleService.State == ConnectionState.Connecting)
        {
            await _bleService.StopAsync();
        }
        else
        {
            _bleService.Start();
        }
    }

    private void BtnMinimizeTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayIcon.ShowBalloonTip(1500, "Triki Music Controller", Loc.Tr("Tray_Balloon_Text"), Forms.ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isRealExit = true;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Task.Run(() => _bleService.StopAsync());
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isRealExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        _logEntries.Clear();
    }

    private void BtnOpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(TrikiLogger.FilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TrikiLogger.FilePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to open log: {ex.Message}");
        }
    }

    private void ChkSendMediaKeys_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkSendMediaKeys != null && _learningEngine != null)
            _learningEngine.SendSystemMediaKeys = ChkSendMediaKeys.IsChecked == true;
    }

    private void ChkAutoReconnect_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkAutoReconnect != null && _bleService != null)
            _bleService.AutoReconnect = ChkAutoReconnect.IsChecked == true;
    }

    private void BleService_LogMessage(string msg)
    {
        AddLog("BLE", msg);
    }

    private void AddLog(string category, string message, string? value = null)
    {
        TrikiLogger.Log($"[{category}] {message} {(value != null ? $"| {value}" : "")}");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => AddLog(category, message, value));
            return;
        }

        var entry = new ActivityLogEntry
        {
            Category = category,
            Message = message,
            Value = value
        };

        _logEntries.Insert(0, entry);
        if (_logEntries.Count > 100)
        {
            _logEntries.RemoveAt(_logEntries.Count - 1);
        }
    }

    private static Icon LoadIcon(string fileName)
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ico", fileName);
        if (File.Exists(localPath)) return new Icon(localPath);

        var devPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "ico", fileName);
        if (File.Exists(devPath)) return new Icon(devPath);

        return SystemIcons.Application;
    }
}
