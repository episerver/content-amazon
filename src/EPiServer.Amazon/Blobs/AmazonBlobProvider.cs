using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using EPiServer.Amazon.Events;
using EPiServer.Framework.Blobs;
using Microsoft.Extensions.Logging;

namespace EPiServer.Amazon.Blobs
{
    /// <summary>
    /// A <see cref="BlobProvider"/> for the Amazon S3 services.
    /// </summary>
    /// <example>
    /// The following shows an example on how the <see cref="AmazonBlobProvider"/> can be configured.
    /// <code>
    /// {
    ///   "EPiServer" : {
    ///     "AmazonBlob" : {
    ///       "bucket" : "root-bucket-name",
    ///       "downloadChunkSize" : "2097152",
    ///       "accessKey" : "[Your AWS Access Key]",
    ///       "secretKey" : "[Your AWS Secret Access Key]",
    ///       "region" : "ap-southeast-2"
    ///     }
    ///   }
    /// }
    /// </code>
    /// Note that all settings must be the same on all sites that should be communicating using remote events and that the
    /// Topic should be unique between different environments.
    /// </example>

    public class AmazonBlobProvider : BlobProvider, IDisposable
    {
        private const string BucketNamePattern = @"^[a-z0-9][a-z0-9\-]{1,61}[a-z0-9]$";
        private readonly ILogger _logger;

        private IAmazonS3 _storageClient;
        private readonly AmazonBlobClientOptions _amazonBlobClientOptions;

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="amazonBlobClientOptions">The amazon blob options</param>
        /// <param name="logger">The logger</param>
        public AmazonBlobProvider(AmazonBlobClientOptions amazonBlobClientOptions,
            ILogger<AmazonBlobProvider> logger)
        {
            _amazonBlobClientOptions = amazonBlobClientOptions;
            _logger = logger;
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            // Profile cannot be used with AccessKey/SecretKey
            if (!string.IsNullOrEmpty(_amazonBlobClientOptions.ProfileName) && _amazonBlobClientOptions.HasAccessKeyCredentials())
            {
                throw new ArgumentException("Profile Name cannot be combined with AccessKey/SecretKey. Use one or the other all use th default AWSSDK.NET credentials configuration.");
            }

            // Either none or both AccessKey & SecretKey must be set
            if (string.IsNullOrEmpty(_amazonBlobClientOptions.AccessKey) ^ string.IsNullOrEmpty(_amazonBlobClientOptions.SecretKey))
            {
                throw new ArgumentException("AccessKey must be accompanied by a SecretKey. Alternatively specify Profile Name or use the default AWSSDK.NET credentials configuration.");
            }

            if (string.IsNullOrEmpty(_amazonBlobClientOptions.ProfileName) && !_amazonBlobClientOptions.HasAccessKeyCredentials())
            {
                _logger.LogDebug("Neither profile nor access/secret keys were specified in the provider configuration. See AWS SDK for .NET documentation for other alternative configuration options.");
            }

            // Bucket name
            if (string.IsNullOrEmpty(_amazonBlobClientOptions.BucketName))
            {
                throw new ArgumentException("The name of the bucket must be provided using the key 'BucketName'.");
            }
            // 3-63 lowercase characters, numbers and dashes (-) are allowed
            if (!Regex.IsMatch(_amazonBlobClientOptions.BucketName, BucketNamePattern))
            {
                throw new ArgumentException("The provided bucket name does not conform with AWS requirements. Bucket names must be at least 3 and no more than 63 characters long and only lower case characters, numbers and dashes (-) are allowed. In addition the name cannot begin or end with a dash.");
            }

            // RegionEndpoint
            RegionEndpoint CheckSystemName()
            {
                try
                {
                    return RegionEndpoint.GetBySystemName(_amazonBlobClientOptions.Region);
                }
                catch (Exception)
                {
                    return default;
                }
            }

            var region = !string.IsNullOrEmpty(_amazonBlobClientOptions.Region) ? CheckSystemName() : default;
            if (region == default)
            {
                throw new ArgumentException("No region was specified in the provider configuration.");
            }

            return Task.Factory.StartNew(() =>
                {
                    var credentials = CreateCredentials();
                    _storageClient = CreateStorageServiceClient(credentials);
                    EnsureBucketExists();
                });
        }

        /// <inheritdoc />
        public override Blob CreateBlob(Uri id, string extension)
        {
            return GetBlob(Blob.NewBlobIdentifier(id, extension));
        }

        /// <inheritdoc />
        public override Blob GetBlob(Uri id)
        {
            var blob = new AmazonBlob(_storageClient, _amazonBlobClientOptions.BucketName, id);
            if (_amazonBlobClientOptions.DownloadChunkSize.HasValue)
            {
                blob.DownloadChunkSize = _amazonBlobClientOptions.DownloadChunkSize.Value;
            }
            return blob;
        }

        /// <inheritdoc />
        public override void Delete(Uri id)
        {
            Blob.ValidateIdentifier(id, null);

            _storageClient.DeleteObjectAsync(_amazonBlobClientOptions.BucketName, AmazonBlob.ConvertToKey(id)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates the Amazon SNS client that is used by this instance. 
        /// Override this if you need to make any specific configuration settings for the service connection.
        /// </summary>
        /// <param name="credentials">The credentials to use when connecting to the service.</param>
        /// <returns>A new <see cref="IAmazonS3"/> instance.</returns>
        protected virtual IAmazonS3 CreateStorageServiceClient(AWSCredentials credentials)
        {
            // Use this construct to ensure we get the right defaults from the AWSClientFactory
            var region = !string.IsNullOrEmpty(_amazonBlobClientOptions.Region) ? RegionEndpoint.GetBySystemName(_amazonBlobClientOptions.Region) : default;
            if (credentials == null && region == null)
            {
                return new AmazonS3Client();
            }
            if (region == null)
            {
                return new AmazonS3Client(credentials);
            }
            if (credentials == null)
            {
                return new AmazonS3Client(region);
            }

            return new AmazonS3Client(credentials, region);
        }

        /// <summary>
        /// Creates the AWS credentials to use when connecting to the S3 services.
        /// Override this if you need to use a different type of credential configuration than AccessKey/SecretAccessKey
        /// </summary>
        /// <returns>An instance of any type of <see cref="AWSCredentials"/> or null if the default credentials logic should be used.</returns>
        protected virtual AWSCredentials CreateCredentials()
        {
            if (!string.IsNullOrEmpty(_amazonBlobClientOptions.ProfileName))
            {
                var chain = new CredentialProfileStoreChain();
                if (chain.TryGetAWSCredentials(_amazonBlobClientOptions.ProfileName, out var profileCredentials))
                {
                    return profileCredentials;
                }
            }
            if (_amazonBlobClientOptions.HasAccessKeyCredentials())
            {
                return new BasicAWSCredentials(_amazonBlobClientOptions.AccessKey, _amazonBlobClientOptions.SecretKey);
            }
            return null;
        }

        /// <summary>
        /// Ensures that a bucket with the name given in the configuration exists.
        /// If no bucket exists, one is created.
        /// </summary>
        protected virtual async void EnsureBucketExists()
        {
            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_storageClient, _amazonBlobClientOptions.BucketName))
            {
                await _storageClient.PutBucketAsync(new PutBucketRequest()
                {
                    BucketName = _amazonBlobClientOptions.BucketName,
                    UseClientRegion = true
                });
            }
        }

        /// <inheritdoc />
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            if (_storageClient != null)
            {
                _storageClient.Dispose();
                _storageClient = null;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
