namespace TrikiMusicController;

public enum HandGestureState
{
    NotTrained,
    Ready,
    ActionFired,
    Cooldown,
    WaitingForHandToRest
}

public sealed class GestureMatchResult
{
    public MusicAction Action { get; set; }
    public string Name { get; set; } = "";
    public double Confidence { get; set; } // 0.0 to 1.0
    public string Details { get; set; } = "";
}

public sealed class GestureLearningEngine
{
    private readonly GestureProfileStorage _storage;
    private readonly Dictionary<MusicAction, GestureProfile> _profiles = new();

    // Noise Gate: ONLY calibrated when user trains "Покой / Шум руки"
    public double? IdleNoiseMaxAccelG { get; private set; }
    public double? IdleNoiseMaxGyroDegPerSec { get; private set; }

    public bool HasIdleCalibration => IdleNoiseMaxAccelG.HasValue && IdleNoiseMaxGyroDegPerSec.HasValue;
    public int TrainedActionsCount => _profiles.Count;
    public int TotalRecordingsCount { get; private set; }

    public HandGestureState HandState { get; private set; } = HandGestureState.NotTrained;
    public string HandStateDescription { get; private set; } = "⚪ Ожидание обучения";

    public event Action<GestureMatchResult>? GestureRecognized;
    public event Action<HandGestureState, string>? HandStateChanged;

    // Cooldown & Return-to-Neutral Timing
    private DateTimeOffset _lastTriggerTime = DateTimeOffset.MinValue;
    private DateTimeOffset _calmStartTime = DateTimeOffset.MinValue;
    private bool _isArmed;

    // Track baseline spatial orientation (gravity vector when hand is calm)
    private double _lastRestX = 0;
    private double _lastRestY = 0;
    private double _lastRestZ = 1.0;

    private const int CooldownIntervalMs = 900; // 0.9 секунды на свободный возврат руки
    private const int RequiredCalmDurationMs = 150; // 150мс спокойствия руки перед взводом

    // Volume tracking
    private double _volumeAccumulator;
    private DateTimeOffset _lastVolumeStepTime = DateTimeOffset.MinValue;

    public bool SendSystemMediaKeys { get; set; } = true;

    public GestureLearningEngine(string recordingsFolderPath)
    {
        _storage = new GestureProfileStorage(recordingsFolderPath);
        TrainFromSavedRecordings();
    }

    public GestureProfileStorage Storage => _storage;

    public bool IsActionTrained(MusicAction action) => _profiles.ContainsKey(action);

