using System.Buffers.Binary;
using System.Text;

namespace TrikiMusicController;

public enum MusicAction
{
    None,
    PlayPause,
    NextTrack,
    PreviousTrack,
    VolumeUp,
    VolumeDown,
    Mute
}

public enum ConnectionState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Disconnecting,
    Error
}

public readonly record struct ImuSample(
    long FrameIndex,
    DateTimeOffset TimestampUtc,
    double GyroX,
    double GyroY,
    double GyroZ,
    double AccelX,
    double AccelY,
    double AccelZ,
    short RawGyroX,
    short RawGyroY,
    short RawGyroZ,
    short RawAccelX,
    short RawAccelY,
    short RawAccelZ,
    bool ButtonPressed = false,
    bool ButtonEdge = false)
{
    public static ImuSample FromFrame(
        byte[] frame,
        long frameIndex,
        double gyroScale,
        double accelScale,
        DateTimeOffset timestampUtc,
        bool isButtonPressed = false,
        bool isButtonEdge = false)
    {
        var rawGyroX = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(2, 2));
        var rawGyroY = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(4, 2));
        var rawGyroZ = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(6, 2));
        var rawAccelX = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(8, 2));
        var rawAccelY = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(10, 2));
        var rawAccelZ = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(12, 2));

        return new ImuSample(
            frameIndex,
            timestampUtc,
            rawGyroX / gyroScale,
            rawGyroY / gyroScale,
            rawGyroZ / gyroScale,
            rawAccelX / accelScale,
            rawAccelY / accelScale,
            rawAccelZ / accelScale,
            rawGyroX,
            rawGyroY,
            rawGyroZ,
            rawAccelX,
            rawAccelY,
            rawAccelZ,
            isButtonPressed,
            isButtonEdge);
    }
}

public sealed record TrikiDeviceInfo(
    string DeviceName,
    int? BatteryLevelPercent,
    string? FirmwareRevision,
    string? HardwareRevision,
    string? SoftwareRevision,
    string? ManufacturerName,
    string? ModelNumber,
    string? SerialNumber)
{
    public static TrikiDeviceInfo Empty => new("Unknown", null, null, null, null, null, null, null);

    public static string? DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        var text = Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static int? DecodeBatteryLevel(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        return bytes[0] <= 100 ? bytes[0] : null;
    }
}

public sealed class ActivityLogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Value { get; init; }

    public string TimeFormatted => Timestamp.ToString("HH:mm:ss.fff");
}
