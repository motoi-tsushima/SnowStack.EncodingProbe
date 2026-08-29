using System;
using System.IO;
using System.Text;
using SnowStack.EncodingProbe;  // クラスライブラリのnamespace

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 文字エンコーディングを判定しながらテキストファイルを読み込む。
    /// </summary>
    /// <remarks>
    /// 1本の FileStream で判定と復号の両方を行う。判定用と読み込み用でファイルを2回開くことはしない。
    /// 判定はファイル全体を対象とし、判定後に Seek(0) してから復号する。
    /// 判定に使ったバイト列は返却するオブジェクトが保持しないため、復号の間は解放されている。
    /// <br/>
    /// BOM は、指定された語彙にかかわらず常に読み飛ばす（原則A）。
    /// StreamReader に detectEncodingFromByteOrderMarks: false を渡すと BOM が
    /// U+FEFF として1行目の先頭に混入するため、ストリームの位置を BOM の後ろに
    /// 進めたうえで StreamReader を生成する。
    /// </remarks>
    internal sealed class ProbedFileReader : IDisposable
    {
        /// <summary>StreamReader に与えるバッファサイズ</summary>
        private const int ReadBufferSize = 4096;

        /// <summary>BOM の判定に必要な先頭バイト数</summary>
        private const int BomHeadSize = 4;

        private readonly FileStream _stream;
        private readonly StreamReader _reader;

        private ProbedFileReader(FileStream stream, StreamReader reader, Encoding encoding, bool hadBom)
        {
            this._stream = stream;
            this._reader = reader;
            this.Encoding = encoding;
            this.HadBom = hadBom;
        }

        /// <summary>復号に使用している文字エンコーディング</summary>
        public Encoding Encoding { get; }

        /// <summary>ファイル先頭に BOM があったかどうか</summary>
        public bool HadBom { get; }

        /// <summary>
        /// ファイルを開き、必要なら文字エンコーディングを判定する。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス</param>
        /// <param name="spec">-Encoding の指定。Auto の場合は判定する。</param>
        /// <param name="detectorOptions">
        /// -Culture / -Strategy から組み立てた判定オプション。未指定の場合は null。
        /// </param>
        /// <exception cref="EncodingDetectionException">判定に失敗した場合</exception>
        public static ProbedFileReader Open(
            string path, EncodingSpec spec, EncodingDetectorOptions? detectorOptions = null)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                ReadBufferSize,
                FileOptions.SequentialScan);

            try
            {
                Encoding encoding;
                int bomLength;

                if (spec.IsAuto)
                {
                    // 判定はファイル全体を対象とする。先頭の一定量だけで判定すると、
                    // 英数字が続いたあとにマルチバイト文字が現れるファイルを誤判定し、
                    // 後続の文字をすべて壊して復号してしまうため。
                    byte[] content = ReadAll(stream, path);
                    bomLength = GetBomLength(content);
                    encoding = Detect(path, content, detectorOptions);
                }
                else
                {
                    // 明示指定された場合は判定を行わないため、BOM の確認に必要な分だけ読む
                    byte[] head = ReadHead(stream, BomHeadSize);
                    bomLength = GetBomLength(head);
                    encoding = spec.Encoding!;
                }

                // BOM の直後から復号を始めることで、U+FEFF が本文に混入するのを防ぐ
                stream.Seek(bomLength, SeekOrigin.Begin);

                var reader = new StreamReader(
                    stream,
                    encoding,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: ReadBufferSize,
                    leaveOpen: true);

                return new ProbedFileReader(stream, reader, encoding, bomLength > 0);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 1行読み込む。末尾に達している場合は null を返す。
        /// </summary>
        public string? ReadLine() => this._reader.ReadLine();

        /// <summary>
        /// 残り全体を1個の文字列として読み込む
        /// </summary>
        public string ReadToEnd() => this._reader.ReadToEnd();

        public void Dispose()
        {
            this._reader.Dispose();
            this._stream.Dispose();
        }

        /// <summary>
        /// 判定のためにファイル全体を読み込む
        /// </summary>
        /// <exception cref="EncodingDetectionException">判定できないサイズの場合</exception>
        private static byte[] ReadAll(FileStream stream, string path)
        {
            long length = stream.Length;

            if (length > EncodingInheritance.MaxDetectableLength)
            {
                throw new EncodingDetectionException(ValidationMessages.FileTooLargeToDetect(path), path);
            }

            return ReadHead(stream, (int)length);
        }

        /// <summary>
        /// ファイル先頭から指定バイト数を読み込む。ファイルが短い場合は読めた分だけを返す。
        /// </summary>
        private static byte[] ReadHead(FileStream stream, int size)
        {
            if (size <= 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[size];
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

            if (read == size)
            {
                return buffer;
            }

            var trimmed = new byte[read];
            Array.Copy(buffer, trimmed, read);
            return trimmed;
        }

        /// <summary>
        /// 復号に使う文字エンコーディングを判定する
        /// </summary>
        /// <exception cref="EncodingDetectionException">判定に失敗した場合</exception>
        private static Encoding Detect(string path, byte[] content, EncodingDetectorOptions? detectorOptions)
        {
            EncodingInformation information = EncodingProbe.Detect(content, detectorOptions);

            if (information.CodePage < 0)
            {
                throw new EncodingDetectionException(ValidationMessages.DetectionFailed(path), path);
            }

            // 読み取りでは BOM 方針は意味を持たないため、BOM 無しの実体で組み立てる。
            // 判定処理は .NET が提供していないコードページを返すことがあるため、
            // 例外を素通りさせず、対象ファイルごとの非終了エラーになるようにする。
            if (!EncodingVocabulary.TryBuildEncoding(information.CodePage, emitBom: false, out Encoding? encoding))
            {
                throw new EncodingDetectionException(
                    ValidationMessages.DetectedCodePageNotAvailable(
                        information.CodePage, information.EncodingWebName),
                    path,
                    EncodingDetectionException.CodePageNotAvailableId);
            }

            return encoding!;
        }

        /// <summary>
        /// ファイル先頭にある BOM のバイト数を返す。BOM が無い場合は 0。
        /// </summary>
        /// <remarks>
        /// 読み飛ばすかどうかは、指定された語彙ではなくファイルの実際の内容で決める（原則A）。
        /// BOM の判定にはクラスライブラリ側の実装を用いる。UTF-32 のBOMは UTF-16 のBOMを
        /// 前方に含むため判定順序が重要であり、規則を二重に持たないようにする。
        /// </remarks>
        private static int GetBomLength(byte[] head)
        {
            var detection = new ByteOrderMarkDetection();

            if (!detection.IsBOM(head))
            {
                return 0;
            }

            return EncodingVocabulary.BuildEncoding(detection.CodePage, emitBom: true).GetPreamble().Length;
        }
    }
}