    public void TrainFromSavedRecordings()
    {
        var recordings = _storage.LoadAllRecordings();
        TotalRecordingsCount = recordings.Count;
        _profiles.Clear();
        IdleNoiseMaxAccelG = null;
        IdleNoiseMaxGyroDegPerSec = null;

        TrikiLogger.Log($"[TRAIN] Загружено {recordings.Count} сохраненных файлов движений");

        if (recordings.Count == 0)
        {
            UpdateHandState(HandGestureState.NotTrained, "⚪ Система чистая: запишите жесты в студии обучения");
            return;
        }

        // 1. Learn Idle Noise Gate if recorded by user
        var idles = recordings.Where(r => r.TargetAction == MusicAction.None).ToList();
        if (idles.Count > 0)
        {
            IdleNoiseMaxAccelG = Math.Max(1.25, idles.Max(r => r.PeakAccelG) * 1.20);
            IdleNoiseMaxGyroDegPerSec = Math.Max(70.0, idles.Max(r => r.PeakGyroDegPerSec) * 1.20);
            TrikiLogger.Log($"[TRAIN] Откалиброван шум руки: Accel={IdleNoiseMaxAccelG:F2}G, Gyro={IdleNoiseMaxGyroDegPerSec:F0}°/с");
        }

        // 2. Train distinct profiles ONLY from user-recorded actions
        var grouped = recordings.Where(r => r.TargetAction != MusicAction.None).GroupBy(r => r.TargetAction);
        foreach (var group in grouped)
        {
            var action = group.Key;
            var list = group.ToList();

            // Calculate directional average vector of impact
            double sumDirX = 0, sumDirY = 0, sumDirZ = 0;
            // Calculate baseline spatial orientation vector at rest
            double sumRestX = 0, sumRestY = 0, sumRestZ = 0;

            foreach (var r in list)
            {
                var dir = r.GetPeakDirection();
                sumDirX += dir.x;
                sumDirY += dir.y;
                sumDirZ += dir.z;

                var rest = r.GetRestOrientation();
                sumRestX += rest.x;
                sumRestY += rest.y;
                sumRestZ += rest.z;
            }

            var dirMag = Math.Sqrt(sumDirX * sumDirX + sumDirY * sumDirY + sumDirZ * sumDirZ) + 0.0001;
            var avgDirX = sumDirX / dirMag;
            var avgDirY = sumDirY / dirMag;
            var avgDirZ = sumDirZ / dirMag;

            var restMag = Math.Sqrt(sumRestX * sumRestX + sumRestY * sumRestY + sumRestZ * sumRestZ) + 0.0001;
            var avgRestX = sumRestX / restMag;
            var avgRestY = sumRestY / restMag;
            var avgRestZ = sumRestZ / restMag;

            var prof = new GestureProfile
            {
                Action = action,
                Name = list[0].ActionName,
                SampleCount = list.Count,
                MinPeakAccelG = list.Min(r => r.PeakAccelG),
                MaxPeakAccelG = list.Max(r => r.PeakAccelG),
                AvgPeakAccelG = list.Average(r => r.PeakAccelG),
                MinPeakGyro = list.Min(r => r.PeakGyroDegPerSec),
                AvgPeakGyro = list.Average(r => r.PeakGyroDegPerSec),
                DominantAxisZRatio = list.Average(r => r.DominantGyroZ),
                DominantAxisYRatio = list.Average(r => r.DominantGyroY),
                DominantAxisXRatio = list.Average(r => r.DominantGyroX),
                AvgDurationMs = list.Average(r => r.DurationMs),
                DirX = avgDirX,
                DirY = avgDirY,
                DirZ = avgDirZ,
                RestDirX = avgRestX,
                RestDirY = avgRestY,
                RestDirZ = avgRestZ,
                AvgAccelX = avgDirX * list.Average(r => r.PeakAccelG),
                AvgAccelY = avgDirY * list.Average(r => r.PeakAccelG),
                AvgAccelZ = avgDirZ * list.Average(r => r.PeakAccelG)
            };

            _profiles[action] = prof;
            TrikiLogger.Log($"[TRAIN] Обучен жест {action}: {list.Count} образцов. Направление: ({prof.DirX:F2}, {prof.DirY:F2}, {prof.DirZ:F2}), Исходная ориентация Triki: ({prof.RestDirX:F2}, {prof.RestDirY:F2}, {prof.RestDirZ:F2})");
        }

        _isArmed = true;
        UpdateHandState(HandGestureState.Ready, "🟢 Готов к жесту (в руке)");
    }

