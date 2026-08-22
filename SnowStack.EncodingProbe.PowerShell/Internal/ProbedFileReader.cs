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
    /// 1本の FileStream で先頭を判定に使い、Seek(0) してから復号する。
    /// 判定用と読み込み用でファイルを2回開くことはしない。
    /// <br/>
    /// BOM は、指定された語彙にかかわらず常に読み飛ばす（原則A）。
    /// StreamReader に detectEncodingFromByteOrderMarks: false を渡すと BOM が
    /// U+FEFF として1行目の先頭に混入するため、ストリームの位置を BOM の後ろに
    /// 進めたうえで StreamReader を生成する。
    /// </remarks>
    internal sealed class ProbedFileReader : IDisposable
    {
        /// <summary>
        /// 判定のために読み込むファイル先頭の最大バイト数。
        /// </summary>
        /// <remarks>
        /// 大容量ファイルでも標準の Get-Content と同様のメモリ挙動を保つため、
        /// 判定に使う範囲を先頭の一定量に限る。
        /// この上限以下のファイルは全体が判定対象になるため、
        /// Resolve-Encoding と完全に同じ判定結果になる。
        /// </remarks>
        public const int DetectionBufferSize = 1024 * 1024;

        /// <summary>StreamReader に与えるバッファサイズ</summary>
        private const int ReadBufferSize = 4096;

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
        /// <exception cref="EncodingDetectionException">判定に失敗した場合</exception>
        public static ProbedFileReader Open(string path, EncodingSpec spec)
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
                byte[] head = ReadHead(stream);
                int bomLength = GetBomLength(head);
                Encoding encoding = ResolveEncoding(path, spec, head);

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
        /// 判定に使うファイル先頭のバイト列を読み込む
        /// </summary>
        private static byte[] ReadHead(FileStream stream)
        {
            long length = stream.Length;
            int size = length < DetectionBufferSize ? (int)length : DetectionBufferSize;

            if (size == 0)
            {
                return Array.Empty<byte>();
            }

            var head = new byte[size];
            int read = 0;

            while (read < size)
            {
                int count = stream.Read(head, read, size - read);

                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (read == size)
            {
                return head;
            }

            var trimmed = new byte[read];
            Array.Copy(head, trimmed, read);
            return trimmed;
        }

        /// <summary>
        /// 復号に使う文字エンコーディングを決める
        /// </summary>
        private static Encoding ResolveEncoding(string path, EncodingSpec spec, byte[] head)
        {
            if (!spec.IsAuto)
            {
                // 明示指定された場合は判定を行わない（誤判定を回避する手段として機能する）
                return spec.Encoding!;
            }

            EncodingInformation information = EncodingProbe.Detect(head);

            if (information.CodePage < 0)
            {
                throw new EncodingDetectionException(ValidationMessages.DetectionFailed(path), path);
            }

            // 読み取りでは BOM 方針は意味を持たないため、BOM 無しの実体で組み立てる
            return EncodingVocabulary.BuildEncoding(information.CodePage, emitBom: false);
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