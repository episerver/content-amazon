using System;

namespace EPiServer.Amazon.Events
{
    /// <summary>
    /// Defines a strategy that decides if and when a method that has failed should be retried again.
    /// </summary>
    internal interface IRetryStrategy
    {
        /// <summary>
        /// Method that will decide if a method should be retried after an exception
        /// and for how long it should delay before it's retried.
        /// </summary>
        /// <param name="retryCount">The number of times the method has been retried before.</param>
        /// <param name="lastException">The last exception that was thrown by the method.</param>
        /// <param name="delay">The delay period that any client should wait before calling a method again.</param>
        /// <returns><c>true</c> if a method should be retried again according to the policy; otherwise <c>false</c>.</returns>
        bool ShouldRetry(int retryCount, Exception lastException, out TimeSpan delay);
    }
}
