using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 文字エンコーディングと BOM 方針を明示してテキストファイルへ書き込む。
    /// </summary>
    /// <remarks>
    /// BOM を出力するかどうかは <see cref="EncodingSpec.EmitBom"/> だけで決める。
    /// 与えられた Encoding の <c>GetPreamble()</c> に任せると、「どの経路で解決されたか」によって出力が変わってしまう。
    /// ここでは BOM を自分で書き出す。
    /// <br/>
    /// 符号化は、コードページから組み立て直した例外フォールバックの Encoding（<see cref="EncodingVocabulary.BuildStrictEncoding"/>）で行う
    /// （1.3.0 第五次。BUG-0001・0010・0012）。表現できない文字を '?' やベストフィット（é → e）に置き換えず、
    /// <see cref="UnrepresentableCharacterException"/> を投げる。利用者が渡した Encoding のフォールバックの設定は使わない。
    /// 1 回の <see cref="Write"/> の内容は、符号化に成功してから書き出すので、途中まで書かれることはない。
    /// Encoder は書き込みをまたいで状態を持ち越す（ISO-2022 のエスケープシーケンスの出方は StreamWriter を使っていたときと同じ）。
    /// <br/>
    /// 失敗したときは <see cref="Rollback"/> でファイルを元に戻す。
    /// <list type="bullet">
    /// <item>上書き・新規作成: 同じフォルダーの一時ファイルに書き、<see cref="Complete"/> で置き換える（既存なら <see cref="File.Replace(string, string, string)"/>、
    /// 無ければ移動）。元に戻すときは一時ファイルを消すだけで、元のファイルには触れない</item>
    /// <item>追記: 開く前の長さを覚えておき、元に戻すときはその長さに切り詰める。追記先が無かった場合は、作ったファイルを消す</item>
    /// </list>
    /// <see cref="Complete"/> も <see cref="Rollback"/> も呼ばずに <see cref="Dispose"/> した場合（パイプラインの停止など）は、
    /// それまでに書いた内容を残す（<see cref="Complete"/> と同じ。第四次までと同じ挙動）。
    /// </remarks>
    internal sealed class ProbedFileWriter : IDisposable
    {
        /// <summary>FileStream に与えるバッファサイズ</summary>
        private const int WriteBufferSize = 4096;

        /// <summary>一時ファイルの名前の接尾辞</summary>
        private const string TemporarySuffix = ".probedtmp";

        private readonly FileStream _stream;
        private readonly Encoder _encoder;
        private readonly string _path;

        /// <summary>上書き・新規作成のときの一時ファイル。追記のときは null。</summary>
        private readonly string? _temporaryPath;

        /// <summary>追記先の開く前の長さ。追記先が無かった場合は -1。上書きのときは使わない。</summary>
        private readonly long _originalLength;

        /// <summary>置き換える前のファイルの属性（上書きで、書き込み先が存在した場合）</summary>
        private readonly FileAttributes? _originalAttributes;

        /// <summary>-Force で外した読み取り専用属性。閉じるときに元へ戻す。外していなければ null。</summary>
        private readonly ReadOnlyAttributeScope? _readOnlyScope;

        /// <summary>エラーメッセージに示す文字エンコーディングの名前</summary>
        private readonly string _encodingName;

        private bool _finished;

        private ProbedFileWriter(
            FileStream stream,
            Encoding encoding,
            string path,
            string? temporaryPath,
            long originalLength,
            FileAttributes? originalAttributes,
            ReadOnlyAttributeScope? readOnlyScope,
            string encodingName)
        {
            this._stream = stream;
            this.Encoding = encoding;
            this._encoder = encoding.GetEncoder();
            this._path = path;
            this._temporaryPath = temporaryPath;
            this._originalLength = originalLength;
            this._originalAttributes = originalAttributes;
            this._readOnlyScope = readOnlyScope;
            this._encodingName = encodingName;
        }

        /// <summary>符号化に使用している文字エンコーディング（BOM を持たない、例外フォールバックのインスタンス）</summary>
        public Encoding Encoding { get; }

        /// <summary>
        /// ファイルを新規作成（既存なら置き換え）して開く。
        /// </summary>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディングと BOM 方針</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        public static ProbedFileWriter Create(string path, EncodingSpec spec, bool force)
            => Open(path, spec, force, append: false, emitBom: spec.EmitBom == true);

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
            => Open(path, spec, force, append: true, emitBom: false);

        /// <summary>
        /// BOM を書くかどうかを呼び出し側が決めてファイルを開く（Out-ProbedFile / Export-ProbedCsv 用）。
        /// </summary>
        /// <remarks>
        /// Out-ProbedFile は -Append でも、追記先が無い・0 バイトなら新規作成と同じく BOM を書く。
        /// また入力が 0 件のときは BOM を書かずに 0 バイトのファイルを作る。
        /// どちらも <see cref="Create"/> / <see cref="Append"/> の規則では表せないため、ここで明示させる。
        /// </remarks>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディング</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        /// <param name="append">追記するなら true、新規作成（既存なら置き換え）なら false</param>
        /// <param name="emitBom">ファイル先頭に BOM を書くなら true</param>
        public static ProbedFileWriter Open(string path, EncodingSpec spec, bool force, bool append, bool emitBom)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            int codePage = spec.Encoding!.CodePage;

            // 利用者が渡した Encoding のフォールバックの設定によらず、コードページから例外フォールバックで組み立て直す（BUG-0012）
            Encoding body = EncodingVocabulary.BuildStrictEncoding(codePage);
            string encodingName = EncodingVocabulary.GetUnifiedName(codePage, spec.EmitBom == true);

            ReadOnlyAttributeScope? readOnlyScope = force ? ReadOnlyAttributeScope.ClearIfReadOnly(path) : null;
            FileStream? stream = null;
            string? temporaryPath = null;

            try
            {
                long originalLength = -1;
                FileAttributes? originalAttributes = null;

                if (append)
                {
                    var info = new FileInfo(path);
                    originalLength = info.Exists ? info.Length : -1;

                    stream = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.Read, WriteBufferSize, FileOptions.SequentialScan);
                }
                else
                {
                    if (File.Exists(path))
                    {
                        originalAttributes = File.GetAttributes(path);
                    }

                    temporaryPath = CreateTemporaryPath(path);
                    stream = new FileStream(
                        temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, WriteBufferSize, FileOptions.SequentialScan);
                    TemporaryFileRegistry.Add(temporaryPath);
                }

                if (emitBom)
                {
                    byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();
                    stream.Write(preamble, 0, preamble.Length);
                }

                return new ProbedFileWriter(
                    stream, body, path, temporaryPath, originalLength, originalAttributes, readOnlyScope, encodingName);
            }
            catch
            {
                stream?.Dispose();

                if (temporaryPath != null)
                {
                    DeleteQuietly(temporaryPath);
                    TemporaryFileRegistry.Remove(temporaryPath);
                }

                readOnlyScope?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 文字列を符号化して書き込む。表現できない文字があれば、1 バイトも書かずに例外を投げる。
        /// </summary>
        /// <exception cref="UnrepresentableCharacterException">表現できない文字がある場合</exception>
        /// <exception cref="IOException">書き込みに失敗した場合</exception>
        public void Write(string text) => WriteBytes(Encode(text, flush: false));

        /// <summary>
        /// 文字列のすべての文字を、この文字エンコーディングで表現できるかを確かめる（まだ書き込まない）。
        /// </summary>
        /// <remarks>
        /// Encoder の状態を進めないよう、状態を持たない <see cref="Encoding.GetByteCount(string)"/> で確かめる。
        /// 文字を表現できるかどうかは状態（ISO-2022 の切り替え）によらない。
        /// 書き込む前に呼び、追記の整合性検査より先に表現できない文字を報告するために使う。
        /// </remarks>
        /// <exception cref="UnrepresentableCharacterException">表現できない文字がある場合</exception>
        public void EnsureRepresentable(string text) => EnsureRepresentable(this.Encoding, text, this._path, this._encodingName);

        /// <summary>
        /// 文字列のすべての文字を、指定した（例外フォールバックの）文字エンコーディングで表現できるかを確かめる
        /// </summary>
        /// <exception cref="UnrepresentableCharacterException">表現できない文字がある場合</exception>
        public static void EnsureRepresentable(Encoding strict, string text, string path, string encodingName)
        {
            try
            {
                strict.GetByteCount(text);
            }
            catch (EncoderFallbackException exception)
            {
                throw UnrepresentableCharacterException.From(exception, path, encodingName);
            }
        }

        private void WriteBytes(byte[] bytes) => this._stream.Write(bytes, 0, bytes.Length);

        /// <summary>
        /// 書き込みを完了する。Encoder に残った内容（ISO-2022 の ASCII へ戻すエスケープシーケンスなど）を書き出し、
        /// 上書きなら一時ファイルで書き込み先を置き換える。失敗した場合はファイルを元に戻して例外を投げる。
        /// </summary>
        /// <exception cref="UnrepresentableCharacterException">末尾に対になっていないサロゲートが残っていた場合</exception>
        /// <exception cref="IOException">書き出し・置き換えに失敗した場合</exception>
        /// <exception cref="UnauthorizedAccessException">置き換えに失敗した場合</exception>
        public void Complete()
        {
            if (this._finished)
            {
                return;
            }

            try
            {
                WriteBytes(Encode(string.Empty, flush: true));
                this._stream.Flush();
                this._stream.Dispose();

                if (this._temporaryPath != null)
                {
                    Replace();
                }

                this._finished = true;
            }
            catch
            {
                Rollback();
                throw;
            }
            finally
            {
                if (this._finished)
                {
                    this._readOnlyScope?.Dispose();
                }
            }
        }

        /// <summary>
        /// ファイルを書き込みの前の状態に戻す（上書きは一時ファイルを消す。追記は元の長さに切り詰める）。
        /// </summary>
        /// <remarks>
        /// 後始末から呼ばれるため、ここでは例外を投げない（元の例外を覆い隠さない）。
        /// </remarks>
        public void Rollback()
        {
            if (this._finished)
            {
                return;
            }

            this._finished = true;

            try
            {
                if (this._temporaryPath != null)
                {
                    this._stream.Dispose();
                    DeleteQuietly(this._temporaryPath);
                    TemporaryFileRegistry.Remove(this._temporaryPath);
                }
                else if (this._originalLength < 0)
                {
                    // 追記先が無かった: 作ったファイルを消す
                    this._stream.Dispose();
                    DeleteQuietly(this._path);
                }
                else
                {
                    // まだバッファに残っている分も含めて、元の長さに切り詰める
                    this._stream.SetLength(this._originalLength);
                    this._stream.Dispose();
                }
            }
            catch (IOException)
            {
                // 元に戻せなかった場合も、後始末を続ける
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
            catch (ObjectDisposedException)
            {
                // すでに閉じていた（Complete の途中で失敗した場合）。切り詰めは下でやり直す
                if (this._temporaryPath == null && this._originalLength >= 0)
                {
                    TruncateQuietly(this._path, this._originalLength);
                }
            }
            finally
            {
                this._readOnlyScope?.Dispose();
            }
        }

        /// <summary>
        /// ファイルを閉じ、-Force で外した読み取り専用属性を元に戻す。
        /// <see cref="Complete"/> も <see cref="Rollback"/> も呼んでいなければ、それまでに書いた内容を残す（<see cref="Complete"/> と同じ）。
        /// </summary>
        /// <remarks>
        /// 後始末から呼ばれるため、完了に失敗しても例外を投げない（ファイルは元に戻る）。
        /// </remarks>
        public void Dispose()
        {
            if (this._finished)
            {
                return;
            }

            try
            {
                Complete();
            }
            catch (UnrepresentableCharacterException)
            {
                // Complete がファイルを元に戻している
            }
            catch (IOException)
            {
                // 同上
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }
        }

        private byte[] Encode(string text, bool flush)
        {
            char[] chars = text.ToCharArray();

            try
            {
                int count = this._encoder.GetByteCount(chars, 0, chars.Length, flush);
                var bytes = new byte[count];
                this._encoder.GetBytes(chars, 0, chars.Length, bytes, 0, flush);
                return bytes;
            }
            catch (EncoderFallbackException exception)
            {
                throw UnrepresentableCharacterException.From(exception, this._path, this._encodingName);
            }
        }

        /// <summary>
        /// 一時ファイルで書き込み先を置き換える（書き込み先が無ければ移動する）
        /// </summary>
        private void Replace()
        {
            if (File.Exists(this._path))
            {
                File.Replace(this._temporaryPath!, this._path, null);

                if (this._originalAttributes.HasValue)
                {
                    // 読み取り専用属性は ReadOnlyAttributeScope が戻す
                    File.SetAttributes(this._path, this._originalAttributes.Value & ~FileAttributes.ReadOnly);
                }
            }
            else
            {
                File.Move(this._temporaryPath!, this._path);
            }

            TemporaryFileRegistry.Remove(this._temporaryPath!);
        }

        /// <summary>
        /// 書き込み先と同じフォルダーに、一時ファイルのパスを作る（同じボリュームで置き換えるため）
        /// </summary>
        private static string CreateTemporaryPath(string path)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string name = "." + Path.GetFileName(path) + "."
                + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).Substring(0, 8) + TemporarySuffix;

            return Path.Combine(directory, name);
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TruncateQuietly(string path, long length)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                {
                    stream.SetLength(length);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
