namespace TrikiMusicController;

public sealed class GestureDetectionEventArgs : EventArgs
{
    public MusicAction Action { get; }
    public string Description { get; }
    public double MetricValue { get; }

    public GestureDetectionEventArgs(MusicAction action, string description, double metricValue = 0)
    {
        Action = action;
        Description = description;
        MetricValue = metricValue;
    }
}

public sealed class GestureSettings
{
    // Downward Whip / Strong Hand Flick Down (Play/Pause)
    public double DownwardWhipThresholdG { get; set; } = 2.2; // Total acceleration peak (G)
    public int DownwardWhipCooldownMs { get; set; } = 750;

    // Wrist Flick Left/Right (Next / Previous Track)
    public double FlickSpeedThresholdDegPerSec { get; set; } = 220.0;
    public int FlickCooldownMs { get; set; } = 650;

    // Finger Spin (Volume Up / Down - twisting cap between fingers)
    public double VolumeMinSpeedDegPerSec { get; set; } = 20.0;
    public double VolumeDegreesPerStep { get; set; } = 12.0;
    public int VolumeStepCooldownMs { get; set; } = 70;

    // Global toggle
    public bool SendSystemMediaKeys { get; set; } = true;
}

public sealed class MusicGestureProcessor
{
    public GestureSettings Settings { get; } = new();

    public event EventHandler<GestureDetectionEventArgs>? GestureDetected;

    private DateTimeOffset? _lastSampleTimestamp;

    // Volume tracking (twisting between fingers)
    private double _volumeAngleAccumulator;
    private DateTimeOffset _lastVolumeMotionTime = DateTimeOffset.MinValue;
    private DateTimeOffset _lastVolumeStepTime = DateTimeOffset.MinValue;

    // Downward Whip (Play / Pause)
    private DateTimeOffset _lastWhipTime = DateTimeOffset.MinValue;
    private bool _whipArmed = true;
    public double LastWhipForceG { get; private set; }

    // Wrist Flick (Next / Prev)
    private DateTimeOffset _lastFlickTime = DateTimeOffset.MinValue;
    private bool _flickArmed = true;

    // Live Metrics for UI
    public double CurrentPitch { get; private set; }
    public double CurrentRoll { get; private set; }
    public double CurrentTotalG { get; private set; }

    public void ProcessSample(ImuSample sample)
    {
        var now = sample.TimestampUtc;
        var dt = _lastSampleTimestamp.HasValue
            ? Math.Max(0.001, Math.Min(0.1, (now - _lastSampleTimestamp.Value).TotalSeconds))
            : 0.02;
        _lastSampleTimestamp = now;

        var ax = sample.AccelX;
        var ay = sample.AccelY;
        var az = sample.AccelZ;
        var accelMag = Math.Sqrt(ax * ax + ay * ay + az * az);
        CurrentTotalG = accelMag;

        if (accelMag > 0.01)
        {
            CurrentPitch = Math.Atan2(-ay, Math.Sqrt(ax * ax + az * az)) * (180.0 / Math.PI);
            CurrentRoll = Math.Atan2(ax, az) * (180.0 / Math.PI);
        }

        // 1. Strong Downward Whip Check (Play / Pause)
        ProcessDownwardWhip(now, accelMag, sample);

        // 2. Wrist Flick Left / Right (Track Prev / Next)
        ProcessFlickDetection(now, sample, accelMag);

        // 3. Twisting cap between fingers (Volume Up / Down)
        ProcessFingerSpinVolume(now, dt, sample, accelMag);
    }

    private void ProcessDownwardWhip(DateTimeOffset now, double accelMag, ImuSample sample)
    {
        var msSinceLastWhip = (now - _lastWhipTime).TotalMilliseconds;

        if (msSinceLastWhip < Settings.DownwardWhipCooldownMs)
        {
            _whipArmed = false;
            return;
        }

        // Rearm when motion settles back below 1.4 G
        if (accelMag < 1.4)
        {
            _whipArmed = true;
        }

        // Detect strong whip down:
        // A strong downward hand whip generates a sharp peak in total acceleration
        // (usually 2.0g - 4.0g at the bottom deceleration) + rapid rotational change
        if (_whipArmed && accelMag >= Settings.DownwardWhipThresholdG)
        {
            _whipArmed = false;
            _lastWhipTime = now;
            LastWhipForceG = accelMag;

            // Reset volume accumulator so the whip doesn't count as a volume spin
            _volumeAngleAccumulator = 0;

            TriggerAction(MusicAction.PlayPause, $"Взмах вниз (Сила: {accelMag:F2} G)", accelMag);
        }
    }

