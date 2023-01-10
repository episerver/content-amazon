using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EPiServer.Amazon.Blobs
{
    public class BufferedDownloadStreamTest
    {
        private static readonly BufferedDownloadStream.Downloader NullDownloader = (d, o, c) => d.Write(new byte[c], 0, c);
        private const int DefaultDownloadChunkSize = 32;

        [Fact]
        public void Length_ShouldReturnValueSetThroughConstructor()
        {
            const int expected = 64;
            var subject = CreateSubject(expected);

            Assert.Equal(expected, subject.Length);
        }

        [Fact]
        public void Seek_FromBeginning_ShouldReturnSameValueAsNewPosition()
        {
            const long seekOffset = 512L;
            var subject = CreateSubject(seekOffset * 2);

            var newPosition = subject.Seek(seekOffset, SeekOrigin.Begin);

            Assert.Equal(seekOffset, newPosition);
        }

        [Fact]
        public void Seek_FromCurrentPosition_ShouldReturnOldPositionPlusOffset()
        {
            const long initialPosition = 16, seekOffset = 32;
            var subject = CreateSubject((initialPosition + seekOffset) * 2);
            subject.Position = initialPosition;
            var newPosition = subject.Seek(seekOffset, SeekOrigin.Current);

            Assert.Equal(initialPosition + seekOffset, newPosition);
        }

        [Fact]
        public void Seek_FromEnd_ShouldReturnLengthPlusOffset()
        {
            const long length = 64, seekOffset = -16;
            var subject = CreateSubject(length);
            var newPosition = subject.Seek(seekOffset, SeekOrigin.End);

            Assert.Equal(length + seekOffset, newPosition);
        }

        [Fact]
        public void Seek_FromEndWithPositiveValue_ShouldThrowException()
        {
            var subject = CreateSubject(16);
            Assert.Throws<ArgumentOutOfRangeException>(() => subject.Seek(8, SeekOrigin.End));
        }

        [Fact]
        public void Seek_FromBeginningWithNegativeValue_ShouldThrowException()
        {
            var subject = CreateSubject(16);
            Assert.Throws<ArgumentOutOfRangeException>(() => subject.Seek(-8, SeekOrigin.Begin));
        }

        [Fact]
        public void Seek_FromCurrentPositionWithValueLargerThanRemaningBytes_ShouldThrowException()
        {
            const long initialPosition = 16, seekOffset = 16;
            var subject = CreateSubject(initialPosition + seekOffset / 2);
            subject.Position = initialPosition;

            Assert.Throws<ArgumentOutOfRangeException>(() => subject.Seek(seekOffset, SeekOrigin.Current));
        }

        [Fact]
        public void Read_WhenPositionIsAtLength_ShouldReturnZero()
        {
            const int length = 64;
            var subject = CreateSubject(length);

            subject.Seek(length, SeekOrigin.Begin);

            var buffer = new byte[8];
            var result = subject.Read(buffer, 0, buffer.Length);

            Assert.Equal(0, result);
        }

        [Fact]
        public void Read_ShouldUpdatePosition()
        {
            const int length = 32;
            var subject = CreateSubject(length, FakeDownloaderFactory.Downloader());

            var buffer = new byte[length / 2];
            var readBytes = subject.Read(buffer, 0, buffer.Length);

            Assert.Equal(readBytes, subject.Position);
        }

        [Fact]
        public void Read_WhenCountIsLargerThanLength_PositionShouldEqualLength()
        {
            const int length = 32;
            var subject = CreateSubject(length, FakeDownloaderFactory.Downloader(length));

            var buffer = new byte[length * 2];
            subject.Read(buffer, 0, buffer.Length);

            Assert.Equal(length, subject.Position);
        }

        [Fact]
        public void Read_WhenBufferIsEmpty_ShouldCallReaderDelegate()
        {
            const int length = 32;
            var downloader = new FakeDownloaderFactory();
            var subject = CreateSubject(length, downloader.DownloadToStream);

            var buffer = new byte[length / 2];
            subject.Read(buffer, 0, buffer.Length);

            Assert.Single(downloader.Calls);
        }

        [Fact]
        public void Read_WhenReadingTheSameDataTwice_ShouldOnlyCallReaderDelegateOnce()
        {
            const int length = 32;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream);

            var buffer = new byte[length];
            subject.Read(buffer, 0, buffer.Length);
            subject.Position = 0;
            buffer = new byte[length];
            subject.Read(buffer, 0, buffer.Length);

            Assert.Single(downloader.Calls);
        }

        [Fact]
        public void Read_WhenBufferContainsRequestedData_ShouldNotCallReaderDelegate()
        {
            const int length = 64, chunkSize = 32;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            var buffer = new byte[length];
            subject.Read(buffer, 0, chunkSize / 2);
            subject.Read(buffer, 0, chunkSize / 2);

            Assert.Single(downloader.Calls);
        }

        [Fact]
        public void Read_WhenBufferContainsSomeData_ShouldReturnThisDataFirst()
        {
            const int length = 64, chunkSize = 32, readCount = 24;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            var buffer = new byte[length];
            subject.Read(buffer, 0, readCount);
            var result = subject.Read(buffer, 0, readCount);

            Assert.Equal(chunkSize - readCount, result);
        }

        [Fact]
        public void Read_WhenBufferContainsSomeData_ShouldNotCallReaderDelegate()
        {
            const int length = 64, bufferSize = 32;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, bufferSize);

            var buffer = new byte[length];
            subject.Read(buffer, 0, 24);
            subject.Read(buffer, 0, 24);

            Assert.Single(downloader.Calls);
        }

        [Fact]
        public void Read_WhenBufferIsEmpty_ShouldCallReaderDelegateAgain()
        {
            const int length = 128, chunkSize = 32;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            var buffer = new byte[length];
            subject.Read(buffer, 0, chunkSize / 2);
            subject.Read(buffer, 0, chunkSize / 2);
            subject.Read(buffer, 0, chunkSize / 2);

            Assert.Equal(2, downloader.Calls.Count);
        }

        [Fact]
        public void Read_WhenBufferNeedsToBeFilledMultipleTimes_ResultShouldAddUpToLength()
        {
            const int length = 1024, chunkSize = 64;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            var buffer = new byte[length];
            int readLength;
            int totalReadLength = 0;
            do
            {
                readLength = subject.Read(buffer, 0, chunkSize);
                totalReadLength += readLength;
            } while (readLength > 0);

            Assert.Equal(length, totalReadLength);
        }

        [Fact]
        public void Read_WhenLengthIsLessThanChunkSize_ShouldCallReaderWithLength()
        {
            const int length = 32;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, length + 16);

            var buffer = new byte[16];
            subject.Read(buffer, 0, buffer.Length);

            var requestLength = downloader.Calls.First().Item2;
            Assert.Equal(length, requestLength);
        }

        [Fact]
        public void Read_WhenCountIsLessThanChunkSize_ShouldCallReaderWithChunkSize()
        {
            const int length = 32, chunkSize = 16;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            var buffer = new byte[length];
            subject.Read(buffer, 0, chunkSize - 1);

            var requestLength = downloader.Calls.First().Item2;
            Assert.Equal(chunkSize, requestLength);
        }

        [Fact]
        public void Read_WhenCountExceedsTheMaximumChunkSize_ShouldCallReaderWithMaximumChunkSize()
        {
            int length = AmazonBlob.MaximumDownloadChunkSize + 128;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream);

            var buffer = new byte[length];
            subject.Read(buffer, 0, length);

            var requestLength = downloader.Calls.First().Item2;
            Assert.Equal(AmazonBlob.MaximumDownloadChunkSize, requestLength);
        }

        [Fact]
        public void Read_WhenCalledWithOffset_ShouldRequestDataAtCurrentPosition()
        {
            const int startPos = 32, offset = 16;
            var downloader = new FakeDownloaderFactory();
            var subject = CreateSubject(startPos * 2, downloader.DownloadToStream);
            subject.Position = startPos;

            var buffer = new byte[32];
            subject.Read(buffer, offset, buffer.Length - offset);

            var calledOffset = downloader.Calls.First().Item1;
            Assert.Equal(startPos, calledOffset);
        }

        [Fact]
        public void Read_WhenReaderReturnsFullBuffer_ShouldReturnLengthOfRequest()
        {
            const int length = 16, requestLength = 8;
            var subject = CreateSubject(length, FakeDownloaderFactory.Downloader());
            var buffer = new byte[length];
            var result = subject.Read(buffer, 0, requestLength);

            Assert.Equal(requestLength, result);
        }

        [Fact]
        public void Read_WhenReaderReturnsShorterThanRequestedBuffer_ShouldReturnBufferLength()
        {
            const int length = 16, readerLength = 2;
            var downloader = new FakeDownloaderFactory { MaxReturnedLength = readerLength };
            var subject = CreateSubject(length, downloader.DownloadToStream);
            var buffer = new byte[length];
            var result = subject.Read(buffer, 0, buffer.Length);

            Assert.Equal(readerLength, result);
        }

        [Fact]
        public void Read_ShouldCopyReaderResultToBuffer()
        {
            var downloader = new FakeDownloaderFactory();
            var subject = CreateSubject(16, downloader.DownloadToStream);
            var buffer = new byte[8];
            subject.Read(buffer, 0, 8);

            var expected = downloader.GenerateByteArray(0, 8);
            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Read_WhenCalledWithOffset_ShouldCopyCorrectReaderResultToBuffer()
        {
            const int length = 16, offset = 4, count = 4;
            var downloader = new FakeDownloaderFactory();
            var subject = CreateSubject(length, downloader.DownloadToStream);

            var buffer = new byte[offset + count];
            subject.Read(buffer, offset, count);

            var expected = downloader.GenerateByteArray(0, count, offset);
            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Read_WhenCalledWithOffsetAfterSeek_ShouldCopyCorrectReaderResultToBuffer()
        {
            const int length = 64, initialPosition = 16, offset = 4, count = 4;
            var downloader = new FakeDownloaderFactory();
            var subject = CreateSubject(length, downloader.DownloadToStream);

            subject.Seek(initialPosition, SeekOrigin.Begin);
            var buffer = new byte[offset + count];
            subject.Read(buffer, offset, count);

            var expected = downloader.GenerateByteArray(initialPosition, count, offset);
            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Read_WhenCalledWithFilledBufferAndOffset_ShouldCopyCorrectReaderResultToBuffer()
        {
            const int length = 64, chunkSize = 32, firstRead = 16, offset = 4, count = 4;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            // Fill buffer with 32 bytes and read byte 0-16
            var buffer = new byte[firstRead];
            subject.Read(buffer, 0, firstRead);

            // Read byte 17-20
            buffer = new byte[offset + count];
            subject.Read(buffer, offset, count);

            var expected = downloader.GenerateByteArray(firstRead, count, offset);
            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Read_WhenCalledWithPreviouslyFilledBufferAndOffset_ShouldCopyCorrectReaderResultToBuffer()
        {
            const int length = 64, chunkSize = 32, offset = 4, count = 4;
            var downloader = new FakeDownloaderFactory { ContentLength = length };
            var subject = CreateSubject(length, downloader.DownloadToStream, chunkSize);

            // Fill buffer with 32 bytes and read all
            var buffer = new byte[chunkSize];
            subject.Read(buffer, 0, chunkSize);

            // Read byte 33-36
            buffer = new byte[offset + count];
            subject.Read(buffer, offset, count);

            var expected = downloader.GenerateByteArray(chunkSize, count, offset);
            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Read_WhenReaderReturnsShorterThanRequestedBuffer_ShouldCopyReaderResultToBuffer()
        {
            const int readerLength = 2, count = 4;
            var downloader = new FakeDownloaderFactory { MaxReturnedLength = readerLength };
            var subject = CreateSubject(16, downloader.DownloadToStream);
            var buffer = new byte[count];

            subject.Read(buffer, 0, count);

            var expected = new byte[] { 1, 2, 0, 0 };

            Assert.Equal(expected, buffer);
        }

        [Fact]
        public void Dispose_WhenTryingToUseAfterCall_ShouldThrowException()
        {
            const int length = 16;
            var subject = CreateSubject(length);

            subject.Dispose();

            var buffer = new byte[length];
            Assert.Throws<ObjectDisposedException>(() => subject.Read(buffer, 0, buffer.Length));
        }

        [Fact]
        public void Flush_ShouldThrowException()
        {
            var subject = CreateSubject(16);
            Assert.Throws<NotSupportedException>(() => subject.Flush());
        }

        [Fact]
        public void SetLength_ShouldThrowException()
        {
            var subject = CreateSubject(16);
            Assert.Throws<NotSupportedException>(() => subject.SetLength(24));
        }

        [Fact]
        public void Write_ShouldThrowException()
        {
            var subject = CreateSubject(16);
            Assert.Throws<NotSupportedException>(() => subject.Write(new byte[0], 0, 0));
        }

        [Fact]
        public void CanWrite_ShouldReturnFalse()
        {
            Assert.False(CreateSubject(1).CanWrite);
        }

        [Fact]
        public void CanSeek_ShouldReturnTrue()
        {
            Assert.True(CreateSubject(1).CanSeek);
        }

        [Fact]
        public void CanRead_ShouldReturnTrue()
        {
            Assert.True(CreateSubject(1).CanRead);
        }

        private static BufferedDownloadStream CreateSubject(long length, BufferedDownloadStream.Downloader downloader = null, int chunkSize = DefaultDownloadChunkSize)
        {
            return new BufferedDownloadStream(length, chunkSize, downloader ?? NullDownloader);
        }

        private class FakeDownloaderFactory
        {
            public FakeDownloaderFactory()
            {
                Calls = new List<Tuple<long, int>>();
            }

            public int? ContentLength { get; set; }

            public int? MaxReturnedLength { get; set; }

            public List<Tuple<long, int>> Calls { get; private set; }

            public void DownloadToStream(Stream destination, long position, int count)
            {
                Calls.Add(Tuple.Create(position, count));
                var data = GenerateByteArray(position, count);

                destination.Write(data, 0, data.Length);
            }

            public byte[] GenerateByteArray(long startValue, int count, int offset = 0)
            {
                var length = Math.Min(count, MaxReturnedLength.GetValueOrDefault(ContentLength.GetValueOrDefault(int.MaxValue)));
                length += offset;

                var data = new byte[length];

                // Fill array with values
                int n = 1;
                for (int i = offset; i < length; i++)
                {
                    data[i] = (byte)(startValue + n++);
                }

                return data;
            }

            public static BufferedDownloadStream.Downloader Downloader(int? contentLength = null, int? maxReturnedLength = null)
            {
                return new FakeDownloaderFactory { ContentLength = contentLength, MaxReturnedLength = maxReturnedLength }.DownloadToStream;
            }
        }
    }
}
