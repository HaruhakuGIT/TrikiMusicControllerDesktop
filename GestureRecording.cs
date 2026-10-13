using System.IO;
using System.Text.Json;

namespace TrikiMusicController;

public sealed class GesturePoint
{
    public double TimeMs { get; set; }
    public double GyroX { get; set; }
    public double GyroY { get; set; }
    public double GyroZ { get; set; }
    public double AccelX { get; set; }
    public double AccelY { get; set; }
    public double AccelZ { get; set; }
    public double TotalAccel { get; set; }
    public double TotalGyro { get; set; }
}

public sealed class RecordedGesture
{
    public string ActionName { get; set; } = "";
    public MusicAction TargetAction { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.Now;
    public double DurationMs { get; set; }
    public double PeakAccelG { get; set; }
    public double PeakGyroDegPerSec { get; set; }
    public double DominantGyroZ { get; set; }
    public double DominantGyroY { get; set; }
    public double DominantGyroX { get; set; }

    public double PeakDirX { get; set; }
    public double PeakDirY { get; set; }
    public double PeakDirZ { get; set; }

    // Baseline Spatial Orientation of Triki in hand at rest (Horizontal vs Vertical vs Tilted)
    public double RestDirX { get; set; }
    public double RestDirY { get; set; }
    public double RestDirZ { get; set; }

    public List<GesturePoint> Points { get; set; } = new();

    public static RecordedGesture FromSamples(string actionName, MusicAction action, IReadOnlyList<ImuSample> samples)
    {
        if (samples.Count == 0) return new RecordedGesture { ActionName = actionName, TargetAction = action };

        var start = samples[0].TimestampUtc;
        var points = new List<GesturePoint>(samples.Count);
        double maxAccel = 0;
        double maxGyro = 0;
        double peakDirX = 0, peakDirY = 0, peakDirZ = 0;
        double sumGyroX = 0, sumGyroY = 0, sumGyroZ = 0;

        // Calculate initial rest orientation (first 5-10 samples before impact/movement)
        var restCount = Math.Min(10, Math.Max(3, samples.Count / 4));
        double restAx = 0, restAy = 0, restAz = 0;
        for (var i = 0; i < restCount; i++)
        {
            restAx += samples[i].AccelX;
            restAy += samples[i].AccelY;
            restAz += samples[i].AccelZ;
        }
        var restMag = Math.Sqrt(restAx * restAx + restAy * restAy + restAz * restAz) + 0.0001;
        var normRestX = restAx / restMag;
        var normRestY = restAy / restMag;
        var normRestZ = restAz / restMag;

        foreach (var s in samples)
        {
            var t = (s.TimestampUtc - start).TotalMilliseconds;
            var aMag = Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ);
            var gMag = Math.Sqrt(s.GyroX * s.GyroX + s.GyroY * s.GyroY + s.GyroZ * s.GyroZ);

            if (aMag > maxAccel)
            {
                maxAccel = aMag;
                peakDirX = s.AccelX / (aMag + 0.0001);
                peakDirY = s.AccelY / (aMag + 0.0001);
                peakDirZ = s.AccelZ / (aMag + 0.0001);
            }

            if (gMag > maxGyro) maxGyro = gMag;

            sumGyroX += Math.Abs(s.GyroX);
            sumGyroY += Math.Abs(s.GyroY);
            sumGyroZ += Math.Abs(s.GyroZ);

            points.Add(new GesturePoint
            {
                TimeMs = t,
                GyroX = s.GyroX,
                GyroY = s.GyroY,
                GyroZ = s.GyroZ,
                AccelX = s.AccelX,
                AccelY = s.AccelY,
                AccelZ = s.AccelZ,
                TotalAccel = aMag,
                TotalGyro = gMag
            });
        }

        var duration = points[^1].TimeMs;
        var totalSum = sumGyroX + sumGyroY + sumGyroZ + 0.001;

        return new RecordedGesture
        {
            ActionName = actionName,
            TargetAction = action,
            DurationMs = duration,
            PeakAccelG = maxAccel,
            PeakGyroDegPerSec = maxGyro,
            DominantGyroX = sumGyroX / totalSum,
            DominantGyroY = sumGyroY / totalSum,
            DominantGyroZ = sumGyroZ / totalSum,
            PeakDirX = peakDirX,
            PeakDirY = peakDirY,
            PeakDirZ = peakDirZ,
            RestDirX = normRestX,
            RestDirY = normRestY,
            RestDirZ = normRestZ,
            Points = points
        };
    }