    public void ProcessSample(ImuSample sample)
    {
        // STRICT RULE: If NO gestures have been trained by the user, DO ABSOLUTELY NOTHING.
        if (_profiles.Count == 0)
        {
            return;
        }

        var now = sample.TimestampUtc;
        var ax = sample.AccelX;
        var ay = sample.AccelY;
        var az = sample.AccelZ;
        var accelMag = Math.Sqrt(ax * ax + ay * ay + az * az);
        var gyroMag = Math.Sqrt(sample.GyroX * sample.GyroX + sample.GyroY * sample.GyroY + sample.GyroZ * sample.GyroZ);

        // 1. NOISE GATE: If motion is below user's calibrated hand noise floor, ignore completely!
        if (HasIdleCalibration)
        {
            if (accelMag <= IdleNoiseMaxAccelG!.Value && gyroMag <= IdleNoiseMaxGyroDegPerSec!.Value)
            {
                _volumeAccumulator = 0;
            }
        }

        // 2. RETURN-TO-NEUTRAL / COOLDOWN PROTECTION GATE
        if (!_isArmed)
        {
            var msSinceLast = (now - _lastTriggerTime).TotalMilliseconds;
            if (msSinceLast < CooldownIntervalMs)
            {
                var leftSeconds = (CooldownIntervalMs - msSinceLast) / 1000.0;
                UpdateHandState(HandGestureState.Cooldown, $"⏳ Возврат руки в исходную позицию ({leftSeconds:F1} сек)...");
                _volumeAccumulator = 0;
                return;
            }

            // Return to neutral threshold: at rest hand is ~1.0G, slight tremor up to ~1.35G
            var calmAccelLimit = 1.45;
            var calmGyroLimit = 60.0;
            var isCalm = accelMag < calmAccelLimit && gyroMag < calmGyroLimit;

            if (!isCalm)
            {
                _calmStartTime = DateTimeOffset.MinValue;
                UpdateHandState(HandGestureState.WaitingForHandToRest, "⏳ Успокойте руку в исходной позиции...");
                return;
            }

            if (_calmStartTime == DateTimeOffset.MinValue)
            {
                _calmStartTime = now;
                return;
            }

            if ((now - _calmStartTime).TotalMilliseconds < RequiredCalmDurationMs)
            {
                return;
            }

            // Hand is stabilized in start position! Now armed!
            _isArmed = true;
            _lastRestX = ax / (accelMag + 0.0001);
            _lastRestY = ay / (accelMag + 0.0001);
            _lastRestZ = az / (accelMag + 0.0001);

            UpdateHandState(HandGestureState.Ready, "🟢 Рука в исходной позиции (ГОТОВ К ЖЕСТУ)");
            TrikiLogger.Log($"[ARM] Система взведена! Исходное положение Triki в руке: ({_lastRestX:F2}, {_lastRestY:F2}, {_lastRestZ:F2})");
        }
        else
        {
            // While armed and relatively calm before a rapid burst, continually update the rest orientation
            if (accelMag < 1.35 && gyroMag < 40.0)
            {
                _lastRestX = ax / (accelMag + 0.0001);
                _lastRestY = ay / (accelMag + 0.0001);
                _lastRestZ = az / (accelMag + 0.0001);
            }
        }

        // 3. Evaluate ONLY trained profiles when armed:

        // A. PLAY / PAUSE (DIRECTIONAL CHECK + ORIENTATION CHECK + FORCE CHECK)
        if (_profiles.TryGetValue(MusicAction.PlayPause, out var playProf))
        {
            var thresholdG = Math.Max(1.65, Math.Min(playProf.MinPeakAccelG * 0.80, playProf.AvgPeakAccelG * 0.65));

            if (accelMag >= thresholdG)
            {
                // CRITICAL DIRECTION CHECK: Cosine similarity with user's trained downward whip vector!
                var cosImpact = playProf.ComputeDirectionCosine(ax, ay, az);
                // CRITICAL ORIENTATION CHECK: Was Triki held horizontally or vertically as trained?
                var cosRest = playProf.ComputeRestOrientationCosine(_lastRestX, _lastRestY, _lastRestZ);

                // Both must align: impact direction within ~58° (cos >= 0.52) AND rest orientation within ~65° (cos >= 0.42)
                if (cosImpact >= 0.52 && cosRest >= 0.42)
                {
                    var deviation = Math.Abs(accelMag - playProf.AvgPeakAccelG) / (playProf.AvgPeakAccelG + 0.1);
                    var confidence = Math.Clamp((1.0 - deviation * 0.5) * (0.5 + 0.5 * cosImpact), 0.75, 0.99);

                    TrikiLogger.Log($"[MATCH PlayPause] Сила: {accelMag:F2}G >= {thresholdG:F2}G, Удар Cos={cosImpact:F2}, Положение Triki Cos={cosRest:F2}! Уверенность: {confidence * 100:F0}%");

                    Trigger(MusicAction.PlayPause, playProf.Name, confidence,
                        $"Взмах совпал (Сила: {accelMag:F2} G, Направление Cos={cosImpact:F2}, Ориентация Cos={cosRest:F2})");

                    _lastTriggerTime = now;
                    _isArmed = false;
                    _calmStartTime = DateTimeOffset.MinValue;
                    _volumeAccumulator = 0;
                    return;
                }
                else
                {
                    // Rejected because of wrong impact angle or wrong posture in hand!
                    var angleImpact = Math.Acos(Math.Clamp(cosImpact, -1.0, 1.0)) * (180.0 / Math.PI);
                    var angleRest = Math.Acos(Math.Clamp(cosRest, -1.0, 1.0)) * (180.0 / Math.PI);
                    TrikiLogger.Log($"[REJECT PlayPause] Отсечено! Сила={accelMag:F2}G, Угол удара={angleImpact:F0}°, Положение Triki в руке={angleRest:F0}° (нужно совпадение обоих)");
                }
            }
        }

        // B. NEXT TRACK (Trained 3D Direction, Rest Posture, or Gyro Flick)
        if (_profiles.TryGetValue(MusicAction.NextTrack, out var nextProf))
        {
            var cosImpact = nextProf.ComputeDirectionCosine(ax, ay, az);
            var cosRest = nextProf.ComputeRestOrientationCosine(_lastRestX, _lastRestY, _lastRestZ);

            var lateralRate = sample.GyroY;
            if (Math.Abs(sample.GyroX) > Math.Abs(lateralRate)) lateralRate = sample.GyroX;

            var thresholdG = Math.Max(1.5, Math.Min(nextProf.MinPeakAccelG * 0.75, nextProf.AvgPeakAccelG * 0.65));
            var thresholdGyro = Math.Max(70.0, Math.Min(nextProf.MinPeakGyro * 0.70, nextProf.AvgPeakGyro * 0.60));

            // Matches either: trained 3D direction vector alignment with matching hand posture OR trained gyro rotation
            var matchesDirection = (accelMag >= thresholdG && cosImpact >= 0.50 && cosRest >= 0.40);
            var matchesGyro = (nextProf.AvgPeakGyro >= 80.0 && lateralRate >= thresholdGyro && cosRest >= 0.35);

            if (matchesDirection || matchesGyro)
            {
                var confidence = matchesDirection 
                    ? Math.Clamp(0.70 + 0.30 * cosImpact, 0.75, 0.99)
                    : Math.Clamp(lateralRate / (nextProf.AvgPeakGyro + 1.0), 0.75, 0.98);

                TrikiLogger.Log($"[MATCH NextTrack] Сила={accelMag:F2}G (Удар Cos={cosImpact:F2}, Положение Cos={cosRest:F2}), ω={lateralRate:F0}°/с");

                Trigger(MusicAction.NextTrack, nextProf.Name, confidence,
                    $"Обученный взмах «Вперёд» (Угол={cosImpact:F2}, Положение={cosRest:F2}, ω={lateralRate:F0}°/с)");
                _lastTriggerTime = now;
                _isArmed = false;
                _calmStartTime = DateTimeOffset.MinValue;
                _volumeAccumulator = 0;
                return;
            }
        }

        // C. PREVIOUS TRACK (Trained 3D Direction, Rest Posture, or Gyro Flick)
        if (_profiles.TryGetValue(MusicAction.PreviousTrack, out var prevProf))
        {
            var cosImpact = prevProf.ComputeDirectionCosine(ax, ay, az);
            var cosRest = prevProf.ComputeRestOrientationCosine(_lastRestX, _lastRestY, _lastRestZ);

            var lateralRate = sample.GyroY;
            if (Math.Abs(sample.GyroX) > Math.Abs(lateralRate)) lateralRate = sample.GyroX;

            var thresholdG = Math.Max(1.5, Math.Min(prevProf.MinPeakAccelG * 0.75, prevProf.AvgPeakAccelG * 0.65));
            var thresholdGyro = Math.Max(70.0, Math.Min(prevProf.MinPeakGyro * 0.70, prevProf.AvgPeakGyro * 0.60));

            var matchesDirection = (accelMag >= thresholdG && cosImpact >= 0.50 && cosRest >= 0.40);
            var matchesGyro = (prevProf.AvgPeakGyro >= 80.0 && -lateralRate >= thresholdGyro && cosRest >= 0.35);

            if (matchesDirection || matchesGyro)
            {
                var confidence = matchesDirection
                    ? Math.Clamp(0.70 + 0.30 * cosImpact, 0.75, 0.99)
                    : Math.Clamp(-lateralRate / (prevProf.AvgPeakGyro + 1.0), 0.75, 0.98);

                TrikiLogger.Log($"[MATCH PrevTrack] Сила={accelMag:F2}G (Удар Cos={cosImpact:F2}, Положение Cos={cosRest:F2}), ω={-lateralRate:F0}°/с");

                Trigger(MusicAction.PreviousTrack, prevProf.Name, confidence,
                    $"Обученный взмах «Назад» (Угол={cosImpact:F2}, Положение={cosRest:F2}, ω={-lateralRate:F0}°/с)");
                _lastTriggerTime = now;
                _isArmed = false;
                _calmStartTime = DateTimeOffset.MinValue;
                _volumeAccumulator = 0;
                return;
            }
        }

        // D. VOLUME UP / DOWN (Finger spin around Z)
        ProcessTrainedVolume(now, sample, accelMag);
    }

