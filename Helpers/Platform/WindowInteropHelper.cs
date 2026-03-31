using System;
using System.Runtime.InteropServices;

namespace CourseList.Helpers.Platform
{
    internal static class WindowInteropHelper
    {
        internal const int WmNcLButtonDblClk = 0x00A3;
        internal const int HtCaption = 0x0002;
        internal const int GwlpWndProc = -4;
        internal const int WmGetMinMaxInfo = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MinMaxInfo
        {
            public Point ptReserved;
            public Point ptMaxSize;
            public Point ptMaxPosition;
            public Point ptMinTrackSize;
            public Point ptMaxTrackSize;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        internal static double GetPixelPerDip(IntPtr hWnd)
        {
            uint dpi = GetDpiForWindow(hWnd);
            if (dpi == 0)
            {
                dpi = 96;
            }

            return dpi / 96.0;
        }

        internal static int DipToPixel(double dip, IntPtr hWnd)
        {
            return (int)Math.Round(dip * GetPixelPerDip(hWnd));
        }

        internal static void SetMinTrackWidthFromDip(IntPtr hWnd, IntPtr lParam, double minWidthDip)
        {
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.ptMinTrackSize.X = DipToPixel(minWidthDip, hWnd);
            Marshal.StructureToPtr(mmi, lParam, true);
        }
    }
}
