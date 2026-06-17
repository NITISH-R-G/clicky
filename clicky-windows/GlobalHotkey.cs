using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace clicky_windows
{
    public class GlobalHotkey : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12; // Alt key

        public event Action? Pressed;
        public event Action? Released;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _isPressed = false;

        private bool _ctrlHeld = false;
        private bool _altHeld = false;

        public GlobalHotkey()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"❌ GlobalHotkey: SetWindowsHookEx failed with Win32 error code: {err}");
            }
            else
            {
                Console.WriteLine($"⌨️ GlobalHotkey: Hook registered successfully (ID: {_hookId})");
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                Console.WriteLine("⌨️ GlobalHotkey: Hook uninstalled");
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            // For WH_KEYBOARD_LL, hMod can be IntPtr.Zero when threadId is 0
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int message = wParam.ToInt32();

                bool isKeyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                bool isKeyUp = message == WM_KEYUP || message == WM_SYSKEYUP;

                if (vkCode == VK_CONTROL || vkCode == 162 || vkCode == 163)
                {
                    _ctrlHeld = isKeyDown;
                }
                else if (vkCode == VK_MENU || vkCode == 164 || vkCode == 165)
                {
                    _altHeld = isKeyDown;
                }

                if (_ctrlHeld && _altHeld)
                {
                    if (!_isPressed)
                    {
                        _isPressed = true;
                        Pressed?.Invoke();
                    }
                }
                else
                {
                    if (_isPressed && isKeyUp)
                    {
                        // Trigger release if either ctrl or alt is released
                        _isPressed = false;
                        Released?.Invoke();
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
