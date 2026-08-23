using System;
using System.IO;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 文字エンコーディング・BOM・改行コードを明示してテキストファイルへ書き込む。
    /// </summary>
    /// <remarks>
    /// BOM を出力するかどうかは <see cref="EncodingSpec.EmitBom"/> だけで決める。
    /// StreamWriter は与えられた Encoding の <c>GetPreamble()</c> を書き出すため、
    /// BOM 付きインスタンスを渡すと「どの経路で解決されたか」によって出力が変わってしまう。
    /// ここでは BOM を自分で書き出し、StreamWriter には BOM を持たないインスタンスを渡す。
    /// </remarks>
    internal sealed class ProbedFileWriter : IDisposable
    {
        /// <summary>StreamWriter に与えるバッファサイズ</summary>
        private const int WriteBufferSize = 4096;

        private readonly FileStream _stream;
        private readonly StreamWriter _writer;
        private readonly string _lineBreak;

        private ProbedFileWriter(FileStream stream, StreamWriter writer, Encoding encoding, string lineBreak)
        {
            this._stream = stream;
            this._writer = writer;
            this.Encoding = encoding;
            this._lineBreak = lineBreak;
        }

        /// <summary>符号化に使用している文字エンコーディング（BOM を持たないインスタンス）</summary>
        public Encoding Encoding { get; }

        /// <summary>
        /// ファイルを新規作成（既存なら切り詰め）して開く。
        /// </summary>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディングと BOM 方針</param>
        /// <param name="lineBreak">改行として出力する文字列</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        public static ProbedFileWriter Create(string path, EncodingSpec spec, string lineBreak, bool force)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            if (force)
            {
                ClearReadOnly(path);
            }

            var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                WriteBufferSize,
                FileOptions.SequentialScan);

            try
            {
                int codePage = spec.Encoding!.CodePage;

                if (spec.EmitBom == true)
                {
                    byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();
                    stream.Write(preamble, 0, preamble.Length);
                }

                Encoding body = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);

                var writer = new StreamWriter(stream, body, WriteBufferSize, leaveOpen: true);

                return new ProbedFileWriter(stream, writer, body, lineBreak);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 文字列を書き込む（改行は付けない）
        /// </summary>
        public void Write(string text) => this._writer.Write(text);

        /// <summary>
        /// 改行を書き込む
        /// </summary>
        public void WriteLineBreak() => this._writer.Write(this._lineBreak);

        public void Dispose()
        {
            this._writer.Dispose();
            this._stream.Dispose();
        }

        /// <summary>
        /// 読み取り専用属性が付いている場合に外す（-Force の実体）
        /// </summary>
        private static void ClearReadOnly(string path)
        {
            var info = new FileInfo(path);

            if (info.Exists && (info.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                info.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }
}