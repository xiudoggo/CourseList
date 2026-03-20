using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CourseList.Helpers
{
    /// <summary>
    /// Win32 system tray icon with callbacks routed to the hosting window's WndProc.
    /// No WinForms dependency.
    /// </summary>
    public sealed class Win32TrayIcon : IDisposable
    {
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_STATE = 0x00000008;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIS_HIDDEN = 0x00000001;

        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_CONTEXTMENU = 0x007B;

        private const int GWLP_WNDPROC = -4;

        private const uint MF_STRING = 0x00000000;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_TOPALIGN = 0x0000;

        private readonly IntPtr _hwnd;
        private readonly uint _callbackMessage;
        private readonly Action _onRestoreRequested;
        private readonly Action _onOpenHomeRequested;
        private readonly Action _onExitRequested;
        private readonly SynchronizationContext? _uiContext;
        private bool _disposed;

        private int _debugLogCount;

        private IntPtr _prevWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate; // keep reference

        private IntPtr _iconHandle;
        private NOTIFYICONDATA _nid;

        private const int MenuIdOpenHome = 1;
        private const int MenuIdExit = 2;

        private const int VK_RBUTTON = 0x02;
        private long _lastContextMenuShownAtMs;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            public uint dwState;
            public uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;

            public uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;

            public uint dwInfoFlags;

            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int TrackPopupMenu(
            IntPtr hmenu,
            uint fuFlags,
            int x,
            int y,
            int nReserved,
            IntPtr hwnd,
            IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static IntPtr LoadApplicationIcon()
        {
            // IDI_APPLICATION = 32512
            const int IDI_APPLICATION = 0x7F00;
            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        public Win32TrayIcon(
            IntPtr hwnd,
            uint callbackMessage,
            bool startHidden,
            string tooltip,
            Action onRestoreRequested,
            Action onOpenHomeRequested,
            Action onExitRequested,
            uint iconId = 1)
        {
            _hwnd = hwnd;
            _callbackMessage = callbackMessage;
            _onRestoreRequested = onRestoreRequested ?? throw new ArgumentNullException(nameof(onRestoreRequested));
            _onOpenHomeRequested = onOpenHomeRequested ?? throw new ArgumentNullException(nameof(onOpenHomeRequested));
            _onExitRequested = onExitRequested ?? throw new ArgumentNullException(nameof(onExitRequested));
            _uiContext = SynchronizationContext.Current;

            _iconHandle = LoadApplicationIcon();

            _nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = iconId,
                uCallbackMessage = _callbackMessage,
                hIcon = _iconHandle,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_STATE,
                szTip = tooltip ?? string.Empty,
                dwState = startHidden ? NIS_HIDDEN : 0,
                dwStateMask = NIS_HIDDEN,
                szInfo = string.Empty,
                uTimeoutOrVersion = 0,
                szInfoTitle = string.Empty,
                dwInfoFlags = 0,
                guidItem = Guid.Empty,
                hBalloonIcon = IntPtr.Zero
            };

            AddIcon();
            SubclassWindowProc();
        }

        private void AddIcon()
        {
            Shell_NotifyIcon(NIM_ADD, ref _nid);
        }

        private void SubclassWindowProc()
        {
            _wndProcDelegate = WndProc;
            _prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }

        public void SetHidden(bool hidden)
        {
            // Use NIM_MODIFY with state only.
            _nid.uFlags = NIF_STATE;
            _nid.dwState = hidden ? NIS_HIDDEN : 0;
            _nid.dwStateMask = NIS_HIDDEN;

            Shell_NotifyIcon(NIM_MODIFY, ref _nid);

            // Restore uFlags for later modify (caller may rely on it).
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_STATE;
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_disposed)
            {
                return _prevWndProc == IntPtr.Zero
                    ? IntPtr.Zero
                    : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
            }

            if (msg == _callbackMessage)
            {
                // For NIF_MESSAGE callbacks, lParam contains the mouse message code.
                // Be defensive and mask to low-word to avoid platform differences.
                var mouseMsg = unchecked((uint)lParam.ToInt64() & 0xFFFF);
                var low = mouseMsg;
                var hi = unchecked(((uint)lParam.ToInt64() >> 16) & 0xFFFF);

                // Debug: help determine what message codes Windows sends for tray right-click.
                // Only log first few times to avoid flooding.
                if (_debugLogCount < 30)
                {
                    _debugLogCount++;
                    Debug.WriteLine($"[TrayIcon] cb wParam=0x{unchecked((uint)wParam.ToInt64()):X8} lParam=0x{unchecked((uint)lParam.ToInt64()):X8} low=0x{low:X4} hi=0x{hi:X4} mouseMsg=0x{mouseMsg:X4}");
                }

                if (mouseMsg == WM_LBUTTONDBLCLK)
                {
                    InvokeOnUiThread(_onRestoreRequested);
                    return IntPtr.Zero;
                }

                bool isRightButtonDown = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;

                if (mouseMsg == WM_RBUTTONDOWN ||
                    mouseMsg == WM_RBUTTONUP ||
                    mouseMsg == WM_CONTEXTMENU ||
                    (isRightButtonDown && mouseMsg == 0x0200 /* WM_MOUSEMOVE */))
                {
                    // Debounce: avoid showing twice due to multiple callback messages.
                    var now = Environment.TickCount64;
                    if (now - _lastContextMenuShownAtMs < 400)
                        return _prevWndProc == IntPtr.Zero
                            ? IntPtr.Zero
                            : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);

                    _lastContextMenuShownAtMs = now;
                    InvokeOnUiThread(ShowContextMenu);
                    return IntPtr.Zero;
                }
            }

            return _prevWndProc == IntPtr.Zero
                ? IntPtr.Zero
                : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        private void InvokeOnUiThread(Action action)
        {
            if (_disposed)
                return;

            if (_uiContext != null)
            {
                _uiContext.Post(_ =>
                {
                    try
                    {
                        action();
                    }
                    catch
                    {
                        // ignore
                    }
                }, null);
            }
            else
            {
                // Fallback: should be rare (construction should happen on UI thread).
                action();
            }
        }

        private void ShowContextMenu()
        {
            if (_disposed)
                return;

            // If cursor pos cannot be obtained, just fallback to center-ish (0,0) would be bad;
            // in that unlikely case, we skip showing the menu.
            if (!TryGetCursorPos(out var pt))
                return;

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
                return;

            try
            {
                AppendMenu(hMenu, MF_STRING, (UIntPtr)MenuIdOpenHome, "显示CourseList");
                AppendMenu(hMenu, MF_STRING, (UIntPtr)MenuIdExit, "退出");

                // Ensure the tray window is considered foreground; otherwise TrackPopupMenu
                // can fail to render on some systems when the app window is hidden.
                SetForegroundWindow(_hwnd);

                int selectedId = TrackPopupMenu(
                    hMenu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_TOPALIGN,
                    pt.X,
                    pt.Y,
                    0,
                    _hwnd,
                    IntPtr.Zero);

                if (_disposed)
                    return;

                // Debug: record what TrackPopupMenu returned (help diagnose if it never displayed).
                if (_debugLogCount < 60)
                {
                    Debug.WriteLine($"[TrayIcon] TrackPopupMenu return={selectedId} at ({pt.X},{pt.Y})");
                }

                if (selectedId == MenuIdOpenHome)
                {
                    _onOpenHomeRequested();
                }
                else if (selectedId == MenuIdExit)
                {
                    _onExitRequested();
                }
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        private static bool TryGetCursorPos(out POINT pt)
        {
            // user32 GetCursorPos returns BOOL; we keep it conservative.
            pt = default;
            var res = GetCursorPos(out pt);
            return res;
        }

        public void Dispose()
        {
            _disposed = true;
            try
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (_prevWndProc != IntPtr.Zero && _wndProcDelegate != null)
                {
                    SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _prevWndProc);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}

