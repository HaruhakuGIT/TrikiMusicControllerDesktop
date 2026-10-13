using System.Runtime.InteropServices;

namespace TrikiMusicController;

public static class MediaKeyController
{
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;

    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;

    public static void PlayPause()
    {
        SendVirtualKey(VkMediaPlayPause);
    }

    public static void NextTrack()
    {
        SendVirtualKey(VkMediaNextTrack);
    }

    public static void PreviousTrack()
    {
        SendVirtualKey(VkMediaPrevTrack);
    }

    public static void VolumeUp(int steps = 1)
    {
        for (var i = 0; i < Math.Clamp(steps, 1, 5); i++)
        {
            SendVirtualKey(VkVolumeUp);
        }
    }

    public static void VolumeDown(int steps = 1)
    {
        for (var i = 0; i < Math.Clamp(steps, 1, 5); i++)
        {
            SendVirtualKey(VkVolumeDown);
        }
    }

    public static void Mute()
    {
        SendVirtualKey(VkVolumeMute);
    }

    private static void SendVirtualKey(byte virtualKey)
    {
        keybd_event(virtualKey, 0, KeyEventExtendedKey, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KeyEventExtendedKey | KeyEventKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
