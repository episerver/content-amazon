using System;
using System.IO;
using Microsoft.Extensions.FileProviders;

namespace EPiServer.Amazon.Blobs
{
    /// <summary>
    /// Exposes an <see cref="AmazonBlob"/> as an <see cref="IFileInfo"/> so that blobs can be served
    /// through the ASP.NET Core file providers.
    /// </summary>
    internal class AmazonBlobFileInfo : IFileInfo
    {
        private readonly string _key;
        private Stream _content;

        /// <summary>
        /// Initializes a new instance representing a blob that does not exist.
        /// </summary>
        /// <param name="key">The Amazon S3 key of the blob.</param>
        /// <param name="lastModified">The time the blob was last modified.</param>
        public AmazonBlobFileInfo(string key, DateTimeOffset lastModified)
        {
            _key = key;
            LastModified = lastModified;
            Length = -1;
        }

        /// <summary>
        /// Initializes a new instance representing an existing blob.
        /// </summary>
        /// <param name="key">The Amazon S3 key of the blob.</param>
        /// <param name="lastModified">The time the blob was last modified.</param>
        /// <param name="length">The length of the blob in bytes.</param>
        /// <param name="content">
        /// An open stream over the blob content, or <c>null</c> if the content was not requested.
        /// Ownership of the stream passes to the caller of <see cref="CreateReadStream"/>.
        /// </param>
        public AmazonBlobFileInfo(string key, DateTimeOffset lastModified, long length, Stream content)
        {
            _key = key;
            _content = content;
            LastModified = lastModified;
            Length = length;
            Exists = true;
        }

        /// <inheritdoc />
        public bool Exists { get; }

        /// <inheritdoc />
        public long Length { get; }

        /// <inheritdoc />
        /// <remarks>Always <c>null</c>, since a blob is not backed by a file on disk.</remarks>
        public string PhysicalPath => null;

        /// <inheritdoc />
        public string Name => _key == null ? null : _key.Substring(_key.LastIndexOf('/') + 1);

        /// <inheritdoc />
        public DateTimeOffset LastModified { get; }

        /// <inheritdoc />
        public bool IsDirectory => false;

        /// <inheritdoc />
        /// <exception cref="System.InvalidOperationException">
        /// The instance was not created with an open stream, or the stream has already been handed out.
        /// </exception>
        public Stream CreateReadStream()
        {
            var content = _content;
            if (content == null)
            {
                throw new InvalidOperationException("No content stream is available for this blob. Request one by passing openReadStream when creating the file info.");
            }

            // The stream is opened once and cannot be read twice.
            _content = null;
            return content;
        }
    }
}
