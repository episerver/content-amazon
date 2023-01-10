using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EPiServer.Amazon.Events;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class TaskFactoryExtensionsTest
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);

        private static void Wait(Task t, TimeSpan? timeout = null)
        {
            try
            {
                t.Wait(timeout.GetValueOrDefault(TestTimeout));
            }
            catch (TaskCanceledException) { }
            catch (AggregateException) { }
        }

        [Fact]
        public void StartContinuous_ShouldRunContinuously()
        {
            var tokenSource = new CancellationTokenSource();
            int expected = new Random().Next(10, 100);
            int counter = 0;
            Action action = () =>
            {
                if (++counter >= expected)
                {
                    tokenSource.Cancel();
                }
            };

            var t = Task.Factory.StartContinuous(action, tokenSource.Token, NoRetries.Instance);

            Wait(t);

            Assert.True(t.IsCanceled, string.Format("Task status was not cancelled but '{0}'.", t.Status));
            Assert.Equal(expected, counter);
        }

        [Fact]
        public void StartContinuous_ShouldStopWhenCancelled()
        {
            int counter = 0;
            var tokenSource = new CancellationTokenSource();

            var t = Task.Factory.StartContinuous(() => counter++, tokenSource.Token, NoRetries.Instance);

            // Let it run for a bit
            Thread.Sleep(50);
            tokenSource.Cancel();

            // Ensure that all tasks has finished before reading the counter
            Thread.Sleep(20);
            var expected = counter;

            // See if if contiues to run
            Wait(t, TimeSpan.FromMilliseconds(200));

            Assert.Equal(expected, counter);
        }

        [Fact]
        public void StartContinuous_WhenExeptionIsThrown_ShouldContinue()
        {
            var tokenSource = new CancellationTokenSource();
            int expected = new Random().Next(10, 100);
            int counter = 0;
            Action action = () =>
            {
                counter++;
                if (counter >= expected)
                {
                    tokenSource.Cancel();
                }
                throw new NotSupportedException();
            };

            var t = Task.Factory.StartContinuous(action, tokenSource.Token, InfiniteRetries.Instance);

            Wait(t);

            Assert.Equal(expected, counter);
        }

        [Fact]
        public void StartContinuous_WhenCancelledDuringExceptionDelay_ShouldExitFast()
        {
            var tokenSource = new CancellationTokenSource();
            Action action = () => { throw new NotSupportedException(); };
            var delay = TimeSpan.FromSeconds(2);
            var stopwatch = Stopwatch.StartNew();

            var task = Task.Factory.StartContinuous(action, tokenSource.Token, new IncrementalRetries(1, delay, TimeSpan.Zero));

            try
            {
                // Simulate a cancellation while the task is delaying before first retry
                task.Wait(50);
                tokenSource.Cancel();

                task.Wait(delay); // Task should be finished after delay at the latest
            }
            catch (AggregateException) { }

            stopwatch.Stop();

            Assert.True(stopwatch.Elapsed < delay, string.Format("Elapsed {0} was not less than delay {1}", stopwatch.Elapsed, delay));
        }

        [Fact]
        public void StartContinuous_WhenExeptionIsThrown_ShouldDelayExecutionOfNextCall()
        {
            var delay = TimeSpan.FromMilliseconds(150);

            // The delay handling for tasks is done with less precision than the Stopwatch class
            // This can give false errors when checking that Elapsed time is infact more than the 
            // requested delay. Therefore we add a level of acceptable error with the delayJitter
            var delayJitter = TimeSpan.FromMilliseconds(10);

            var tokenSource = new CancellationTokenSource();
            var stopwatch = new Stopwatch();
            Action action = () =>
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                    tokenSource.Cancel();
                }
                else
                {
                    stopwatch.Start();
                    throw new NotSupportedException();
                }
            };


            var t = Task.Factory.StartContinuous(action, tokenSource.Token, new IncrementalRetries(1, delay, TimeSpan.Zero));

            Wait(t);

            Assert.True(stopwatch.Elapsed >= (delay - delayJitter), string.Format("Elapsed {0} was not >= delay {1}", stopwatch.Elapsed, delay));
            Assert.True(stopwatch.Elapsed < TestTimeout, string.Format("Elapsed {0} was not < TestTimeout {1}", stopwatch.Elapsed, TestTimeout));
        }

        public class InfiniteRetries : IRetryStrategy
        {
            private InfiniteRetries() { }

            public static InfiniteRetries Instance = new InfiniteRetries();

            public bool ShouldRetry(int retryCount, Exception lastException, out TimeSpan delay)
            {
                delay = TimeSpan.Zero;
                if (lastException is StackOverflowException)
                {
                    return false;
                }
                return true;
            }
        }

        public class NoRetries : IRetryStrategy
        {
            private NoRetries() { }

            public static NoRetries Instance = new NoRetries();

            public bool ShouldRetry(int retryCount, Exception lastException, out TimeSpan delay)
            {
                delay = TimeSpan.Zero;
                return false;
            }
        }
    }
}
