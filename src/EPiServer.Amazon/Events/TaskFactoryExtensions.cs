using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EPiServer.Logging;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Extension methods for <see cref="TaskFactory"/> that supports starting continuous tasks.
    /// </summary>
    internal static class TaskFactoryExtensions
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>
        /// Runs the given function continuously until cancelled.
        /// Each time the provided function returns a result, the provided action method is called with this result.
        /// </summary>
        /// <param name="taskFactory">The task factory.</param>
        /// <param name="action">The action that should be called continuously.</param>
        /// <param name="cancellationToken">A cancellation token that will be assigned each tasks created.</param>
        /// <param name="retryStrategy">The retry strategy that should be used in case where the function throws an exception.</param>
        /// <returns>A task representing the continuous task. It will only complete if faulted or cancelled.</returns>
        /// <remarks>
        /// The method calls are done in sequential and synchronized order, e.g. the second function call is not done until the first action method has completed.
        /// The task will fault immediately if the action method throws an exception.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to ensure that all types of Exceptions are run past the retry strategy.")]
        public static Task StartContinuous(this TaskFactory taskFactory, Action action, CancellationToken cancellationToken, IRetryStrategy retryStrategy)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            if (retryStrategy == null)
                throw new ArgumentNullException("retryStrategy");

            return taskFactory.StartNew(() =>
                {
                    var exceptions = new List<Exception>();

                    // Run for all eternity
                    while (true)
                    {
                        try
                        {
                            action();
                            // Clear old exceptions after a successful run
                            exceptions.Clear();
                        }
                        catch (TaskCanceledException)
                        {
                            Logger.Debug("A continuous task was cancelled");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                            Logger.Error(string.Format(CultureInfo.InvariantCulture, "An exception occurred during a continuous task execution. This was attempt no. {0}.", exceptions.Count), ex);

                            TimeSpan retryDelay;
                            if (!retryStrategy.ShouldRetry(exceptions.Count, ex, out retryDelay))
                            {
                                Logger.Debug("Exiting continuous task after the provided retry policy returned false.");
                                throw new AggregateException(exceptions);
                            }

                            // Delay retry or cancel the whole task
                            cancellationToken.WaitHandle.WaitOne(retryDelay);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                },
                cancellationToken,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);
        }

        //public static Task StartContinuous<TResult>(this TaskFactory taskFactory, Func<TResult> function, Action<TResult> action, CancellationToken cancellationToken, IRetryStrategy retryStrategy)
        //{
        //    int errorCount = 0;
        //    bool exit = false;

        //    return taskFactory.StartNew(() =>
        //    {
        //        // Run in all eternety
        //        while (!exit)
        //        {
        //            // Call provided function
        //            var mainTask = taskFactory.StartNew(function, cancellationToken, TaskCreationOptions.AttachedToParent, TaskScheduler.Current);

        //            var resultTask = mainTask.ContinueWith(t =>
        //            {
        //                // If the function execution throws an error
        //                if (t.IsFaulted)
        //                {
        //                    _log.Error(string.Format(CultureInfo.InvariantCulture, "An exception occurred during a continuous task execution. This was retry no. {0}.", errorCount + 1), t.Exception.InnerException);
        //                    TimeSpan retryDelay;
        //                    if (!retryStrategy.ShouldRetry(++errorCount, t.Exception.InnerException, out retryDelay))
        //                    {
        //                        _log.Debug("Exiting continuous task after retry policy returned false.");
        //                        exit = true;
        //                    }
        //                    else
        //                    {
        //                        // Delay retry or cancel the whole task
        //                        cancellationToken.WaitHandle.WaitOne(retryDelay);
        //                    }
        //                    return; // Exit result task
        //                }

        //                // Use function result
        //                if (t.Result != null)
        //                {
        //                    action(t.Result);
        //                }
        //                errorCount = 0;
        //            },
        //                cancellationToken, TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.AttachedToParent, TaskScheduler.Current);

        //            // Wait until both main function and result action has completed
        //            resultTask.Wait(cancellationToken);
        //        }
        //    },
        //        cancellationToken,
        //        TaskCreationOptions.LongRunning,
        //        TaskScheduler.Current);
        //}

    }
}
