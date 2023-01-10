using System;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// A retry strategy that will increase the delay between retries with a given time span each time.
    /// </summary>
    internal class IncrementalRetries : IRetryStrategy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IncrementalRetries"/> class.
        /// </summary>
        /// <param name="retryCount">The maximum number of retries. A negative value means that an infinite number of retries will be done.</param>
        /// <param name="initialInterval">The initial delay interval before the first retry.</param>
        /// <param name="intervalIncrement">The interval that is added with each retry.</param>
        /// <exception cref="System.ArgumentOutOfRangeException" />
        public IncrementalRetries(int retryCount, TimeSpan initialInterval, TimeSpan intervalIncrement)
        {
            if (initialInterval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("initialInterval", "Initial interval cannot be negative.");
            if (intervalIncrement < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("intervalIncrement", "Interval increment cannot be negative.");

            RetryCount = retryCount;
            InitialInterval = initialInterval;
            IntervalIncrement = intervalIncrement;
        }

        /// <summary>
        /// Gets the maximum number of retries.
        /// </summary>
        public int RetryCount { get; private set; }

        /// <summary>
        /// Gets initial delay interval before the first retry.
        /// </summary>
        public TimeSpan InitialInterval { get; private set; }

        /// <summary>
        /// Gets the interval with which the delay is increased with each retry.
        /// </summary>
        public TimeSpan IntervalIncrement { get; private set; }

        /// <summary>
        /// Method that will decide if a method should be retried after an exception
        /// and for how long it should delay before it's retried.
        /// </summary>
        /// <param name="retryCount">The number of retry attempts including the current one.</param>
        /// <param name="lastException">The last exception that was thrown by the method.</param>
        /// <param name="delay">The delay period that any client should wait before calling a method again.</param>
        /// <returns><c>true</c> if a method should be retried again according to the policy; otherwise <c>false</c>.</returns>
        public bool ShouldRetry(int retryCount, Exception lastException, out TimeSpan delay)
        {
            if (RetryCount > 0 && retryCount > RetryCount)
            {
                delay = TimeSpan.Zero;
                return false;
            }

            delay = TimeSpan.FromMilliseconds(InitialInterval.TotalMilliseconds + retryCount * IntervalIncrement.TotalMilliseconds);
            return true;
        }
    }
}
