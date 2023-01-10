using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using EPiServer.Framework.Blobs;
using Xunit;

namespace EPiServer.Amazon.Blobs
{
    // Make test class public to enable
    [Trait("Category", "Integration")]
    internal class AmazonBlobProviderIntegrationTest
    {
        private static AmazonBlobProvider CreateProvider()
        {
            var appSettings = ConfigurationManager.AppSettings;

            var bucketName = appSettings["AmazonBlobProvider.BucketName"];
            if (string.IsNullOrEmpty(bucketName))
            {
                Assert.True(false, "You must configure a bucket name for this test to run.");
            }

            var provider = new AmazonBlobProvider();
            var config = new NameValueCollection
            {
                { AmazonBlobProvider.ProfileNameKey, appSettings["AmazonProviders.ProfileName"] },
                { AmazonBlobProvider.AccessKey, appSettings["AmazonProviders.AccessKey"] },
                { AmazonBlobProvider.SecretKey, appSettings["AmazonProviders.SecretKey"] },
                { AmazonBlobProvider.RegionKey, appSettings["AmazonProviders.Region"] },
                { AmazonBlobProvider.BucketNameKey, bucketName }
            };
            provider.Initialize("default", config);
            var t = provider.InitializeAsync();
            t.Wait();

            return provider;
        }

        [Fact]
        public void Write_ShouldCreateFile()
        {
            var provider = CreateProvider();

            // If no provider is configured - Ignore test
            if (provider == null)
                return;

            var containerId = Blob.GetContainerIdentifier(Guid.NewGuid());
            var fileId = Blob.NewBlobIdentifier(containerId, ".txt");
            var content = "Content of file " + fileId;

            var mst = new MemoryStream();
            var w = new StreamWriter(mst);
            w.Write(content);
            w.Flush();
            mst.Seek(0, SeekOrigin.Begin);

            provider.GetBlob(fileId).Write(mst);

            using (var s = provider.GetBlob(fileId).OpenRead())
            {
                var data = new StreamReader(s).ReadToEnd();
                Assert.Equal(content, data);
            }

            provider.Delete(fileId);
            provider.Delete(containerId);
        }

        [Fact]
        public void OpenWrite_ShouldAllowFileToBeWritten()
        {
            var provider = CreateProvider();

            // If no provider is configured - Ignore test
            if (provider == null)
                return;

            var containerId = Blob.GetContainerIdentifier(Guid.NewGuid());
            var fileId = Blob.NewBlobIdentifier(containerId, ".txt");
            var content = "Content of file " + fileId;

            using (var s = provider.GetBlob(fileId).OpenWrite())
            using (var w = new StreamWriter(s))
            {
                w.Write(content);
                w.Flush();
            }

            using (var s = provider.GetBlob(fileId).OpenRead())
            {
                var data = new StreamReader(s).ReadToEnd();
                Assert.Equal(content, data);
            }

            provider.Delete(fileId);
            provider.Delete(containerId);
        }

        //[Fact]
        //[Trait("Category", "Integration")]
        //public void OpenWrite_WhenFileDoesntExist_ShouldCreateFile()
        //{
        //    var provider = CreateProvider();

        //    // If no provider is configured - Ignore test
        //    if (provider == null)
        //        return;

        //    var containerId = Blob.GetContainerIdentifier(Guid.NewGuid());
        //    var fileId = Blob.NewBlobIdentifier(containerId, ".txt");
        //    var content = "Content of file " + fileId;

        //    using (var s = provider.GetBlob(fileId).OpenWrite())
        //    using (var w = new StreamWriter(s))
        //    {
        //        w.Write(content);
        //        w.Flush();
        //    }

        //    using (var s = provider.GetBlob(fileId).OpenRead())
        //    {
        //        var data = new StreamReader(s).ReadToEnd();
        //        Assert.Equal(content, data);
        //    }

        //    provider.Delete(fileId);
        //    provider.Delete(containerId);
        //}

    }
}
