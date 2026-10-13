using System.Buffers.Binary;
using System.Threading.Channels;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace TrikiMusicController;

public sealed class TrikiBleService
{
    private static readonly Guid NusServiceUuid = Guid.Parse("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid NusRxCharacteristicUuid = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid NusTxCharacteristicUuid = Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e");
    private static readonly Guid BatteryServiceUuid = BluetoothUuid(0x180F);
    private static readonly Guid BatteryLevelCharacteristicUuid = BluetoothUuid(0x2A19);
    private static readonly Guid DeviceInformationServiceUuid = BluetoothUuid(0x180A);
    private static readonly Guid FirmwareRevisionCharacteristicUuid = BluetoothUuid(0x2A26);
    private static readonly Guid ModelNumberCharacteristicUuid = BluetoothUuid(0x2A24);

    private readonly byte[] _startCommand = Convert.FromHexString("201000D007680003");
    private const double GyroScale = 131.0;
    private const double AccelScale = 2048.0;

    private BluetoothLEDevice? _device;
    private GattDeviceService? _nusService;
    private GattCharacteristic? _txCharacteristic;
    private GattCharacteristic? _rxCharacteristic;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
    private long _frameIndex;

    public bool AutoReconnect { get; set; } = true;
    public string TargetDeviceName { get; set; } = "Triki";
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public TrikiDeviceInfo DeviceInfo { get; private set; } = TrikiDeviceInfo.Empty;

    public event Action<ConnectionState, string>? StateChanged;
    public event Action<TrikiDeviceInfo>? DeviceInfoReceived;
    public event Action<ImuSample>? SampleReceived;
    public event Action? ButtonClicked;
    public event Action<bool>? ButtonStateChanged;
    public event Action<string>? LogMessage;

