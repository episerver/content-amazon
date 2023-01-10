using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EPiServer.Amazon.Blobs
{
    public class AmazonBlobProviderTest
    {
        private readonly AmazonBlobClientOptions _amazonBlobClientOptions;

        public AmazonBlobProviderTest()
        {
            _amazonBlobClientOptions = new AmazonBlobClientOptions
            {
                BucketName = "bucket-name",
                Region = "eu-west-1"
            };
        }

        [Fact]
        public async Task Initialize_WhenBothProfileNameAndAccessKeyIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.ProfileName = "Some profile";
            _amazonBlobClientOptions.AccessKey = Guid.NewGuid().ToString("n");
            _amazonBlobClientOptions.SecretKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenAccessKeyButNoSecretKeyIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.AccessKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenSecretKeyButNoAccessKeyIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.SecretKey = Guid.NewGuid().ToString("n");
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenBucketNameWithUnderscoreIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.BucketName = "bucket_name_cant_use_underscore";
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenBucketNameWithCapitalsIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.BucketName = "Bucket-name-cant-use-CAPITAL-letters";
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }

        [Fact]
        public async Task Initialize_WhenNoBucketNameIsProvided_ShouldThrowException()
        {
            _amazonBlobClientOptions.BucketName = "";
            var provider = new AmazonBlobProvider(_amazonBlobClientOptions, new NullLogger<AmazonBlobProvider>());
            await Assert.ThrowsAsync<ArgumentException>(() => provider.InitializeAsync());
        }
    }
}