    private void ProcessTrainedVolume(DateTimeOffset now, ImuSample sample, double accelMag)
    {
        var hasVolUp = _profiles.TryGetValue(MusicAction.VolumeUp, out var volUpProf);
        var hasVolDown = _profiles.TryGetValue(MusicAction.VolumeDown, out var volDownProf);

        if (!hasVolUp && !hasVolDown) return;

        if (accelMag > 1.6)
        {
            _volumeAccumulator = 0;
            return;
        }

        var spinSpeed = sample.GyroZ;
        var minSpeed = Math.Min(
            hasVolUp ? volUpProf!.MinPeakGyro * 0.35 : 1000,
            hasVolDown ? volDownProf!.MinPeakGyro * 0.35 : 1000);

        if (minSpeed > 500) minSpeed = 25.0;

        if (Math.Abs(spinSpeed) >= minSpeed)
        {
            const double dt = 0.02;
            _volumeAccumulator += spinSpeed * dt;

            const double stepDegrees = 14.0;
            if (Math.Abs(_volumeAccumulator) >= stepDegrees)
            {
                var msSinceStep = (now - _lastVolumeStepTime).TotalMilliseconds;
                if (msSinceStep >= 80)
                {
                    _lastVolumeStepTime = now;
                    if (_volumeAccumulator > 0 && hasVolUp)
                    {
                        Trigger(MusicAction.VolumeUp, volUpProf!.Name, 0.95, $"Обученное кручение вправо (Скорость: {spinSpeed:F0}°/с)");
                        _volumeAccumulator -= stepDegrees;
                    }
                    else if (_volumeAccumulator < 0 && hasVolDown)
                    {
                        Trigger(MusicAction.VolumeDown, volDownProf!.Name, 0.95, $"Обученное кручение влево (Скорость: {-spinSpeed:F0}°/с)");
                        _volumeAccumulator += stepDegrees;
                    }
                }
            }
        }
        else
        {
            _volumeAccumulator = 0;
        }
    }

