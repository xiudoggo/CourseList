using System;
using Microsoft.UI.Xaml.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace CourseList.Helpers
{
    /// <summary>
    /// 全局串行化 ContentDialog，避免多个对话框同时打开导致 Win32/COM 异常。
    /// </summary>
    public static class ContentDialogGuard
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
        {
            await _semaphore.WaitAsync();
            try
            {
                // WinRT: ContentDialog.ShowAsync() 返回 IAsyncOperation<ContentDialogResult>。
                // 某些环境下它不能直接 await（CS4036），因此这里显式把它转换成 Task。
                var operation = dialog.ShowAsync();
                var tcs = new TaskCompletionSource<ContentDialogResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                operation.Completed = (asyncInfo, status) =>
                {
                    try
                    {
                        if (status == AsyncStatus.Completed)
                        {
                            tcs.TrySetResult(asyncInfo.GetResults());
                        }
                        else if (status == AsyncStatus.Canceled)
                        {
                            tcs.TrySetCanceled();
                        }
                        else
                        {
                            tcs.TrySetException(new InvalidOperationException($"ContentDialog async op status: {status}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                };

                return await tcs.Task;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}