    /// <summary>
    /// Gets rest spatial orientation (unit gravity vector before movement).
    /// </summary>
    public (double x, double y, double z) GetRestOrientation()
    {
        if (Math.Abs(RestDirX) > 0.001 || Math.Abs(RestDirY) > 0.001 || Math.Abs(RestDirZ) > 0.001)
        {
            return (RestDirX, RestDirY, RestDirZ);
        }

        if (Points.Count == 0) return (0, 0, 1.0);
        var p0 = Points[0];
        var mag = p0.TotalAccel + 0.0001;
        return (p0.AccelX / mag, p0.AccelY / mag, p0.AccelZ / mag);
    }

    /// <summary>
    /// Gets directional unit vector of peak acceleration (falls back to calculating from Points).
    /// </summary>
    public (double x, double y, double z) GetPeakDirection()
    {
        if (Math.Abs(PeakDirX) > 0.001 || Math.Abs(PeakDirY) > 0.001 || Math.Abs(PeakDirZ) > 0.001)
        {
            return (PeakDirX, PeakDirY, PeakDirZ);
        }

        if (Points.Count == 0) return (0, 0, 0);

        var peakPt = Points.OrderByDescending(p => p.TotalAccel).First();
        var mag = peakPt.TotalAccel + 0.0001;
        return (peakPt.AccelX / mag, peakPt.AccelY / mag, peakPt.AccelZ / mag);
    }

    /// <summary>
    /// Automatically segments multiple repetitions of a gesture performed during a continuous multi-second recording session.
    /// </summary>
    public static List<RecordedGesture> SegmentRepetitions(string actionName, MusicAction action, IReadOnlyList<ImuSample> session)
    {
        var result = new List<RecordedGesture>();
        if (session.Count < 10) return result;

        // If Idle noise session: take the whole session as baseline noise sample
        if (action == MusicAction.None)
        {
            result.Add(FromSamples(actionName, action, session));
            return result;
        }

        // Discrete gestures (Play/Pause whip down, or Next/Prev flicks)
        if (action == MusicAction.PlayPause)
        {
            // Calculate dynamic threshold based on session maximum
            var sessionMaxG = session.Max(s => Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ));
            var thresholdG = Math.Max(1.45, sessionMaxG * 0.65);
            var peakIndices = FindPeaks(session, s => Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ), threshold: thresholdG, minDistanceSamples: 15);

            foreach (var idx in peakIndices)
            {
                var start = Math.Max(0, idx - 10);
                var end = Math.Min(session.Count - 1, idx + 15);
                var slice = new List<ImuSample>();
                for (var i = start; i <= end; i++) slice.Add(session[i]);

                result.Add(FromSamples(actionName, action, slice));
            }
        }
        else if (action == MusicAction.NextTrack || action == MusicAction.PreviousTrack)
        {
            // Segment by lateral gyro bursts or directional acceleration
            var maxGyro = session.Max(s => Math.Abs(Math.Abs(s.GyroX) > Math.Abs(s.GyroY) ? s.GyroX : s.GyroY));
            var maxAccel = session.Max(s => Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ));

            if (maxGyro >= 75.0)
            {
                var thresh = Math.Max(60.0, maxGyro * 0.60);
                var peakIndices = FindPeaks(session, s => Math.Abs(Math.Abs(s.GyroX) > Math.Abs(s.GyroY) ? s.GyroX : s.GyroY), threshold: thresh, minDistanceSamples: 15);
                foreach (var idx in peakIndices)
                {
                    var start = Math.Max(0, idx - 10);
                    var end = Math.Min(session.Count - 1, idx + 15);
                    var slice = new List<ImuSample>();
                    for (var i = start; i <= end; i++) slice.Add(session[i]);

                    result.Add(FromSamples(actionName, action, slice));
                }
            }
            else
            {
                // Fallback to acceleration peaks if user made linear flicks
                var thresh = Math.Max(1.4, maxAccel * 0.65);
                var peakIndices = FindPeaks(session, s => Math.Sqrt(s.AccelX * s.AccelX + s.AccelY * s.AccelY + s.AccelZ * s.AccelZ), threshold: thresh, minDistanceSamples: 15);
                foreach (var idx in peakIndices)
                {
                    var start = Math.Max(0, idx - 10);
                    var end = Math.Min(session.Count - 1, idx + 15);
                    var slice = new List<ImuSample>();
                    for (var i = start; i <= end; i++) slice.Add(session[i]);

                    result.Add(FromSamples(actionName, action, slice));
                }
            }
        }
        else // Volume Up / Volume Down (Finger spin)
        {
            var maxZ = session.Max(s => Math.Abs(s.GyroZ));
            var thresh = Math.Max(30.0, maxZ * 0.55);
            var peakIndices = FindPeaks(session, s => Math.Abs(s.GyroZ), threshold: thresh, minDistanceSamples: 15);

            foreach (var idx in peakIndices)
            {
                var start = Math.Max(0, idx - 12);
                var end = Math.Min(session.Count - 1, idx + 15);
                var slice = new List<ImuSample>();
                for (var i = start; i <= end; i++) slice.Add(session[i]);

                result.Add(FromSamples(actionName, action, slice));
            }
        }

        // If no distinct peaks detected, save whole session as one fallback
        if (result.Count == 0)
        {
            result.Add(FromSamples(actionName, action, session));
        }

        return result;
    }

    private static List<int> FindPeaks(IReadOnlyList<ImuSample> samples, Func<ImuSample, double> metricSelector, double threshold, int minDistanceSamples)
    {
        var peaks = new List<int>();
        var lastPeak = -minDistanceSamples;

        for (var i = 1; i < samples.Count - 1; i++)
        {
            var val = metricSelector(samples[i]);
            var prev = metricSelector(samples[i - 1]);
            var next = metricSelector(samples[i + 1]);

            if (val >= threshold && val >= prev && val >= next && (i - lastPeak) >= minDistanceSamples)
            {
                peaks.Add(i);
                lastPeak = i;
            }
        }

        return peaks;
    }
}