    private void Trigger(MusicAction action, string name, double confidence, string details)
    {
        if (SendSystemMediaKeys)
        {
            try
            {
                switch (action)
                {
                    case MusicAction.PlayPause:
                        MediaKeyController.PlayPause();
                        break;
                    case MusicAction.NextTrack:
                        MediaKeyController.NextTrack();
                        break;
                    case MusicAction.PreviousTrack:
                        MediaKeyController.PreviousTrack();
                        break;
                    case MusicAction.VolumeUp:
                        MediaKeyController.VolumeUp();
                        break;
                    case MusicAction.VolumeDown:
                        MediaKeyController.VolumeDown();
                        break;
                }
            }
            catch (Exception ex)
            {
                TrikiLogger.Log($"[ERROR] Ошибка отправки медиа-клавиши: {ex.Message}");
            }
        }

        UpdateHandState(HandGestureState.ActionFired, $"⚡ Сработало: {name} (Возврат руки...)");

        GestureRecognized?.Invoke(new GestureMatchResult
        {
            Action = action,
            Name = name,
            Confidence = confidence,
            Details = details
        });
    }

    private void UpdateHandState(HandGestureState state, string desc)
    {
        if (HandState != state || HandStateDescription != desc)
        {
            HandState = state;
            HandStateDescription = desc;
            HandStateChanged?.Invoke(state, desc);
        }
    }

    public void Reset()
    {
        _volumeAccumulator = 0;
        _isArmed = _profiles.Count > 0;
        _calmStartTime = DateTimeOffset.MinValue;
    }
}
