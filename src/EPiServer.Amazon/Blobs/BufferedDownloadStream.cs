using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace EPiServer.Amazon.Blobs
{
    /// <summary>
    /// Stream with a local memory buffer that is filled using a download delegate.
    /// </summary>
    internal class BufferedDownloadStream : Stream
    {
        public delegate void Downloader(Stream destination, long position, int count);

        private readonly long _length;
        private readonly Downloader _downloader;
        private MemoryBuffer _buffer = new MemoryBuffer();
        private long _position;
        private int _downloadChunkSize;

        public BufferedDownloadStream(long length, int downloadChunkSize, Downloader downloader)
        {
            _length = length;
            _downloader = downloader;
            _downloadChunkSize = downloadChunkSize;
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override long Length
        {
            get { return _length; }
        }

        public override long Position
        {
            get { return _position; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        private MemoryBuffer Buffer
        {
            get
            {
                if (_buffer == null)
                {
                    throw new ObjectDisposedException(null);
                }
                return _buffer;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            #region Argument Validation
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset", "offset cannot be less than zero or longer than the buffer length");
            if (count < 0 || (offset + count) > buffer.Length)
                throw new ArgumentOutOfRangeException("count", "count cannot be less than zero or longer than the buffer length with the offset included");
            #endregion

            if (count == 0 || _position == _length)
            {
                return 0;
            }

            var length = ReadFromBuffer(buffer, offset, count);

            _position += length;

            return length;
        }

        private int ReadFromBuffer(byte[] buffer, int offset, int count)
        {
            if (!Buffer.Empty && Buffer.AtEnd)
            {
                Buffer.Clear();
            }

            if (Buffer.Empty)
            {
                Buffer.Origin = _position;

                // Never request less than limit...
                var requestLength = Math.Max(count, _downloadChunkSize);
                // ...unless longer that the total bytes left...
                requestLength = Math.Min(requestLength, (int)(Length - _position));
                // ...but don't request more than the maximum limit!
                requestLength = Math.Min(requestLength, AmazonBlob.MaximumDownloadChunkSize);

                Buffer.Fill(s => _downloader(s, _position, requestLength));

                if (Buffer.Empty)
                {
                    return 0;
                }
            }

            return Buffer.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var newPosition = CalculateNewPosition(offset, origin);

            if (newPosition < 0L || newPosition > _length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            if (!Buffer.Empty)
            {
                // Move buffer position accordingly
                Buffer.SetPosition(newPosition - Buffer.Origin);
            }

            return _position = newPosition;
        }

        private long CalculateNewPosition(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                return offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                return _position + offset;
            }
            else if (origin == SeekOrigin.End)
            {
                return _length + offset;
            }

            throw new ArgumentOutOfRangeException("origin", "origin not a valid enum value.");
        }

        protected override void Dispose(bool disposing)
        {
            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
            base.Dispose(disposing);
        }

        #region Write members (unsupported)

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        #endregion

        private class MemoryBuffer : IDisposable
        {
            private MemoryStream _memoryBuffer = new MemoryStream();

            public long Origin { get; set; }

            public bool Empty
            {
                get { return _memoryBuffer.Length == 0; }
            }

            public bool AtEnd
            {
                get { return _memoryBuffer.Position == _memoryBuffer.Length; }
            }

            public void Clear()
            {
                _memoryBuffer.SetLength(0);
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                return _memoryBuffer.Read(buffer, offset, count);
            }

            public void Fill(Action<Stream> writer)
            {
                Clear();
                writer(_memoryBuffer);
                _memoryBuffer.Position = 0;
            }

            public void SetPosition(long position)
            {
                // If new position is outside of the current buffer, clear buffer
                if (position < 0 || position > _memoryBuffer.Length)
                {
                    Clear();
                }
                else
                {
                    _memoryBuffer.Position = position;
                }
            }

            // TODO: The Trim method could be used to opimize memory usage, but it would have a performance impact and if made async would require a lock on memorybuffer.
            //public void Trim()
            //{
            //    // If length or position is zero there are no already read values
            //    if (_memoryBuffer.Length == 0 || _memoryBuffer.Position == 0)
            //    {
            //        return;
            //    }

            //    if (_memoryBuffer.Position == _memoryBuffer.Length)
            //    {
            //        _memoryBuffer.SetLength(0);
            //        return;
            //    }

            //    var internalBuffer = _memoryBuffer.GetBuffer();
            //    var trimLength = (int)_memoryBuffer.Position;
            //    var targetLength = (int)_memoryBuffer.Length - trimLength;
            //    System.Buffer.BlockCopy(internalBuffer, trimLength, internalBuffer, 0, targetLength);
            //    _memoryBuffer.SetLength(targetLength);
            //}

            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "disposing", Justification = "Matches best practice pattern.")]
            protected void Dispose(bool disposing)
            {
                if (_memoryBuffer != null)
                {
                    _memoryBuffer.Dispose();
                    _memoryBuffer = null;
                }
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }
        }
    }
}
