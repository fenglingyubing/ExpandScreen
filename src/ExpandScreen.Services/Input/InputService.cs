using System.Drawing;
using System.Runtime.InteropServices;
using ExpandScreen.Protocol.Messages;
using ExpandScreen.Utils;

namespace ExpandScreen.Services.Input
{
    /// <summary>
    /// 触控事件注入服务（WIN-103）
    /// </summary>
    public sealed class InputService
    {
        private readonly TouchCoordinateMapper _mapper = new();
        private readonly TouchContactRegistry _contacts;
        private readonly object _injectLock = new();

        private bool _touchInjectionInitialized;
        private bool _touchInjectionUnavailable;
        private Rectangle _targetBounds;

        public InputService(int maxContacts = 10)
        {
            _contacts = new TouchContactRegistry(maxContacts);

            if (OperatingSystem.IsWindows())
            {
                _targetBounds = GetVirtualScreenBounds();
                _mapper.UpdateTargetBounds(_targetBounds);
            }
            else
            {
                _targetBounds = Rectangle.Empty;
            }
        }

        public void UpdateRemoteScreenSize(int width, int height) => _mapper.UpdateSourceScreen(width, height);

        public void UpdateTargetBounds(Rectangle targetBounds)
        {
            _targetBounds = targetBounds;
            _mapper.UpdateTargetBounds(targetBounds);
        }

        public void UpdateRotationDegrees(int rotationDegrees) => _mapper.UpdateRotationDegrees(rotationDegrees);

        public void HandleTouchEvent(TouchEventMessage message)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            TouchAction action = (TouchAction)message.Action;

            int? slot =
                action switch
                {
                    TouchAction.Down => _contacts.AllocateSlot(message.PointerId),
                    TouchAction.Move => _contacts.GetSlot(message.PointerId),
                    TouchAction.Up => _contacts.GetSlot(message.PointerId),
                    _ => null
                };

            if (slot == null)
            {
                return;
            }

            Point screenPoint;
            try
            {
                screenPoint = _mapper.Map(message.X, message.Y);
            }
            catch (Exception ex)
            {
                LogHelper.Warning($"Touch coordinate mapping failed: {ex.Message}");
                return;
            }

            bool injected = TryInjectTouch(slot.Value, action, screenPoint, message.Pressure);
            if (!injected)
            {
                TryInjectMouseFallback(action, screenPoint, isPrimary: slot.Value == _contacts.GetPrimarySlot());
            }

            if (action == TouchAction.Up)
            {
                _contacts.ReleaseSlot(message.PointerId);
            }
        }

