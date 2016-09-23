using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Provides extension methods for working with <see cref="Task"/>s.
    /// </summary>
    public static class TaskExtensions
    {
        public static Task Unwrap(this Task<Task> task)
        {
            var tcs = new TaskCompletionSource<int>();
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    tcs.TrySetException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    task.Result.ContinueWith(inner =>
                    {
                        if (inner.IsFaulted)
                        {
                            tcs.TrySetException(inner.Exception);
                        }
                        else if (inner.IsCanceled)
                        {
                            tcs.TrySetCanceled();
                        }
                        else
                        {
                            tcs.TrySetResult(0);
                        }
                    });
                }
            });
            return tcs.Task;
        }
        
        public static Task<T> Unwrap<T>(this Task<Task<T>> task)
        {
            var tcs = new TaskCompletionSource<T>();
            task.ContinueWith(new Action<Task<Task<T>>>((Task<Task<T>> t) =>
            {
                if (t.IsFaulted)
                {
                    tcs.TrySetException(t.Exception);
                }
                else if (t.IsCanceled)
                {
                    tcs.TrySetCanceled();
                }
                else
                {
                    t.Result.ContinueWith((Task<T> inner) =>
                    {
                        if (inner.IsFaulted)
                        {
                            tcs.TrySetException(inner.Exception);
                        }
                        else if (inner.IsCanceled)
                        {
                            tcs.TrySetCanceled();
                        }
                        else
                        {
                            tcs.TrySetResult(inner.Result);
                        }
                    });
                }
            }));
            return tcs.Task;
        }
    }
}
