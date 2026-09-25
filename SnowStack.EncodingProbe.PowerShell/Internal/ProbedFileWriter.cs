using System;
using System.IO;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 文字エンコーディングと BOM 方針を明示してテキストファイルへ書き込む。
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

        /// <summary>-Force で外した読み取り専用属性。閉じるときに元へ戻す。外していなければ null。</summary>
        private readonly ReadOnlyAttributeScope? _readOnlyScope;

        private bool _disposed;

        private ProbedFileWriter(
            FileStream stream, StreamWriter writer, Encoding encoding, ReadOnlyAttributeScope? readOnlyScope)
        {
            this._stream = stream;
            this._writer = writer;
            this.Encoding = encoding;
            this._readOnlyScope = readOnlyScope;
        }

        /// <summary>符号化に使用している文字エンコーディング（BOM を持たないインスタンス）</summary>
        public Encoding Encoding { get; }

        /// <summary>
        /// ファイルを新規作成（既存なら切り詰め）して開く。
        /// </summary>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディングと BOM 方針</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        public static ProbedFileWriter Create(string path, EncodingSpec spec, bool force)
            => Open(path, spec, force, FileMode.Create, emitBom: spec.EmitBom == true);

        /// <summary>
        /// ファイルを追記用に開く。存在しない場合は新規作成する。
        /// </summary>
        /// <remarks>
        /// BOM は書き出さない。ファイルの途中に BOM を書き込むことは、いかなる場合も正しくない。
        /// 追記では <c>utf8BOM</c> と <c>utf8NoBOM</c> が同じ結果になる。
        /// </remarks>
        /// <param name="path">追記先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディング（BOM 方針は使用しない）</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        public static ProbedFileWriter Append(string path, EncodingSpec spec, bool force)
            => Open(path, spec, force, FileMode.Append, emitBom: false);

        /// <summary>
        /// BOM を書くかどうかを呼び出し側が決めてファイルを開く（Out-ProbedFile 用）。
        /// </summary>
        /// <remarks>
        /// Out-ProbedFile は -Append でも、追記先が無い・0 バイトなら新規作成と同じく BOM を書く。
        /// また入力が 0 件のときは BOM を書かずに 0 バイトのファイルを作る。
        /// どちらも <see cref="Create"/> / <see cref="Append"/> の規則では表せないため、ここで明示させる。
        /// </remarks>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディング</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        /// <param name="append">追記するなら true、新規作成（既存なら切り詰め）なら false</param>
        /// <param name="emitBom">ファイル先頭に BOM を書くなら true</param>
        public static ProbedFileWriter Open(string path, EncodingSpec spec, bool force, bool append, bool emitBom)
            => Open(path, spec, force, append ? FileMode.Append : FileMode.Create, emitBom);

        /// <summary>
        /// 文字列を書き込む
        /// </summary>
        public void Write(string text) => this._writer.Write(text);

        /// <summary>
        /// ファイルを閉じ、-Force で外した読み取り専用属性を元に戻す。
        /// </summary>
        /// <remarks>
        /// 書き出しに失敗して例外になった場合も、属性は必ず戻す。
        /// </remarks>
        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;

            try
            {
                this._writer.Dispose();
                this._stream.Dispose();
            }
            finally
            {
                this._readOnlyScope?.Dispose();
            }
        }

        /// <summary>
        /// ファイルを開き、必要なら BOM を書き出してから StreamWriter を用意する
        /// </summary>
        private static ProbedFileWriter Open(
            string path, EncodingSpec spec, bool force, FileMode mode, bool emitBom)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            ReadOnlyAttributeScope? readOnlyScope = force ? ReadOnlyAttributeScope.ClearIfReadOnly(path) : null;
            FileStream? stream = null;

            try
            {
                stream = new FileStream(
                    path,
                    mode,
                    FileAccess.Write,
                    FileShare.Read,
                    WriteBufferSize,
                    FileOptions.SequentialScan);

                int codePage = spec.Encoding!.CodePage;

                if (emitBom)
                {
                    byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();
                    stream.Write(preamble, 0, preamble.Length);
                }

                Encoding body = EncodingVocabulary.BuildEncoding(codePage, emitBom: false);

                var writer = new StreamWriter(stream, body, WriteBufferSize, leaveOpen: true);

                return new ProbedFileWriter(stream, writer, body, readOnlyScope);
            }
            catch
            {
                stream?.Dispose();
                readOnlyScope?.Dispose();
                throw;
            }
        }
    }
}
