using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Windowing;

namespace CourseList.Helpers
{
    /// <summary>
    /// Win32 托盘图标与右键菜单控制器。
    /// </summary>
    public sealed class TrayMenuController : IDisposable
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
        private readonly Action _onShowExpandedRequested;
        private readonly Action _onShowCompactRequested;
        private readonly Action _onExitRequested;
        private readonly SynchronizationContext? _uiContext;
        private bool _disposed;
        private bool _isHiddenToTray;

        private int _debugLogCount;

        private IntPtr _prevWndProc = IntPtr.Zero;
        private WndProcDelegate? _wndProcDelegate;

        private IntPtr _iconHandle;
        private NOTIFYICONDATA _nid;

        private const int MenuIdShowExpanded = 1;
        private const int MenuIdShowCompact = 2;
        private const int MenuIdExit = 3;

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

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

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
        private static extern int TrackPopupMenu(IntPtr hmenu, uint fuFlags, int x, int y, int nReserved, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static IntPtr LoadApplicationIcon()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var iconPath = System.IO.Path.Combine(baseDir, "Assets", "CourseList.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    const uint IMAGE_ICON = 1;
                    const uint LR_LOADFROMFILE = 0x00000010;
                    const uint LR_DEFAULTSIZE = 0x00000040;

                    var hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                    if (hIcon != IntPtr.Zero)
                    {
                        return hIcon;
                    }
                }
            }
            catch
            {
                // ignore and fallback
            }

            const int IDI_APPLICATION = 0x7F00;
            return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        }

        public TrayMenuController(
            IntPtr hwnd,
            uint callbackMessage,
            bool startHidden,
            string tooltip,
            Action onRestoreRequested,
            Action onShowExpandedRequested,
            Action onShowCompactRequested,
            Action onExitRequested,
            uint iconId = 1)
        {
            _hwnd = hwnd;
            _callbackMessage = callbackMessage;
            _onRestoreRequested = onRestoreRequested ?? throw new ArgumentNullException(nameof(onRestoreRequested));
            _onShowExpandedRequested = onShowExpandedRequested ?? throw new ArgumentNullException(nameof(onShowExpandedRequested));
            _onShowCompactRequested = onShowCompactRequested ?? throw new ArgumentNullException(nameof(onShowCompactRequested));
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

        private void AddIcon() => Shell_NotifyIcon(NIM_ADD, ref _nid);

        private void SubclassWindowProc()
        {
            _wndProcDelegate = WndProc;
            _prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }

        public void SetHidden(bool hidden)
        {
            _nid.uFlags = NIF_STATE;
            _nid.dwState = hidden ? NIS_HIDDEN : 0;
            _nid.dwStateMask = NIS_HIDDEN;
            Shell_NotifyIcon(NIM_MODIFY, ref _nid);
            _nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_STATE;
        }

        public bool IsHiddenToTray => _isHiddenToTray;

        public void HideToTray(AppWindow appWindow)
        {
            if (_disposed)
            {
                return;
            }

            _isHiddenToTray = true;
            appWindow.IsShownInSwitchers = false;
            appWindow.Hide();
            SetHidden(false);
        }

        public void RestoreFromTray(AppWindow appWindow)
        {
            if (_disposed)
            {
                return;
            }

            _isHiddenToTray = false;
            appWindow.IsShownInSwitchers = true;
            appWindow.Show();
        }

        public void EnsureWindowShown(AppWindow appWindow)
        {
            if (_disposed)
            {
                return;
            }

            if (_isHiddenToTray)
            {
                RestoreFromTray(appWindow);
                return;
            }

            appWindow.IsShownInSwitchers = true;
            appWindow.Show();
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_disposed)
            {
                return _prevWndProc == IntPtr.Zero ? IntPtr.Zero : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
            }

            if (msg == _callbackMessage)
            {
                var mouseMsg = unchecked((uint)lParam.ToInt64() & 0xFFFF);
                if (_debugLogCount < 30)
                {
                    _debugLogCount++;
                    Debug.WriteLine($"[TrayIcon] cb mouseMsg=0x{mouseMsg:X4}");
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
                    (isRightButtonDown && mouseMsg == 0x0200))
                {
                    var now = Environment.TickCount64;
                    if (now - _lastContextMenuShownAtMs < 400)
                    {
                        return _prevWndProc == IntPtr.Zero ? IntPtr.Zero : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
                    }

                    _lastContextMenuShownAtMs = now;
                    InvokeOnUiThread(ShowContextMenu);
                    return IntPtr.Zero;
                }
            }

            return _prevWndProc == IntPtr.Zero ? IntPtr.Zero : CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        private void InvokeOnUiThread(Action action)
        {
            if (_disposed)
            {
                return;
            }

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
                return;
            }

            action();
        }

        private void ShowContextMenu()
        {
            if (_disposed || !TryGetCursorPos(out var pt))
            {
                return;
            }

            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                AppendMenu(hMenu, MF_STRING, (UIntPtr)MenuIdShowExpanded, "显示大窗口");
                AppendMenu(hMenu, MF_STRING, (UIntPtr)MenuIdShowCompact, "显示小窗口");
                AppendMenu(hMenu, MF_STRING, (UIntPtr)MenuIdExit, "退出");

                SetForegroundWindow(_hwnd);
                int selectedId = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_TOPALIGN, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
                if (_disposed)
                {
                    return;
                }

                if (selectedId == MenuIdShowExpanded)
                {
                    _onShowExpandedRequested();
                }
                else if (selectedId == MenuIdShowCompact)
                {
                    _onShowCompactRequested();
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
            pt = default;
            return GetCursorPos(out pt);
        }

        public void Dispose()
        {
            _disposed = true;
            try { Shell_NotifyIcon(NIM_DELETE, ref _nid); } catch { }

            try
            {
                if (_prevWndProc != IntPtr.Zero && _wndProcDelegate != null)
                {
                    SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _prevWndProc);
                }
            }
            catch { }

            try
            {
                if (_iconHandle != IntPtr.Zero)
                {
                    DestroyIcon(_iconHandle);
                    _iconHandle = IntPtr.Zero;
                }
            }
            catch { }
        }
    }
}
