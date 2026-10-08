using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using EPiServer.Framework.Blobs;
using Microsoft.Extensions.FileProviders;

namespace EPiServer.Amazon.Blobs
{
    /// <summary>
    /// Blob implementation that represents a blob stored in the Amazon S3 cloud service.
    /// </summary>
    public class AmazonBlob : Blob
    {
        private const int DefaultDownloadChunkSize = 2097152; // 2MB

        private readonly IAmazonS3 _storageClient;
        private readonly string _bucketName;
        private int _downloadChunkSize = DefaultDownloadChunkSize;

        /// <summary>
        /// The maximum chunk size that is allowed when downloading a blob.
        /// </summary>
        public static readonly int MaximumDownloadChunkSize = 4194304; // 4MB

        /// <summary>
        /// Initializes a new instance of the <see cref="AmazonBlob"/> class.
        /// </summary>
        /// <param name="storageClient">The storage client to use when communicating with the service.</param>
        /// <param name="bucketName">Name of the bucket to use.</param>
        /// <param name="id">The unique identifier of the blob.</param>
        public AmazonBlob(IAmazonS3 storageClient, string bucketName, Uri id)
            : base(id)
        {
            _storageClient = storageClient;
            _bucketName = bucketName;
        }

        /// <summary>
        /// Gets the name of the bucket where this blob is placed.
        /// </summary>
        public string BucketName
        {
            get { return _bucketName; }
        }

        /// <summary>
        /// Gets the Amazon S3 key that represents this blob.
        /// </summary>
        public string Key
        {
            get { return ConvertToKey(ID); }
        }

        /// <summary>
        /// Gets or sets the chunk size in bytes to use when requesting downloads. 
        /// The value cannot exceed 4MB (4194304).
        /// The default is 2MB.
        /// </summary>
        public int DownloadChunkSize
        {
            get
            {
                return _downloadChunkSize;
            }
            set
            {
                if (value < 0 || value > MaximumDownloadChunkSize)
                {
                    throw new ArgumentOutOfRangeException("value", string.Format(CultureInfo.InvariantCulture, "DownloadChunkSize cannot be less than zero or more than {0}", MaximumDownloadChunkSize));
                }
                _downloadChunkSize = value;
            }
        }

        /// <inheritdoc />
        public override Stream OpenRead()
        {
            // Do a head request to get Content Length.
            var request = new GetObjectMetadataRequest
            {
                BucketName = BucketName,
                Key = Key,
                
            };

            var metadata = _storageClient.GetObjectMetadataAsync(request).GetAwaiter().GetResult();

            return new BufferedDownloadStream(metadata.ContentLength, DownloadChunkSize, (d, p, c) => DownloadByteRange(d, p, c, metadata.ETag));
        }

        /// <inheritdoc />
        public override Stream OpenWrite()
        {
            // Create local file stream and upload it to S3 once the stream is closed.
            var tempFileName = Path.GetTempFileName();
            var stream = new TrackableStream(new FileStream(tempFileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None));
            stream.Closing += (s, e) =>
            {
                // Commits the whole file once it has been uploaded locally
                var innerStream = ((TrackableStream)s).InnerStream;
                innerStream.Seek(0, SeekOrigin.Begin);
                Write(innerStream);
            };
            stream.Closed += (s, e) =>
            {
                var file = new FileInfo(tempFileName);
                if (file.Exists)
                {
                    file.Delete();
                }
            };
            return stream;
        }

        /// <inheritdoc />
        public override void Write(Stream data)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            // Use Transfer Utility since it nicely supports multipart chunking for large blobs
            using (var transfer = new TransferUtility(_storageClient))
            {
                transfer.Upload(data, BucketName, Key);
            }
        }

        /// <inheritdoc />
        public override async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
        {
            // Do a head request to get Content Length.
            var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
            if (metadata == null)
            {
                throw new BlobNotFoundException(ID);
            }

            return CreateDownloadStream(metadata);
        }

        /// <inheritdoc />
        public override Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
        {
            // The returned stream buffers to a local file and only uploads once it is closed,
            // so there is nothing to await here.
            return Task.FromResult(OpenWrite());
        }

        /// <inheritdoc />
        public override async Task WriteAsync(Stream data, CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentNullException("data");

            // Use Transfer Utility since it nicely supports multipart chunking for large blobs
            using (var transfer = new TransferUtility(_storageClient))
            {
                await transfer.UploadAsync(data, BucketName, Key, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public override bool Exists()
        {
            return GetMetadataAsync(CancellationToken.None).GetAwaiter().GetResult() != null;
        }

        /// <inheritdoc />
        public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
        {
            return await GetMetadataAsync(cancellationToken).ConfigureAwait(false) != null;
        }

        /// <inheritdoc />
        public override async Task<IFileInfo> AsFileInfoAsync(DateTimeOffset lastModified, bool openReadStream, CancellationToken cancellationToken)
        {
            var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
            if (metadata == null)
            {
                return new AmazonBlobFileInfo(Key, lastModified);
            }

            // IFileInfo.CreateReadStream is synchronous, so the content has to be opened up front
            // when the caller intends to read it. Ownership passes to the caller.
            var content = openReadStream ? CreateDownloadStream(metadata) : null;
            return new AmazonBlobFileInfo(Key, lastModified, metadata.ContentLength, content);
        }

        /// <summary>
        /// Requests the metadata of the S3 object that this blob represents.
        /// </summary>
        /// <returns>The object metadata, or <c>null</c> if no such object exists.</returns>
        private async Task<GetObjectMetadataResponse> GetMetadataAsync(CancellationToken cancellationToken)
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = BucketName,
                Key = Key
            };

            try
            {
                return await _storageClient.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        private Stream CreateDownloadStream(GetObjectMetadataResponse metadata)
        {
            return new BufferedDownloadStream(metadata.ContentLength, DownloadChunkSize, (d, p, c) => DownloadByteRange(d, p, c, metadata.ETag));
        }

        private void DownloadByteRange(Stream destination, long position, int count, string eTag)
        {
            var rangeRequest = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = Key,
                ByteRange = new ByteRange(position, position + count),
                EtagToMatch = eTag
            };

            // TODO: Explore if this request could be better done async
            using (var rangeResponse = _storageClient.GetObjectAsync(rangeRequest).GetAwaiter().GetResult())
            {
                rangeResponse.ResponseStream.CopyTo(destination);
            }
        }

        /// <summary>
        /// Converts the blob ID to an Amazon S3 compatible key.
        /// </summary>
        /// <param name="id">The blob unique identifier.</param>
        /// <returns>An Amazon S3 blob key.</returns>
        public static string ConvertToKey(Uri id)
        {
            return id.AbsolutePath.TrimStart('/');
        }

    }
}
