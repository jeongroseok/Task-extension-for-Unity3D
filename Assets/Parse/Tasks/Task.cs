using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents an asynchronous task.
    /// </summary>
    public abstract class Task : IEnumerator
    {
        private static readonly ThreadLocal<int> executionDepth = new ThreadLocal<int>(() => 0);
        private static readonly Action<Action> immediateExecutor = a =>
        {
            // TODO (hallucinogen): remove this after Unity resolves the ThreadPool problem.
            bool IsCompiledByIL2CPP = System.AppDomain.CurrentDomain.FriendlyName.Equals("IL2CPP Root Domain");
            int maxDepth = 10;
            if (IsCompiledByIL2CPP)
            {
                maxDepth = 200;
            }
            executionDepth.Value++;
            try
            {
                if (executionDepth.Value <= maxDepth)
                {
                    a();
                }
                else
                {
                    Factory.Scheduler.Post(a);
                }
            }
            finally
            {
                executionDepth.Value--;
            }
        };

        protected readonly object mutex = new object();
        protected IList<Action<Task>> continuations = new List<Action<Task>>();

        protected Task()
        {
        }

        internal static TaskFactory Factory
        {
            get
            {
                return new TaskFactory();
            }
        }

        protected AggregateException exception;
        /// <summary>
        /// Gets the exceptions for the task, if there are any. <c>null</c> otherwise.
        /// </summary>
        public AggregateException Exception
        {
            get
            {
                lock (mutex)
                {
                    return exception;
                }
            }
        }

        protected bool isCanceled;
        /// <summary>
        /// Gets whether the task was cancelled.
        /// </summary>
        public bool IsCanceled
        {
            get
            {
                lock (mutex)
                {
                    return isCanceled;
                }
            }
        }

        protected bool isCompleted;
        /// <summary>
        /// Gets whether the task has been completed with either an exception,
        /// cancellation, or a result.
        /// </summary>
        public bool IsCompleted
        {
            get
            {
                lock (mutex)
                {
                    return isCompleted;
                }
            }
        }

        /// <summary>
        /// Gets whether the task failed.
        /// </summary>
        public bool IsFaulted { get { return Exception != null; } }

        /// <summary>
        /// Blocks until the task is complete.
        /// </summary>
        public void Wait()
        {
            lock (mutex)
            {
                if (!IsCompleted)
                {
                    Monitor.Wait(mutex);
                }
                if (IsFaulted)
                {
                    throw Exception;
                }
            }
        }

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <typeparam name="T">The type returned by the continuation.</typeparam>
        /// <param name="continuation">The continuation to run after the task completes.
        /// The function takes the completed task as an argument and can return a value.</param>
        /// <returns>A new Task that returns the value returned by the continuation after both
        /// the task and the continuation are complete.</returns>
        public Task<T> ContinueWith<T>(Func<Task, T> continuation)
        {
            return ContinueWith(continuation, CancellationToken.None);
        }

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <typeparam name="T">The type returned by the continuation.</typeparam>
        /// <param name="continuation">The continuation to run after the task completes.
        /// The function takes the completed task as an argument and can return a value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A new Task that returns the value returned by the continuation after both
        /// the task and the continuation are complete.</returns>
        public Task<T> ContinueWith<T>(Func<Task, T> continuation, CancellationToken cancellationToken)
        {
            bool completed = false;
            var tcs = new TaskCompletionSource<T>();
            var cancellation = cancellationToken.Register(() => tcs.TrySetCanceled());
            Action<Task> completeTask = t =>
            {
                immediateExecutor(() =>
                {
                    try
                    {
                        tcs.TrySetResult(continuation(t));
                        cancellation.Dispose();
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e);
                        cancellation.Dispose();
                    }
                });
            };

            lock (mutex)
            {
                completed = IsCompleted;
                if (!completed)
                {
                    continuations.Add(completeTask);
                }
            }

            if (completed)
            {
                completeTask(this);
            }
            return tcs.Task;
        }

        #region Custom functions for unity3d

        public Task ContinueWith(YieldInstruction yieldInstruction, CancellationToken cancellationToken = default(CancellationToken))
        {
            return ContinueWith(YieldInstructionToEnumerator(yieldInstruction));
        }

        public Task ContinueWith(IEnumerator coroutine, CancellationToken cancellationToken = default(CancellationToken))
        {
            bool completed = false;
            var tcs = new TaskCompletionSource<int>();
            var cancellation = cancellationToken.Register(() => tcs.TrySetCanceled());
            Action<Task> completeTask = t =>
            {
                immediateExecutor(() =>
                {
                    try
                    {
                        TaskScheduler.FromUnityContext().Post(WaitCoroutine(coroutine, () => tcs.TrySetResult(0)));
                        cancellation.Dispose();
                    }
                    catch (Exception e)
                    {
                        tcs.TrySetException(e);
                        cancellation.Dispose();
                    }
                });
            };

            lock (mutex)
            {
                completed = IsCompleted;
                if (!completed)
                {
                    continuations.Add(completeTask);
                }
            }

            if (completed)
            {
                completeTask(this);
            }
            return tcs.Task;

        }

        private static IEnumerator WaitCoroutine(IEnumerator coroutine, Action done)
        {
            yield return coroutine;
            done();
        }

        private static IEnumerator YieldInstructionToEnumerator(YieldInstruction yieldInstruction)
        {
            yield return yieldInstruction;
        }

        #endregion

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <param name="continuation">The continuation to run after the task completes.
        /// The function takes the completed task as an argument.</param>
        /// <returns>A new Task that is complete after both the task and the continuation are
        /// complete.</returns>
        public Task ContinueWith(Action<Task> continuation)
        {
            return ContinueWith(continuation, CancellationToken.None);
        }

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <param name="continuation">The continuation to run after the task completes.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A new Task that is complete after both the task and the continuation are
        /// complete.</returns>
        public Task ContinueWith(Action<Task> continuation, CancellationToken cancellationToken)
        {
            return ContinueWith<int>(t =>
            {
                continuation(t);
                return 0;
            }, cancellationToken);
        }

        /// <summary>
        /// Creates a task that is complete when all of the provided tasks are complete.
        /// If any of the tasks has an exception, all exceptions raised in the tasks will
        /// be aggregated into the returned task. Otherwise, if any of the tasks is cancelled,
        /// the returned task will be cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to aggregate.</param>
        /// <returns>A task that is complete when all of the provided tasks are complete.</returns>
        public static Task WhenAll(params Task[] tasks)
        {
            return WhenAll((IEnumerable<Task>)tasks);
        }

        /// <summary>
        /// Creates a task that is complete when all of the provided tasks are complete.
        /// If any of the tasks has an exception, all exceptions raised in the tasks will
        /// be aggregated into the returned task. Otherwise, if any of the tasks is cancelled,
        /// the returned task will be cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to aggregate.</param>
        /// <returns>A task that is complete when all of the provided tasks are complete.</returns>
        public static Task WhenAll(IEnumerable<Task> tasks)
        {
            var taskArr = tasks.ToArray();
            if (taskArr.Length == 0)
            {
                return Task.FromResult(0);
            }
            var tcs = new TaskCompletionSource<int>();
            Task.Factory.ContinueWhenAll(taskArr, _ =>
            {
                var exceptions = (from t in taskArr
                                  where t.IsFaulted
                                  select t.Exception).ToArray();
                if (exceptions.Length > 0)
                {
                    tcs.SetException(new AggregateException(exceptions));
                }
                else if (taskArr.Any(t => t.IsCanceled))
                {
                    tcs.SetCanceled();
                }
                else
                {
                    tcs.SetResult(0);
                }
            });
            return tcs.Task;
        }

        protected static Task<Task> WhenAny(params Task[] tasks)
        {
            return WhenAny((IEnumerable<Task>)tasks);
        }

        protected static Task<Task> WhenAny(IEnumerable<Task> tasks)
        {
            var tcs = new TaskCompletionSource<Task>();
            foreach (var task in tasks)
            {
                task.ContinueWith(t => tcs.TrySetResult(t));
            }
            return tcs.Task;
        }

        /// <summary>
        /// Creates a new, completed task for the given result.
        /// </summary>
        /// <typeparam name="T">The result type of the task.</typeparam>
        /// <param name="result"></param>
        /// <returns>A completed task.</returns>
        public static Task<T> FromResult<T>(T result)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(result);
            return tcs.Task;
        }

        /// <summary>
        /// Executes a function asynchronously, returning a task that represents the operation.
        /// </summary>
        /// <typeparam name="T">The return type of the task.</typeparam>
        /// <param name="toRun">The function to run.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task<T> Run<T>(Func<T> toRun)
        {
            return Task.Factory.StartNew(toRun);
        }

        /// <summary>
        /// Executes an action asynchronously, returning a task that represents the operation.
        /// </summary>
        /// <param name="toRun">The action to run.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static Task Run(Action toRun)
        {
            return Task.Factory.StartNew(() =>
            {
                toRun();
                return 0;
            });
        }

        /// <summary>
        /// Creates a task that will complete successfully after the given timespan.
        /// </summary>
        /// <param name="timespan">The amount of time to wait.</param>
        /// <returns>A new task.</returns>
        public static Task Delay(TimeSpan timespan)
        {
            var tcs = new TaskCompletionSource<int>();
            var timer = new Timer(_ =>
            {
                tcs.TrySetResult(0);
            });
            timer.Change(timespan, TimeSpan.FromMilliseconds(-1));
            return tcs.Task;
        }

        #region implements IEnumerator
        public object Current { get { return null; } }
        public bool MoveNext() { return !IsCompleted; }
        public void Reset() { }
        #endregion
    }

    /// <summary>
    /// Represents an asynchronous task that has a result.
    /// </summary>
    /// <typeparam name="T">The type of the task's result.</typeparam>
    public sealed class Task<T> : Task
    {
        internal Task()
        {
        }

        private T result;
        /// <summary>
        /// Gets the result of the task. If the task is not complete, this property blocks
        /// until the task is complete. If the task has an Exception or was cancelled, this
        /// property will rethrow the exception.
        /// </summary>
        public T Result
        {
            get
            {
                Wait();
                return result;
            }
        }

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <param name="continuation">The continuation to run after the task completes.
        /// The function takes the completed task as an argument.</param>
        /// <returns>A new Task that is complete after both the task and the continuation are
        /// complete.</returns>
        public Task ContinueWith(Action<Task<T>> continuation)
        {
            return base.ContinueWith(t => continuation((Task<T>)t));
        }

        /// <summary>
        /// Registers a continuation for the task that will run when the task is complete.
        /// </summary>
        /// <typeparam name="TResult">The type returned by the continuation.</typeparam>
        /// <param name="continuation">The continuation to run after the task completes.
        /// The function takes the completed task as an argument and can return a value.</param>
        /// <returns>A new Task that returns the value returned by the continuation after both
        /// the task and the continuation are complete.</returns>
        public Task<TResult> ContinueWith<TResult>(Func<Task<T>, TResult> continuation)
        {
            return base.ContinueWith(t => continuation((Task<T>)t));
        }

        private void RunContinuations()
        {
            lock (mutex)
            {
                foreach (var continuation in continuations)
                {
                    continuation(this);
                }
                continuations = null;
            }
        }

        internal bool TrySetResult(T result)
        {
            lock (mutex)
            {
                if (isCompleted)
                {
                    return false;
                }
                isCompleted = true;
                this.result = result;
                Monitor.PulseAll(mutex);
                RunContinuations();
                return true;
            }
        }

        internal bool TrySetCanceled()
        {
            lock (mutex)
            {
                if (isCompleted)
                {
                    return false;
                }
                isCompleted = true;
                this.isCanceled = true;
                Monitor.PulseAll(mutex);
                RunContinuations();
                return true;
            }
        }

        internal bool TrySetException(AggregateException exception)
        {
            lock (mutex)
            {
                if (isCompleted)
                {
                    return false;
                }
                isCompleted = true;
                this.exception = exception;
                Monitor.PulseAll(mutex);
                RunContinuations();
                return true;
            }
        }
    }
}