    public void Start()
    {
        if (_workerTask != null && !_workerTask.IsCompleted) return;

        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoopAsync(_workerCts.Token));
    }

    public async Task StopAsync()
    {
        if (_workerCts == null) return;

        SetState(ConnectionState.Disconnecting, "Disconnect requested");
        _workerCts.Cancel();
        if (_workerTask != null)
        {
            try
            {
                await _workerTask;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log($"Error during stop: {ex.Message}");
            }
        }

        Cleanup();
        _workerCts.Dispose();
        _workerCts = null;
        _workerTask = null;
        SetState(ConnectionState.Disconnected, "Stopped");
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(ConnectionState.Scanning, "Scanning for Triki device...");
                var device = await ScanForDeviceAsync(ct);
                if (device == null)
                {
                    Log("Triki not found in scan. Retrying in 3 seconds...");
                    if (!AutoReconnect)
                    {
                        SetState(ConnectionState.Disconnected, "Scan timed out");
                        break;
                    }
                    await Task.Delay(3000, ct);
                    continue;
                }

                SetState(ConnectionState.Connecting, $"Connecting to {device.Name}...");
                _device = device;
                _device.ConnectionStatusChanged += OnDeviceConnectionStatusChanged;

                await ConnectAndStreamAsync(device, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"BLE error: {ex.Message}");
                SetState(ConnectionState.Error, ex.Message);
            }
            finally
            {
                Cleanup();
            }

            if (!AutoReconnect || ct.IsCancellationRequested)
            {
                SetState(ConnectionState.Disconnected, "Disconnected");
                break;
            }

            Log("Waiting 3 seconds before reconnecting...");
            SetState(ConnectionState.Disconnected, "Reconnecting...");
            await Task.Delay(3000, ct);
        }
    }

    private async Task ConnectAndStreamAsync(BluetoothLEDevice device, CancellationToken ct)
    {
        // Read Battery and Device Info
        DeviceInfo = await ReadDeviceInfoAsync(device, ct);
        DeviceInfoReceived?.Invoke(DeviceInfo);
        Log($"Connected to {DeviceInfo.DeviceName}. Battery: {(DeviceInfo.BatteryLevelPercent.HasValue ? $"{DeviceInfo.BatteryLevelPercent}%" : "Unknown")}");

        // Find NUS Service
        var services = await device.GetGattServicesForUuidAsync(NusServiceUuid, BluetoothCacheMode.Uncached);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            Log($"NUS Service not found. Status: {services.Status}");
            return;
        }

        _nusService = services.Services[0];

        // Find TX (Notify) Characteristic
        var txChars = await _nusService.GetCharacteristicsForUuidAsync(NusTxCharacteristicUuid, BluetoothCacheMode.Uncached);
        if (txChars.Status != GattCommunicationStatus.Success || txChars.Characteristics.Count == 0)
        {
            Log($"NUS TX Characteristic not found. Status: {txChars.Status}");
            return;
        }
        _txCharacteristic = txChars.Characteristics[0];

        // Setup notification channel
        var channel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        Windows.Foundation.TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = (_, args) =>
        {
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var bytes);
            channel.Writer.TryWrite(bytes);
        };

        _txCharacteristic.ValueChanged += handler;

        var notifyStatus = await _txCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
            GattClientCharacteristicConfigurationDescriptorValue.Notify);

        if (notifyStatus != GattCommunicationStatus.Success)
        {
            Log($"Failed to enable notifications: {notifyStatus}");
            _txCharacteristic.ValueChanged -= handler;
            return;
        }

        // Write start command to RX
        var rxChars = await _nusService.GetCharacteristicsForUuidAsync(NusRxCharacteristicUuid, BluetoothCacheMode.Uncached);
        if (rxChars.Status == GattCommunicationStatus.Success && rxChars.Characteristics.Count > 0)
        {
            _rxCharacteristic = rxChars.Characteristics[0];
            await Task.Delay(500, ct); // Short settle delay
            var writeStatus = await _rxCharacteristic.WriteValueAsync(
                CryptographicBuffer.CreateFromByteArray(_startCommand),
                GattWriteOption.WriteWithoutResponse);
            Log($"Start command sent: {writeStatus}");
        }

        SetState(ConnectionState.Connected, "Active stream");

        using var disconnectCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, disconnectCts.Token);

        void OnStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
            {
                Log("BLE device disconnected.");
                disconnectCts.Cancel();
            }
        }

        device.ConnectionStatusChanged += OnStatusChanged;

        // Process incoming notifications
        var frameParser = new FrameParser();
        var lastButtonState = false;
        try
        {
            await foreach (var data in channel.Reader.ReadAllAsync(linkedCts.Token))
            {
                var timestamp = DateTimeOffset.UtcNow;
                foreach (var (frame, isPressed) in frameParser.PushWithButton(data))
                {
                    _frameIndex++;

                    var isEdge = isPressed && !lastButtonState;
                    if (isPressed != lastButtonState)
                    {
                        lastButtonState = isPressed;
                        ButtonStateChanged?.Invoke(isPressed);
                    }
                    if (isEdge)
                    {
                        ButtonClicked?.Invoke();
                    }

                    // Discard first few startup frames to allow sensor to settle
                    if (_frameIndex < 15) continue;

                    var sample = ImuSample.FromFrame(frame, _frameIndex, GyroScale, AccelScale, timestamp, isPressed, isEdge);
                    SampleReceived?.Invoke(sample);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            device.ConnectionStatusChanged -= OnStatusChanged;
            if (_txCharacteristic != null)
            {
                _txCharacteristic.ValueChanged -= handler;
                try
                {
                    await _txCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
                catch { }
            }
        }
    }

    private void OnDeviceConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Log("BLE device disconnected.");
        }
    }

    private async Task<BluetoothLEDevice?> ScanForDeviceAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<BluetoothLEDevice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scanTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, scanTimeoutCts.Token);
        using var reg = linkedCts.Token.Register(() => tcs.TrySetResult(null));

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        watcher.Received += async (_, args) =>
        {
            var name = args.Advertisement.LocalName;
            if (!string.IsNullOrWhiteSpace(name) && name.Contains(TargetDeviceName, StringComparison.OrdinalIgnoreCase))
            {
                watcher.Stop();
                try
                {
                    var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    tcs.TrySetResult(dev);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            }
        };

        watcher.Start();
        var result = await tcs.Task;
        watcher.Stop();
        return result;
    }

    private async Task<TrikiDeviceInfo> ReadDeviceInfoAsync(BluetoothLEDevice device, CancellationToken ct)
    {
        int? battery = null;
        string? firmware = null;
        string? model = null;

        try
        {
            var battServices = await device.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached);
            if (battServices.Status == GattCommunicationStatus.Success && battServices.Services.Count > 0)
            {
                using var battService = battServices.Services[0];
                var battChars = await battService.GetCharacteristicsForUuidAsync(BatteryLevelCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (battChars.Status == GattCommunicationStatus.Success && battChars.Characteristics.Count > 0)
                {
                    var read = await battChars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (read.Status == GattCommunicationStatus.Success && read.Value != null)
                    {
                        CryptographicBuffer.CopyToByteArray(read.Value, out var bytes);
                        battery = TrikiDeviceInfo.DecodeBatteryLevel(bytes);
                    }
                }
            }
        }
        catch { }

        try
        {
            var infoServices = await device.GetGattServicesForUuidAsync(DeviceInformationServiceUuid, BluetoothCacheMode.Uncached);
            if (infoServices.Status == GattCommunicationStatus.Success && infoServices.Services.Count > 0)
            {
                using var infoService = infoServices.Services[0];
                var fwChars = await infoService.GetCharacteristicsForUuidAsync(FirmwareRevisionCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (fwChars.Status == GattCommunicationStatus.Success && fwChars.Characteristics.Count > 0)
                {
                    var read = await fwChars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (read.Status == GattCommunicationStatus.Success && read.Value != null)
                    {
                        CryptographicBuffer.CopyToByteArray(read.Value, out var bytes);
                        firmware = TrikiDeviceInfo.DecodeText(bytes);
                    }
                }

                var modelChars = await infoService.GetCharacteristicsForUuidAsync(ModelNumberCharacteristicUuid, BluetoothCacheMode.Uncached);
                if (modelChars.Status == GattCommunicationStatus.Success && modelChars.Characteristics.Count > 0)
                {
                    var read = await modelChars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (read.Status == GattCommunicationStatus.Success && read.Value != null)
                    {
                        CryptographicBuffer.CopyToByteArray(read.Value, out var bytes);
                        model = TrikiDeviceInfo.DecodeText(bytes);
                    }
                }
            }
        }
        catch { }

        return new TrikiDeviceInfo(
            DeviceName: device.Name,
            BatteryLevelPercent: battery,
            FirmwareRevision: firmware,
            HardwareRevision: null,
            SoftwareRevision: null,
            ManufacturerName: null,
            ModelNumber: model,
            SerialNumber: null);
    }

    private void Cleanup()
    {
        if (_device != null)
        {
            _device.ConnectionStatusChanged -= OnDeviceConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }

        if (_nusService != null)
        {
            _nusService.Dispose();
            _nusService = null;
        }

        _txCharacteristic = null;
        _rxCharacteristic = null;
        _frameIndex = 0;
    }

    private void SetState(ConnectionState state, string message)
    {
        State = state;
        StateChanged?.Invoke(state, message);
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss} {message}");
    }

    private static Guid BluetoothUuid(ushort value) =>
        Guid.Parse($"0000{value:X4}-0000-1000-8000-00805f9b34fb");

    private sealed class FrameParser
    {
        private const int FrameLength = 14;
        private readonly List<byte> _buffer = new();

        public IEnumerable<(byte[] Frame, bool IsPressed)> PushWithButton(byte[] bytes)
        {
            _buffer.AddRange(bytes);
            while (true)
            {
                var headerIndex = -1;
                for (var i = 0; i < _buffer.Count - 1; i++)
                {
                    // Triki sends 0x22 as packet header.
                    // Byte 1 represents button status (0x00 = released, 0x01 = pressed)
                    if (_buffer[i] == 0x22)
                    {
                        headerIndex = i;
                        break;
                    }
                }

                if (headerIndex < 0)
                {
                    if (_buffer.Count > 0)
                    {
                        var keepTrailing = _buffer[^1] == 0x22;
                        _buffer.Clear();
                        if (keepTrailing) _buffer.Add(0x22);
                    }
                    yield break;
                }

                if (headerIndex > 0)
                {
                    _buffer.RemoveRange(0, headerIndex);
                }

                if (_buffer.Count < FrameLength)
                {
                    yield break;
                }

                var buttonByte = _buffer[1];
                var isPressed = buttonByte != 0;
                var frame = _buffer.GetRange(0, FrameLength).ToArray();
                _buffer.RemoveRange(0, FrameLength);

                yield return (frame, isPressed);
            }
        }
    }
}
