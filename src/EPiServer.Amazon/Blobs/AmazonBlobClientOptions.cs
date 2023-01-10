using EPiServer.ServiceLocation;

namespace EPiServer.Amazon.Blobs
{
    /// <summary>
    /// Holds configuration information for the <see cref="AmazonBlobProvider"/>.
    /// </summary>
    [Options(ConfigurationSection = "AmazonBlob")]
    public class AmazonBlobClientOptions : AmazonClientOptions
    {
        /// <summary>
        /// Gets or sets the name of the bucket that should be used.
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// Gets or sets what chunk size to request when downloading files.
        /// </summary>
        public int? DownloadChunkSize { get; set; }
    }
}