public sealed class GestureProfile
{
    public MusicAction Action { get; set; }
    public string Name { get; set; } = "";
    public int SampleCount { get; set; }
    public double MinPeakAccelG { get; set; }
    public double MaxPeakAccelG { get; set; }
    public double AvgPeakAccelG { get; set; }
    public double MinPeakGyro { get; set; }
    public double AvgPeakGyro { get; set; }
    public double DominantAxisZRatio { get; set; }
    public double DominantAxisYRatio { get; set; }
    public double DominantAxisXRatio { get; set; }
    public double AvgDurationMs { get; set; }

    // Normalized Direction Vector of Peak Impact/Motion
    public double DirX { get; set; }
    public double DirY { get; set; }
    public double DirZ { get; set; }

    // Normalized Rest Orientation Vector of Triki in hand (Horizontal vs Vertical vs Tilted)
    public double RestDirX { get; set; }
    public double RestDirY { get; set; }
    public double RestDirZ { get; set; }

    // Dominant Accel Axis components
    public double AvgAccelX { get; set; }
    public double AvgAccelY { get; set; }
    public double AvgAccelZ { get; set; }

    public double ComputeDirectionCosine(double ax, double ay, double az)
    {
        var mag = Math.Sqrt(ax * ax + ay * ay + az * az);
        var dirMag = Math.Sqrt(DirX * DirX + DirY * DirY + DirZ * DirZ);
        if (mag < 0.001 || dirMag < 0.001) return 0.0;
        return (ax * DirX + ay * DirY + az * DirZ) / (mag * dirMag);
    }

    public double ComputeRestOrientationCosine(double rx, double ry, double rz)
    {
        var rMag = Math.Sqrt(rx * rx + ry * ry + rz * rz);
        var baseMag = Math.Sqrt(RestDirX * RestDirX + RestDirY * RestDirY + RestDirZ * RestDirZ);
        if (rMag < 0.001 || baseMag < 0.001) return 1.0; // If uncalibrated, don't reject
        return (rx * RestDirX + ry * RestDirY + rz * RestDirZ) / (rMag * baseMag);
    }
}

public sealed class TrainingStatItem
{
    public string ActionName { get; init; } = "";
    public int SampleCount { get; init; }
    public string PeakGFormatted { get; init; } = "";
    public string PeakGyroFormatted { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class GestureProfileStorage
{
    private readonly string _folderPath;

    public GestureProfileStorage(string folderPath)
    {
        _folderPath = folderPath;
        if (!Directory.Exists(_folderPath)) Directory.CreateDirectory(_folderPath);
    }

    public void SaveRecording(RecordedGesture gesture)
    {
        var fileName = $"{gesture.TargetAction}_{DateTime.Now:yyyyMMdd_HHmmssfff}_{Guid.NewGuid().ToString("N")[..4]}.json";
        var filePath = Path.Combine(_folderPath, fileName);
        var json = JsonSerializer.Serialize(gesture, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public List<RecordedGesture> LoadAllRecordings()
    {
        var list = new List<RecordedGesture>();
        if (!Directory.Exists(_folderPath)) return list;

        foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var gesture = JsonSerializer.Deserialize<RecordedGesture>(json);
                if (gesture != null) list.Add(gesture);
            }
            catch { }
        }

        return list;
    }

    public void ClearAllRecordings()
    {
        if (!Directory.Exists(_folderPath)) return;
        foreach (var file in Directory.GetFiles(_folderPath, "*.json"))
        {
            try { File.Delete(file); } catch { }
        }
    }
}
