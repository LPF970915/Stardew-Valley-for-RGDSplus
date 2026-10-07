using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Rgdsplus.VerticalWorld
{
    // RGDSplus's firmware input is available as evdev even when SDL/gptokeyb
    // does not forward the physical Select or touchscreen to MonoGame.
    internal static class EvdevInput
    {
        private const int O_RDONLY = 0;
        private const int O_NONBLOCK = 0x800;
        private const short EvSyn = 0;
        private const short EvKey = 1;
        private const short EvAbs = 3;
        private const ushort KeySelect = 310;
        private const ushort AbsSlot = 47;
        private const ushort AbsMtPositionX = 53;
        private const ushort AbsMtPositionY = 54;
        private const ushort AbsMtTrackingId = 57;
        private const ushort AbsX = 0;
        private const ushort AbsY = 1;
        private const ushort BtnTouch = 330;

        private static readonly List<int> keyFds = new List<int>();
        private static int touchFd = -1;
        private static bool initialized;
        private static bool touchActive;
        private static bool touchDirty;
        private static bool mtPositionSeen;
        private static int trackingId = -1;
        private static int slot;
        private static int touchX;
        private static int touchY;
        private static int touchMinX;
        private static int touchMaxX = 1023;
        private static int touchMinY;
        private static int touchMaxY = 767;
        private const int EventSize32 = 16;
        private const int EventSize64 = 24;
        private static readonly byte[] buffer = new byte[EventSize64 * 64];
        private const ulong EviocgabsBase = 0x80184540UL;
        private const ulong Eviocgrab = 0x40044590UL;
        private static bool touchReadLogged;

        internal static void Poll(Action<int> keyPressed, Action<float, float, bool, bool> touchChanged)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;
            EnsureInitialized();
            PollKeys(keyPressed);
            PollTouch(touchChanged);
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                // Directory.GetFiles does not expand wildcards across directory
                // segments on the device's Mono/.NET runtime. Enumerate event
                // directories first, then read each device name explicitly.
                foreach (var eventDir in Directory.GetDirectories("/sys/class/input", "event*"))
                {
                    var namePath = Path.Combine(eventDir, "device", "name");
                    if (!File.Exists(namePath)) continue;
                    var name = File.ReadAllText(namePath).Trim();
                    var eventName = new DirectoryInfo(eventDir).Name;
                    if (!eventName.StartsWith("event", StringComparison.Ordinal)) continue;
                    if (!int.TryParse(eventName.Substring(5), out var number)) continue;
                    var fd = Open("/dev/input/" + eventName);
                    if (fd < 0) continue;
                    if (string.Equals(name, "gt9xx-0", StringComparison.OrdinalIgnoreCase))
                    {
                        touchFd = fd;
                        var grabResult = ioctl(fd, (UIntPtr)Eviocgrab, 1);
                        ModEntry.Write("evdev-touch-grab=" + (grabResult == 0 ? "ok" : "failed errno=" + Marshal.GetLastWin32Error()));
                        ReadAxisRange(fd, AbsMtPositionX, AbsX, out touchMinX, out touchMaxX);
                        ReadAxisRange(fd, AbsMtPositionY, AbsY, out touchMinY, out touchMaxY);
                        ModEntry.Write("evdev-touch-axis x=" + touchMinX + ".." + touchMaxX + "; y=" + touchMinY + ".." + touchMaxY);
                        // Some firmware revisions expose gt9xx as type-B MT,
                        // others expose the same panel as legacy ABS_X/ABS_Y.
                        // Detect the packet form instead of assuming it from
                        // the device name.
                        continue;
                    }
                    keyFds.Add(fd);
                    ModEntry.Write("evdev-open event=" + number + "; name=" + name);
                }
                if (touchFd >= 0) ModEntry.Write("evdev-open touch=gt9xx-0; normalized=1024x768");
                else ModEntry.Write("evdev-touch-unavailable");
            }
            catch (Exception ex)
            {
                ModEntry.Write("evdev-init-failed " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PollKeys(Action<int> keyPressed)
        {
            foreach (var fd in keyFds)
            {
                ReadAvailable(fd, (type, code, value) =>
                {
                    if (type == EvKey && code == KeySelect && value == 1)
                    {
                        ModEntry.Write("evdev-key code=310 value=1");
                        keyPressed?.Invoke(code);
                    }
                });
            }
        }

        private static void PollTouch(Action<float, float, bool, bool> touchChanged)
        {
            if (touchFd < 0) return;
            ReadAvailable(touchFd, (type, code, value) =>
            {
                if (type == EvAbs)
                {
                    if (code == AbsSlot) { slot = value; }
                    else if (code == AbsMtTrackingId) { trackingId = value; }
                    else if (code == AbsMtPositionX) { touchX = value; touchDirty = true; mtPositionSeen = true; }
                    else if (code == AbsMtPositionY) { touchY = value; touchDirty = true; mtPositionSeen = true; }
                    else if (code == AbsX && !mtPositionSeen) { touchX = value; touchDirty = true; }
                    else if (code == AbsY && !mtPositionSeen) { touchY = value; touchDirty = true; }
                }
                else if (type == EvKey && code == BtnTouch)
                {
                    trackingId = value != 0 ? Math.Max(trackingId, 0) : -1;
                    touchDirty = true;
                }
                else if (type == EvSyn && code == 0)
                {
                    if (trackingId >= 0)
                    {
                        var began = !touchActive;
                        touchActive = true;
                        if (began || touchDirty)
                            touchChanged?.Invoke(Normalize(touchX, touchMinX, touchMaxX, 1023), Normalize(touchY, touchMinY, touchMaxY, 767), began, false);
                    }
                    else if (touchActive)
                    {
                        touchActive = false;
                        touchChanged?.Invoke(Normalize(touchX, touchMinX, touchMaxX, 1023), Normalize(touchY, touchMinY, touchMaxY, 767), false, true);
                    }
                    touchDirty = false;
                    mtPositionSeen = false;
                }
            });
        }

        // A single read from evdev commonly returns several input_event structs.
        // Parse every complete struct; consuming only the first one drops the
        // coordinates or SYN_REPORT that complete a touch gesture.
        private static void ReadAvailable(int fd, Action<short, ushort, int> handle)
        {
            var bytesRead = read(fd, buffer, (UIntPtr)buffer.Length);
            if (bytesRead <= 0) return;
            var eventSize = IntPtr.Size >= 8 ? EventSize64 : EventSize32;
            if (fd == touchFd && !touchReadLogged)
            {
                touchReadLogged = true;
                ModEntry.Write("evdev-touch-read bytes=" + bytesRead + "; event-size=" + eventSize);
            }
            var typeOffset = eventSize == EventSize64 ? 16 : 8;
            var codeOffset = typeOffset + 2;
            var valueOffset = typeOffset + 4;
            for (var offset = 0; offset + eventSize <= bytesRead; offset += eventSize)
            {
                var type = BitConverter.ToInt16(buffer, offset + typeOffset);
                var code = BitConverter.ToUInt16(buffer, offset + codeOffset);
                var value = BitConverter.ToInt32(buffer, offset + valueOffset);
                handle?.Invoke(type, code, value);
            }
        }

        private static int Open(string path) => open(path, O_RDONLY | O_NONBLOCK);

        private static void ReadAxisRange(int fd, ushort preferred, ushort fallback, out int minimum, out int maximum)
        {
            minimum = 0;
            maximum = preferred == AbsMtPositionY || fallback == AbsY ? 767 : 1023;
            var data = new byte[24];
            if (ioctl(fd, (UIntPtr)(EviocgabsBase + preferred), data) == 0)
            {
                var min = BitConverter.ToInt32(data, 4);
                var max = BitConverter.ToInt32(data, 8);
                if (max > min) { minimum = min; maximum = max; return; }
            }
            if (ioctl(fd, (UIntPtr)(EviocgabsBase + fallback), data) == 0)
            {
                var min = BitConverter.ToInt32(data, 4);
                var max = BitConverter.ToInt32(data, 8);
                if (max > min) { minimum = min; maximum = max; }
            }
        }

        private static float Normalize(int value, int minimum, int maximum, int outputMaximum)
        {
            if (maximum <= minimum) return Math.Max(0, Math.Min(outputMaximum, value));
            var normalized = (value - minimum) * (double)outputMaximum / (maximum - minimum);
            return (float)Math.Max(0, Math.Min(outputMaximum, normalized));
        }

        [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
        [DllImport("libc", SetLastError = true)] private static extern int read(int fd, byte[] buffer, UIntPtr count);
        [DllImport("libc", SetLastError = true)] private static extern int ioctl(int fd, UIntPtr request, byte[] data);
        [DllImport("libc", SetLastError = true)] private static extern int ioctl(int fd, UIntPtr request, int value);
    }
}
