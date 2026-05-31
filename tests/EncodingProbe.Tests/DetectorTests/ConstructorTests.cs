using System.IO;
using EncodingProbe.Tests.Helpers;
using SnowStack.EncodingProbe;
using Xunit;

namespace EncodingProbe.Tests.DetectorTests
{
    /// <summary>
    /// EncodingDetector(byte[] buffer) コンストラクタのテスト
    /// </summary>
    public class ByteArrayConstructorTests
    {
        [Fact]
        public void Constructor_WithValidBuffer_DoesNotThrow()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_utf8.txt");
            var detector = new EncodingDetector(buffer);
            Assert.NotNull(detector);
        }

        [Fact]
        public void Constructor_WithNullBuffer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new EncodingDetector((byte[])null));
        }

        [Fact]
        public void Constructor_SetsBufferSize()
        {
            var buffer = TestDataHelper.ReadBytes("Japanese", "sample_utf8.txt");
            var detector = new EncodingDetector(buffer);
            Assert.Equal(buffer.Length, detector.BufferSize);
        }
    }

    /// <summary>
    /// EncodingDetector(Stream stream) コンストラクタのテスト
    /// </summary>
    public class StreamConstructorTests
    {
        [Fact]
        public void Constructor_WithValidStream_DoesNotThrow()
        {
            using var stream = TestDataHelper.OpenStream("Japanese", "sample_utf8.txt");
            var detector = new EncodingDetector(stream);
            Assert.NotNull(detector);
        }

        [Fact]
        public void Constructor_WithNullStream_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new EncodingDetector((Stream)null));
        }

        [Fact]
        public void Constructor_WithNonReadableStream_ThrowsArgumentException()
        {
            // CanRead = false を返すカスタムストリームで検証（実ファイルに依存しない）
            using var stream = new NonReadableStream();
            Assert.Throws<ArgumentException>(() => new EncodingDetector(stream));
        }

        /// <summary>CanRead が常に false を返すテスト用ストリーム</summary>
        private sealed class NonReadableStream : Stream
        {
            public override bool CanRead  => false;
            public override bool CanSeek  => false;
            public override bool CanWrite => true;
            public override long Length   => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int  Read(byte[] buffer, int offset, int count)  => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)        => throw new NotSupportedException();
            public override void SetLength(long value)                       => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { }
        }

        [Fact]
        public void Constructor_FromStream_SetsBufferSizeSameAsByteArray()
        {
            var expected = TestDataHelper.ReadBytes("Japanese", "sample_utf8.txt");
            using var stream = TestDataHelper.OpenStream("Japanese", "sample_utf8.txt");

            var detectorFromBytes = new EncodingDetector(expected);
            var detectorFromStream = new EncodingDetector(stream);

            Assert.Equal(detectorFromBytes.BufferSize, detectorFromStream.BufferSize);
        }
    }

    /// <summary>
    /// EncodingDetector(string filePath) コンストラクタのテスト
    /// </summary>
    public class FilePathConstructorTests
    {
        [Fact]
        public void Constructor_WithValidPath_DoesNotThrow()
        {
            var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
            var detector = new EncodingDetector(path);
            Assert.NotNull(detector);
        }

        [Fact]
        public void Constructor_WithNullPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new EncodingDetector((string)null));
        }

        [Fact]
        public void Constructor_WithEmptyPath_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new EncodingDetector(string.Empty));
        }

        [Fact]
        public void Constructor_WithNonExistentPath_ThrowsIOException()
        {
            // 存在しないディレクトリの場合は DirectoryNotFoundException、
            // ディレクトリは存在するがファイルがない場合は FileNotFoundException が発生する。
            // どちらも IOException のサブクラスなので IOException で検証する。
            Assert.Throws<DirectoryNotFoundException>(() =>
                new EncodingDetector(@"C:\nonexistent\file_that_does_not_exist.txt"));
        }

        [Fact]
        public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException()
        {
            // 既存ディレクトリ配下の存在しないファイルは FileNotFoundException
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");
            Assert.Throws<FileNotFoundException>(() => new EncodingDetector(path));
        }

        [Fact]
        public void Constructor_FromFilePath_SetsBufferSizeSameAsByteArray()
        {
            var path = TestDataHelper.GetPath("Japanese", "sample_utf8.txt");
            var expected = File.ReadAllBytes(path);

            var detectorFromPath = new EncodingDetector(path);
            var detectorFromBytes = new EncodingDetector(expected);

            Assert.Equal(detectorFromBytes.BufferSize, detectorFromPath.BufferSize);
        }
    }
}