    private void ProcessFlickDetection(DateTimeOffset now, ImuSample sample, double accelMag)
    {
        // Don't trigger lateral flicks during a violent downward whip
        if (accelMag > Settings.DownwardWhipThresholdG * 0.85)
        {
            return;
        }

        var msSinceLastFlick = (now - _lastFlickTime).TotalMilliseconds;
        if (msSinceLastFlick < Settings.FlickCooldownMs)
        {
            _flickArmed = false;
            return;
        }

        // Wrist flick sideways: look at angular velocity on Roll and Pitch
        var flickRate = sample.GyroY;
        if (Math.Abs(sample.GyroX) > Math.Abs(flickRate))
        {
            flickRate = sample.GyroX;
        }

        var absSpeed = Math.Abs(flickRate);

        // Rearm when hand slows down to calm state (< 65 deg/s)
        if (absSpeed < 65.0)
        {
            _flickArmed = true;
        }

        if (_flickArmed && absSpeed >= Settings.FlickSpeedThresholdDegPerSec)
        {
            _flickArmed = false;
            _lastFlickTime = now;

            // Reset volume accumulation
            _volumeAngleAccumulator = 0;

            if (flickRate > 0)
            {
                TriggerAction(MusicAction.NextTrack, $"Взмах вправо (ω = {absSpeed:F0}°/с)", absSpeed);
            }
            else
            {
                TriggerAction(MusicAction.PreviousTrack, $"Взмах влево (ω = {absSpeed:F0}°/с)", absSpeed);
            }
        }
    }

    private void ProcessFingerSpinVolume(DateTimeOffset now, double dt, ImuSample sample, double accelMag)
    {
        // When twisting the cap between fingers, hand is relatively steady (accel close to 1G, < 1.7G)
        if (accelMag > 1.7)
        {
            _volumeAngleAccumulator = 0;
            return;
        }

        // Rotation around the cap's primary central axis (Z)
        var spinSpeed = sample.GyroZ;
        var absSpinSpeed = Math.Abs(spinSpeed);

        if (absSpinSpeed >= Settings.VolumeMinSpeedDegPerSec)
        {
            _lastVolumeMotionTime = now;
            var angleDelta = spinSpeed * dt;
            _volumeAngleAccumulator += angleDelta;

            if (Math.Abs(_volumeAngleAccumulator) >= Settings.VolumeDegreesPerStep)
            {
                var msSinceStep = (now - _lastVolumeStepTime).TotalMilliseconds;
                if (msSinceStep >= Settings.VolumeStepCooldownMs)
                {
                    _lastVolumeStepTime = now;
                    if (_volumeAngleAccumulator > 0)
                    {
                        TriggerAction(MusicAction.VolumeUp, $"Кручение пальцами вправо (+{Math.Abs(_volumeAngleAccumulator):F0}°)", absSpinSpeed);
                        _volumeAngleAccumulator -= Settings.VolumeDegreesPerStep;
                    }
                    else
                    {
                        TriggerAction(MusicAction.VolumeDown, $"Кручение пальцами влево (-{Math.Abs(_volumeAngleAccumulator):F0}°)", absSpinSpeed);
                        _volumeAngleAccumulator += Settings.VolumeDegreesPerStep;
                    }
                }
            }
        }
        else
        {
            // If twisting stops for > 250 ms, reset remainder angle
            if ((now - _lastVolumeMotionTime).TotalMilliseconds > 250)
            {
                _volumeAngleAccumulator = 0;
            }
        }
    }

    private void TriggerAction(MusicAction action, string description, double metric)
    {
        if (Settings.SendSystemMediaKeys)
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

        GestureDetected?.Invoke(this, new GestureDetectionEventArgs(action, description, metric));
    }

    public void Reset()
    {
        _volumeAngleAccumulator = 0;
        _whipArmed = true;
        _flickArmed = true;
    }
}