        private bool TryInjectTouch(int slot, TouchAction action, Point screenPoint, float pressure)
        {
            if (_touchInjectionUnavailable)
            {
                return false;
            }

            lock (_injectLock)
            {
                if (!_touchInjectionInitialized)
                {
                    _touchInjectionInitialized = NativeMethods.InitializeTouchInjection((uint)_contacts.MaxContacts, NativeMethods.TOUCH_FEEDBACK_DEFAULT);
                    if (!_touchInjectionInitialized)
                    {
                        _touchInjectionUnavailable = true;
                        LogHelper.Warning($"InitializeTouchInjection failed (win32={Marshal.GetLastWin32Error()}); falling back to mouse simulation.");
                        return false;
                    }
                }

                var touchInfo = NativeMethods.CreateTouchInfo(
                    pointerId: (uint)slot,
                    isPrimary: slot == _contacts.GetPrimarySlot(),
                    action: action,
                    x: screenPoint.X,
                    y: screenPoint.Y,
                    pressure: pressure);

                bool ok = NativeMethods.InjectTouchInput(1, new[] { touchInfo });
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    LogHelper.Warning($"InjectTouchInput failed (win32={err}); falling back to mouse simulation.");
                    _touchInjectionUnavailable = true;
                    return false;
                }

                return true;
            }
        }

        private static void TryInjectMouseFallback(TouchAction action, Point point, bool isPrimary)
        {
            if (!isPrimary)
            {
                return;
            }

            uint flags = action switch
            {
                TouchAction.Down => NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_LEFTDOWN,
                TouchAction.Move => NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE,
                TouchAction.Up => NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_LEFTUP,
                _ => 0
            };

            if (flags == 0)
            {
                return;
            }

            var bounds = GetVirtualScreenBounds();
            int normalizedX = NormalizeToAbsolute(point.X, bounds.Left, bounds.Width);
            int normalizedY = NormalizeToAbsolute(point.Y, bounds.Top, bounds.Height);

            var input = NativeMethods.CreateMouseInput(normalizedX, normalizedY, flags);
            NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
        }

        private static int NormalizeToAbsolute(int value, int origin, int length)
        {
            int local = value - origin;
            int clamped = Math.Clamp(local, 0, Math.Max(1, length - 1));
            return (int)Math.Round(clamped * 65535.0 / Math.Max(1, length - 1));
        }

        private static Rectangle GetVirtualScreenBounds()
        {
            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            return new Rectangle(left, top, width, height);
        }

        private static class NativeMethods
        {
            public const uint TOUCH_FEEDBACK_DEFAULT = 0x1;

            public const int SM_XVIRTUALSCREEN = 76;
            public const int SM_YVIRTUALSCREEN = 77;
            public const int SM_CXVIRTUALSCREEN = 78;
            public const int SM_CYVIRTUALSCREEN = 79;

            public const uint PT_TOUCH = 0x00000002;

            public const uint POINTER_FLAG_NONE = 0x00000000;
            public const uint POINTER_FLAG_NEW = 0x00000001;
            public const uint POINTER_FLAG_INRANGE = 0x00000002;
            public const uint POINTER_FLAG_INCONTACT = 0x00000004;
            public const uint POINTER_FLAG_DOWN = 0x00010000;
            public const uint POINTER_FLAG_UPDATE = 0x00020000;
            public const uint POINTER_FLAG_UP = 0x00040000;
            public const uint POINTER_FLAG_PRIMARY = 0x00002000;

            public const uint TOUCH_MASK_NONE = 0x00000000;
            public const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
            public const uint TOUCH_MASK_ORIENTATION = 0x00000002;
            public const uint TOUCH_MASK_PRESSURE = 0x00000004;

            public const uint MOUSEEVENTF_MOVE = 0x0001;
            public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            public const uint MOUSEEVENTF_LEFTUP = 0x0004;
            public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int left;
                public int top;
                public int right;
                public int bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct POINTER_INFO
            {
                public uint pointerType;
                public uint pointerId;
                public uint frameId;
                public uint pointerFlags;
                public IntPtr sourceDevice;
                public IntPtr hwndTarget;
                public POINT ptPixelLocation;
                public POINT ptHimetricLocation;
                public POINT ptPixelLocationRaw;
                public POINT ptHimetricLocationRaw;
                public uint dwTime;
                public uint historyCount;
                public int inputData;
                public uint dwKeyStates;
                public ulong PerformanceCount;
                public uint ButtonChangeType;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct POINTER_TOUCH_INFO
            {
                public POINTER_INFO pointerInfo;
                public uint touchFlags;
                public uint touchMask;
                public RECT rcContact;
                public RECT rcContactRaw;
                public uint orientation;
                public uint pressure;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool InitializeTouchInjection(uint maxCount, uint dwMode);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int nIndex);

            [StructLayout(LayoutKind.Sequential)]
            public struct INPUT
            {
                public uint type;
                public MOUSEINPUT mi;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            public static INPUT CreateMouseInput(int normalizedX, int normalizedY, uint flags)
            {
                return new INPUT
                {
                    type = 0, // INPUT_MOUSE
                    mi = new MOUSEINPUT
                    {
                        dx = normalizedX,
                        dy = normalizedY,
                        dwFlags = flags
                    }
                };
            }

            public static POINTER_TOUCH_INFO CreateTouchInfo(
                uint pointerId,
                bool isPrimary,
                TouchAction action,
                int x,
                int y,
                float pressure)
            {
                uint flags =
                    action switch
                    {
                        TouchAction.Down => POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_NEW,
                        TouchAction.Move => POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT,
                        TouchAction.Up => POINTER_FLAG_UP,
                        _ => POINTER_FLAG_NONE
                    };

                if (isPrimary)
                {
                    flags |= POINTER_FLAG_PRIMARY;
                }

                uint pressureValue = (uint)Math.Clamp((int)Math.Round(Math.Clamp(pressure, 0f, 1f) * 1024), 0, 1024);

                const int contactRadius = 4;
                var contact = new RECT
                {
                    left = x - contactRadius,
                    top = y - contactRadius,
                    right = x + contactRadius,
                    bottom = y + contactRadius
                };

                return new POINTER_TOUCH_INFO
                {
                    pointerInfo = new POINTER_INFO
                    {
                        pointerType = PT_TOUCH,
                        pointerId = pointerId,
                        pointerFlags = flags,
                        ptPixelLocation = new POINT { X = x, Y = y }
                    },
                    touchFlags = 0,
                    touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE,
                    rcContact = contact,
                    rcContactRaw = contact,
                    orientation = 90,
                    pressure = pressureValue
                };
            }
        }
    }
}

