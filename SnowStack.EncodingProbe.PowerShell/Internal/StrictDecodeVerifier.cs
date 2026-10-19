using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 全体の厳密な検証で見つかった、最初の不正なバイト列。
    /// </summary>
    internal sealed class StrictDecodeFailure
    {
        public StrictDecodeFailure(long offset, byte[] bytes)
        {
            this.Offset = offset;
            this.Bytes = bytes;
        }

        /// <summary>不正なバイト列の位置（ファイル先頭からのバイトオフセット。BOM を含めて数える）</summary>
        public long Offset { get; }

        /// <summary>不正なバイト列（復号器が報告したもの）</summary>
        public byte[] Bytes { get; }
    }

    /// <summary>
    /// ファイル全体を厳密に復号できるかを検証する（1.3.0 仕様書 4 章）。
    /// </summary>
    /// <remarks>
    /// -DetectionLineCount で先頭 N 行から判定した文字エンコーディングを使う前に、
    /// 判定を誤っていないかを全体で確かめる。誤っていれば、処理する前に止める（文字を黙って失わない）。
    /// <br/>
    /// ファイルを固定サイズのバッファで先頭から順に読み、<see cref="Decoder"/> に与える。復号した文字は捨てる。
    /// メモリの使用量はファイルの大きさに関係なく一定である。
    /// <see cref="Decoder"/> はバッファの境目で分かれた多バイト文字を内部の状態でつなぐ。
    /// </remarks>
    internal static class StrictDecodeVerifier
    {
        /// <summary>読み込みに使うバッファの大きさ</summary>
        private const int DefaultBufferSize = 64 * 1024;

        /// <summary>不正なバイト列の先頭をさかのぼって探すために覚えておくバイト数</summary>
        private const int HistorySize = 64;

        /// <summary>BOM の判定に必要な先頭バイト数</summary>
        private const int BomHeadSize = 4;

        /// <summary>
        /// ファイルを開いて全体を検証する。不正なバイト列が無ければ null を返す。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス。存在することを前提とする。</param>
        /// <param name="codePage">判定したコードページ</param>
        /// <exception cref="ArgumentException">実行環境がコードページを提供していない場合</exception>
        /// <exception cref="NotSupportedException">実行環境がコードページを提供していない場合</exception>
        public static StrictDecodeFailure? Verify(string path, int codePage)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            // ProbedFileReader と同じ開き方にする（読み込み中の他のプロセスと共存し、先頭から順に読む）
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                4096,
                FileOptions.SequentialScan))
            {
                return Verify(stream, codePage, DefaultBufferSize);
            }
        }

        /// <summary>
        /// ストリームの全体を検証する。バッファの大きさを指定できる（テスト用）。
        /// </summary>
        /// <param name="stream">シーク可能なストリーム。先頭から読む。</param>
        /// <param name="codePage">判定したコードページ</param>
        /// <param name="bufferSize">読み込みに使うバッファの大きさ</param>
        internal static StrictDecodeFailure? Verify(Stream stream, int codePage, int bufferSize)
        {
            Encoding strict = EncodingVocabulary.BuildStrictEncoding(codePage);

            // BOM は読み飛ばしてから復号する。長さは ProbedFileReader と同じ規則で求める
            long bomLength = ProbedFileReader.GetBomLength(ReadHead(stream, BomHeadSize));

            stream.Seek(bomLength, SeekOrigin.Begin);

            Decoder decoder = strict.GetDecoder();
            var buffer = new byte[bufferSize];
            var chars = new char[strict.GetMaxCharCount(bufferSize)];
            long position = bomLength;

            while (true)
            {
                int count = ReadFull(stream, buffer);

                if (count == 0)
                {
                    break;
                }

                try
                {
                    decoder.GetChars(buffer, 0, count, chars, 0, flush: false);
                }
                catch (DecoderFallbackException)
                {
                    return Locate(stream, strict, bomLength, position, bufferSize);
                }

                position += count;
            }

            try
            {
                // 末尾で途切れた多バイト文字は、flush したときに分かる
                decoder.GetChars(buffer, 0, 0, chars, 0, flush: true);
            }
            catch (DecoderFallbackException)
            {
                return Locate(stream, strict, bomLength, position, bufferSize);
            }

            return null;
        }

        /// <summary>
        /// 最初の不正なバイト列の位置と、そのバイト列を求める。
        /// </summary>
        /// <remarks>
        /// <see cref="DecoderFallbackException.Index"/> は使わない。.NET Framework と .NET Core とで基準が異なり、
        /// PowerShell 5.1 と 7.x で違う位置を報告してしまう（1.2.0 で判明。Convert-ProbedContent と同じ理由）。
        /// <br/>
        /// <see cref="Decoder"/> の状態は複製できないので、新しい <see cref="Decoder"/> でファイルの先頭から読み直す。
        /// 失敗したバッファの直前までは、検証と同じ区切りのバッファで与える。この区間は復号に成功しているので、
        /// <see cref="Decoder"/> の状態がそのまま再現される。途中のバッファから読み直さないのは、
        /// ISO-2022 のように、エスケープシーケンスで切り替えた状態がいくらでも長く続く文字エンコーディングがあるためである。
        /// <br/>
        /// 失敗したバッファからは 1 バイトずつ与え、例外になったバイトの位置から、
        /// 例外が報告した不正なバイト列の先頭を逆にたどる（<c>ConvertProbedContentCommand.FindInvalidBytes</c> と同じ考え方）。
        /// 不正と分かるのは、不正なバイト列そのものの最後のバイトか、その直後のバイトが届いたときである。
        /// 読み直しは失敗したときだけ行うので、成功する検証の速さには影響しない。
        /// </remarks>
        /// <param name="failedBufferStart">失敗したバッファの先頭の位置。末尾の flush で失敗した場合はファイルの長さ。</param>
        private static StrictDecodeFailure Locate(
            Stream stream, Encoding strict, long bomLength, long failedBufferStart, int bufferSize)
        {
            stream.Seek(bomLength, SeekOrigin.Begin);

            Decoder decoder = strict.GetDecoder();
            var buffer = new byte[bufferSize];
            var chars = new char[Math.Max(strict.GetMaxCharCount(bufferSize), strict.GetMaxCharCount(1) + 2)];
            var history = new Queue<byte>(HistorySize);
            long position = bomLength;

            // 成功していた区間を、検証と同じ区切りで与え直す
            while (position < failedBufferStart)
            {
                int count = ReadFull(stream, buffer, (int)Math.Min(bufferSize, failedBufferStart - position));

                if (count == 0)
                {
                    break;
                }

                decoder.GetChars(buffer, 0, count, chars, 0, flush: false);
                Remember(history, buffer, count);
                position += count;
            }

            var single = new byte[1];
            long detectedAt = position;

            try
            {
                while (stream.Read(single, 0, 1) == 1)
                {
                    detectedAt = position;
                    Remember(history, single, 1);
                    position++;
                    decoder.GetChars(single, 0, 1, chars, 0, flush: false);
                }

                detectedAt = position;
                decoder.GetChars(single, 0, 0, chars, 0, flush: true);
            }
            catch (DecoderFallbackException exception)
            {
                byte[] invalidBytes = exception.BytesUnknown ?? Array.Empty<byte>();

                // history は、position の直前までに与えたバイト列の末尾である
                return new StrictDecodeFailure(
                    FindStart(history.ToArray(), position - history.Count, detectedAt, invalidBytes),
                    invalidBytes);
            }

            // 読み直しでは失敗しなかった（検証の後にファイルが書き換えられた場合など）。
            // 位置を特定できないので、失敗したバッファの先頭を報告する
            return new StrictDecodeFailure(failedBufferStart, Array.Empty<byte>());
        }

        /// <summary>
        /// 例外になった位置から、不正なバイト列の先頭をさかのぼって探す。
        /// </summary>
        /// <param name="history">最後に与えたバイト列（例外になったバイトを含む）</param>
        /// <param name="historyStart"><paramref name="history"/> の先頭のファイル上の位置</param>
        /// <param name="detectedAt">例外になったバイトの位置。flush で失敗した場合はファイルの長さ。</param>
        /// <param name="invalidBytes">例外が報告した不正なバイト列</param>
        private static long FindStart(byte[] history, long historyStart, long detectedAt, byte[] invalidBytes)
        {
            int length = invalidBytes.Length;
            long historyEnd = historyStart + history.Length;

            for (long candidate = Math.Min(detectedAt, historyEnd - length); candidate >= historyStart; candidate--)
            {
                if (candidate + length <= detectedAt + 1
                    && StartsWith(history, (int)(candidate - historyStart), invalidBytes))
                {
                    return candidate;
                }
            }

            return detectedAt;
        }

        private static bool StartsWith(byte[] content, int offset, byte[] value)
        {
            if (offset < 0 || offset + value.Length > content.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (content[offset + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void Remember(Queue<byte> history, byte[] buffer, int count)
        {
            for (int i = Math.Max(0, count - HistorySize); i < count; i++)
            {
                if (history.Count == HistorySize)
                {
                    history.Dequeue();
                }

                history.Enqueue(buffer[i]);
            }
        }

        /// <summary>
        /// バッファがいっぱいになるか、末尾に達するまで読む
        /// </summary>
        private static int ReadFull(Stream stream, byte[] buffer) => ReadFull(stream, buffer, buffer.Length);

        private static int ReadFull(Stream stream, byte[] buffer, int size)
        {
            int read = 0;

            while (read < size)
            {
                int count = stream.Read(buffer, read, size - read);

                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            return read;
        }

        private static byte[] ReadHead(Stream stream, int size)
        {
            stream.Seek(0, SeekOrigin.Begin);

            var buffer = new byte[size];
            int read = ReadFull(stream, buffer);

            if (read < size)
            {
                Array.Resize(ref buffer, read);
            }

            return buffer;
        }
    }
}
