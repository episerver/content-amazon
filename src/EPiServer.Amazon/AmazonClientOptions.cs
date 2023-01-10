using Amazon;

namespace EPiServer.Amazon
{
    /// <summary>
    /// Holds basic configuration information for Amazon AWS client implementations.
    /// </summary>
    public abstract class AmazonClientOptions
    {
        /// <summary>
        /// Gets or sets the profile name used to connect to Amazon AWS.
        /// </summary>
        public string ProfileName { get; set; }

        /// <summary>
        /// Gets or sets the access key ID used to connect to Amazon AWS.
        /// </summary>
        public string AccessKey { get; set; }

        /// <summary>
        /// Gets or sets the secret key used to connect to Amazon AWS.
        /// </summary>
        public string SecretKey { get; set; }

        /// <summary>
        /// Gets or sets the AWS region that the client should try to connect to.
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// Gets or sets the enabling of auto cleanup queue job.
        /// </summary>
        public bool DisableAutoCleanupQueue { get; set; }

        /// <summary>
        /// Determines whether this instance has credentials based on access key set, i.e. both <see cref="P:AccessKey"/> and <see cref="P:SecretKey"/> is set.
        /// </summary>
        /// <returns>True if both <see cref="P:AccessKey"/> and <see cref="P:SecretKey"/> is set; otherwise false</returns>
        public bool HasAccessKeyCredentials()
        {
            return !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey);
        }
    }
}
