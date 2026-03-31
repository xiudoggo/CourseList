using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CourseList.Helpers.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace CourseList.Helpers.Ui
{
    internal sealed class WindowModeController : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly AppWindow _appWindow;
        private readonly Button _toggleButton;
        private readonly FrameworkElement _iconSmall;
        private readonly FrameworkElement _iconLarge;
        private readonly TextBlock _brandTitleText;
        private readonly double _minWindowWidthDip;
        private readonly double _compactWidthDip;
        private readonly double _expandedWidthDip;
        private readonly double _expandedModeThresholdWidthDip;

        private bool _isAnimating;
        private bool _targetCompact;
        private CancellationTokenSource? _animationCts;
        private Storyboard? _iconStoryboard;

        internal WindowModeController(
            IntPtr hwnd,
            AppWindow appWindow,
            Button toggleButton,
            FrameworkElement iconSmall,
            FrameworkElement iconLarge,
            TextBlock brandTitleText,
            double minWindowWidthDip,
            double compactWidthDip,
            double expandedWidthDip,
            double expandedModeThresholdWidthDip)
        {
            _hwnd = hwnd;
            _appWindow = appWindow;
            _toggleButton = toggleButton;
            _iconSmall = iconSmall;
            _iconLarge = iconLarge;
            _brandTitleText = brandTitleText;
            _minWindowWidthDip = minWindowWidthDip;
            _compactWidthDip = compactWidthDip;
            _expandedWidthDip = expandedWidthDip;
            _expandedModeThresholdWidthDip = expandedModeThresholdWidthDip;
        }

        internal void UpdateToggleTip()
        {
            bool isCompact = false;
            try
            {
                isCompact = IsCurrentlyCompact();
            }
            catch
            {
                // ignore; keep current tooltip
            }

            ToolTipService.SetToolTip(_toggleButton, isCompact ? "切换为大窗口模式" : "切换为小窗口模式");
            SetIconStateInstant(isCompact);
            _brandTitleText.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
        }

        internal async Task ToggleAsync()
        {
            bool targetCompact = _isAnimating ? !_targetCompact : !IsCurrentlyCompact();
            await SetModeAsync(targetCompact);
        }

        internal async Task SetCompactAsync()
        {
            await SetModeAsync(true);
        }

        internal async Task SetExpandedAsync()
        {
            await SetModeAsync(false);
        }

        public void Dispose()
        {
            try
            {
                _animationCts?.Cancel();
                _animationCts?.Dispose();
                _animationCts = null;
            }
            catch
            {
                // ignore
            }

            try
            {
                _iconStoryboard?.Stop();
                _iconStoryboard = null;
            }
            catch
            {
                // ignore
            }
        }

        private (double widthDip, double heightDip) GetCurrentSizeDip()
        {
            double pixelPerDip = WindowInteropHelper.GetPixelPerDip(_hwnd);
            var pxSize = _appWindow.Size;
            return (pxSize.Width / pixelPerDip, pxSize.Height / pixelPerDip);
        }

        private bool IsCurrentlyCompact()
        {
            var (widthDip, _) = GetCurrentSizeDip();
            return widthDip < _expandedModeThresholdWidthDip;
        }

        private void ResizeWindowToDip(double widthDip, double heightDip)
        {
            double pixelPerDip = WindowInteropHelper.GetPixelPerDip(_hwnd);

            int widthPx = Math.Max(
                (int)Math.Round(widthDip * pixelPerDip),
                (int)Math.Round(_minWindowWidthDip * pixelPerDip));
            int heightPx = (int)Math.Round(heightDip * pixelPerDip);

            var pos = _appWindow.Position;
            _appWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, widthPx, heightPx));
        }

        private void ResizeWindowToDipWithPixelPerDip(double widthDip, double heightDip, double pixelPerDip)
        {
            int widthPx = Math.Max(
                (int)Math.Round(widthDip * pixelPerDip),
                (int)Math.Round(_minWindowWidthDip * pixelPerDip));
            int heightPx = (int)Math.Round(heightDip * pixelPerDip);

            var pos = _appWindow.Position;
            _appWindow.MoveAndResize(new RectInt32(pos.X, pos.Y, widthPx, heightPx));
        }

        private void SetIconStateInstant(bool isCompact)
        {
            _iconSmall.Opacity = isCompact ? 1.0 : 0.0;
            _iconLarge.Opacity = isCompact ? 0.0 : 1.0;
        }

        private void AnimateIconOpacity(bool targetIsCompact)
        {
            try
            {
                _iconStoryboard?.Stop();
            }
            catch
            {
                // ignore
            }

            const int durationMs = 200;
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var sb = new Storyboard();

            var smallAnim = new DoubleAnimation
            {
                To = targetIsCompact ? 1.0 : 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(smallAnim, _iconSmall);
            Storyboard.SetTargetProperty(smallAnim, "Opacity");
            sb.Children.Add(smallAnim);

            var largeAnim = new DoubleAnimation
            {
                To = targetIsCompact ? 0.0 : 1.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(largeAnim, _iconLarge);
            Storyboard.SetTargetProperty(largeAnim, "Opacity");
            sb.Children.Add(largeAnim);

            _iconStoryboard = sb;
            sb.Begin();
        }

        private async Task AnimateWindowWidthAsync(double targetWidthDip, double targetHeightDip, CancellationToken cancellationToken)
        {
            var (startWidthDip, _) = GetCurrentSizeDip();
            if (Math.Abs(startWidthDip - targetWidthDip) < 0.5)
            {
                ResizeWindowToDip(targetWidthDip, targetHeightDip);
                return;
            }

            double pixelPerDip = WindowInteropHelper.GetPixelPerDip(_hwnd);

            const double durationMs = 400.0;
            const int frameDelayMs = 8;
            var sw = Stopwatch.StartNew();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / durationMs, 0.0, 1.0);
                double eased = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                double widthDip = startWidthDip + (targetWidthDip - startWidthDip) * eased;
                ResizeWindowToDipWithPixelPerDip(widthDip, targetHeightDip, pixelPerDip);

                if (t >= 1.0)
                {
                    break;
                }

                await Task.Delay(frameDelayMs, cancellationToken);
            }

            ResizeWindowToDip(targetWidthDip, targetHeightDip);
        }

        private async Task ToggleWindowModeAsync(bool targetCompact, CancellationToken cancellationToken)
        {
            try
            {
                if (_appWindow.Presenter is OverlappedPresenter presenter &&
                    presenter.State == OverlappedPresenterState.Maximized)
                {
                    presenter.Restore();
                    await Task.Delay(60, cancellationToken);
                }
            }
            catch
            {
                // ignore
            }

            var (_, heightDip) = GetCurrentSizeDip();
            double targetWidthDip = targetCompact ? _compactWidthDip : _expandedWidthDip;

            AnimateIconOpacity(targetCompact);

            try
            {
                _appWindow.TitleBar.PreferredHeightOption = targetCompact
                    ? TitleBarHeightOption.Collapsed
                    : TitleBarHeightOption.Tall;
            }
            catch
            {
                // ignore
            }

            await AnimateWindowWidthAsync(targetWidthDip, heightDip, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            SetIconStateInstant(targetCompact);
            ToolTipService.SetToolTip(_toggleButton, targetCompact ? "切换为大窗口模式" : "切换为小窗口模式");
        }

        private async Task SetModeAsync(bool targetCompact)
        {
            bool currentCompact = false;
            try
            {
                currentCompact = IsCurrentlyCompact();
            }
            catch
            {
                // ignore
            }

            if (!_isAnimating && currentCompact == targetCompact)
            {
                UpdateToggleTip();
                return;
            }

            _targetCompact = targetCompact;
            _animationCts?.Cancel();
            _animationCts?.Dispose();
            _animationCts = new CancellationTokenSource();

            _isAnimating = true;
            try
            {
                await ToggleWindowModeAsync(targetCompact, _animationCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when user clicks repeatedly during animation.
            }
            finally
            {
                _isAnimating = false;
            }
        }
    }
}
