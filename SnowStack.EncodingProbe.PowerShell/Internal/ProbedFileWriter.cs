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
    /// <item>上書き・新規作成: <see cref="SafeFileOverwrite"/> に任せる（小さなファイルはメモリに写してから直接書き、それ以外は一時ファイルで置き換える。ハードリンク・権限・リンク切れの場合は書き方を切り替える。1.3.0 第五次の後半・仕上げ）</item>
    /// <item>追記: 追記先を直接開き、開いたときの長さを覚えておく。元に戻すときはその長さに切り詰める。追記先が無かった場合は、作ったファイルを消す。
    /// シンボリックリンクはたどって実体に追記する（リンク切れならリンクの先に作る。標準の Add-Content と同じ）。
    /// ISO-2022 で書く追記で、追記先が ASCII 以外の状態のまま終わっていれば、最初に書く内容の前に ASCII に戻すシーケンスを書く（<see cref="Iso2022TailState"/>。1.3.0 第六次）</item>
    /// </list>
    /// どちらも、書き込み先が同じパイプラインで読み取り中のファイルと同じ実体なら <see cref="SameFileRoundTripException"/> を投げる。
    /// 上書きは書き始める前（開く前）に、追記は「切り詰めずに開く → 開いたハンドルで比べる → 書く」の順に比べる。
    /// <see cref="Complete"/> も <see cref="Rollback"/> も呼ばずに <see cref="Dispose"/> した場合（パイプラインの停止など）は、
    /// それまでに書いた内容を残す（<see cref="Complete"/> と同じ。第四次までと同じ挙動）。
    /// </remarks>
    internal sealed class ProbedFileWriter : IDisposable
    {
        /// <summary>FileStream に与えるバッファサイズ</summary>
        private const int WriteBufferSize = 4096;

        /// <summary>追記のときのストリーム。上書き・新規作成のときは null（<see cref="SafeFileOverwrite.Write"/> に書く）。</summary>
        private readonly FileStream? _stream;
        private readonly Encoder _encoder;
        private readonly string _path;

        /// <summary>上書き・新規作成のときの書き込み。追記のときは null。</summary>
        private readonly SafeFileOverwrite? _overwrite;

        /// <summary>追記先の開いたときの長さ。追記先が無かった場合は -1。上書きのときは使わない。</summary>
        private readonly long _originalLength;

        /// <summary>追記先の実体のパス（リンクをたどったもの）。上書きのときは使わない。</summary>
        private readonly string _appendTargetPath;

        /// <summary>-Force で外した読み取り専用属性。閉じるときに元へ戻す。外していなければ null。</summary>
        private readonly ReadOnlyAttributeScope? _readOnlyScope;

        /// <summary>エラーメッセージに示す文字エンコーディングの名前</summary>
        private readonly string _encodingName;

        /// <summary>
        /// 追記で、最初に書く内容の前に書くバイト列（ISO-2022 の追記先を ASCII に戻すシーケンス。<see cref="Iso2022TailState"/>）。
        /// 書いた後・不要なら null。
        /// </summary>
        private byte[]? _pendingPrefix;

        private bool _finished;

        private ProbedFileWriter(
            FileStream? stream,
            Encoding encoding,
            string path,
            SafeFileOverwrite? overwrite,
            long originalLength,
            string appendTargetPath,
            ReadOnlyAttributeScope? readOnlyScope,
            string encodingName,
            byte[]? pendingPrefix)
        {
            this._pendingPrefix = pendingPrefix != null && pendingPrefix.Length > 0 ? pendingPrefix : null;
            this._stream = stream;
            this.Encoding = encoding;
            this._encoder = encoding.GetEncoder();
            this._path = path;
            this._overwrite = overwrite;
            this._originalLength = originalLength;
            this._appendTargetPath = appendTargetPath;
            this._readOnlyScope = readOnlyScope;
            this._encodingName = encodingName;
        }

        /// <summary>符号化に使用している文字エンコーディング（BOM を持たない、例外フォールバックのインスタンス）</summary>
        public Encoding Encoding { get; }

        /// <summary>元に戻すときに写しから書き戻せず、一時フォルダーに残した写しのパス（<see cref="SafeFileOverwrite.KeptBackupPath"/>）。無ければ null。</summary>
        public string? KeptBackupPath => this._overwrite?.KeptBackupPath;

        /// <summary>
        /// ファイルを新規作成（既存なら置き換え）して開く。
        /// </summary>
        /// <remarks>
        /// 書き込み先がリンク切れのシンボリックリンクなら、書かずに <see cref="LinkTargetNotFoundException"/> を投げる
        /// （Set-ProbedContent。標準の Set-Content と同じく書かない）。
        /// </remarks>
        /// <param name="path">書き込み先の絶対パス</param>
        /// <param name="spec">使用する文字エンコーディングと BOM 方針</param>
        /// <param name="force">読み取り専用属性を外してから書き込む場合は true</param>
        public static ProbedFileWriter Create(string path, EncodingSpec spec, bool force)
            => Open(path, spec, force, append: false, emitBom: spec.EmitBom == true, DanglingLinkPolicy.Reject);

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
        /// <param name="danglingLink">
        /// 上書きで、書き込み先がリンク切れのシンボリックリンクだった場合の扱い
        /// （既定は、標準の Out-File / Export-Csv と同じくリンクの先に作る）
        /// </param>
        /// <exception cref="SameFileRoundTripException">書き込み先が、同じパイプラインで読み取り中のファイルと同じ実体の場合</exception>
        public static ProbedFileWriter Open(
            string path, EncodingSpec spec, bool force, bool append, bool emitBom,
            DanglingLinkPolicy danglingLink = DanglingLinkPolicy.CreateTarget)
        {
            CodePagesProviderRegistration.EnsureRegistered();

            int codePage = spec.Encoding!.CodePage;

            // 利用者が渡した Encoding のフォールバックの設定によらず、コードページから例外フォールバックで組み立て直す（BUG-0012）
            Encoding body = EncodingVocabulary.BuildStrictEncoding(codePage);
            string encodingName = EncodingVocabulary.GetUnifiedName(codePage, spec.EmitBom == true);

            if (!append && ActiveReadRegistry.IsBeingRead(path))
            {
                // 上書きは、一時ファイルに書き始める前に比べる
                throw new SameFileRoundTripException(path);
            }

            ReadOnlyAttributeScope? readOnlyScope = force ? ReadOnlyAttributeScope.ClearIfReadOnly(path) : null;
            FileStream? stream = null;
            SafeFileOverwrite? overwrite = null;
            bool createdByAppend = false;
            string appendTargetPath = path;
            byte[]? resetPrefix = null;

            try
            {
                long originalLength = -1;

                if (append)
                {
                    // リンク切れは「無い」に数える（開くとリンクの先にファイルができる）
                    bool existed = FileIdentity.Of(path).Exists;

                    stream = new FileStream(
                        path, FileMode.Append, FileAccess.Write, FileShare.Read, WriteBufferSize, FileOptions.SequentialScan);
                    createdByAppend = !existed;

                    // 切り詰めずに開いてから、開いたハンドルで実体を比べる
                    FileIdentity opened = FileIdentity.FromHandle(stream.SafeFileHandle, path);
                    appendTargetPath = opened.FinalPath ?? path;

                    if (ActiveReadRegistry.IsBeingRead(opened))
                    {
                        throw new SameFileRoundTripException(path);
                    }

                    originalLength = existed ? stream.Length : -1;

                    if (originalLength > 0 && Iso2022TailState.AppliesTo(codePage))
                    {
                        // ISO-2022 の追記先が ASCII 以外の状態のまま終わっていれば、追記の前に戻す（第六次。BUG-0018）。
                        // 書く文字エンコーディングが ISO-2022 のときだけ（-AllowEncodingChange で別の文字エンコーディングを足す場合は手を入れない）
                        resetPrefix = Iso2022TailState.GetResetSequence(appendTargetPath, originalLength, codePage);
                    }
                }
                else
                {
                    overwrite = SafeFileOverwrite.Begin(path, danglingLink, WriteBufferSize);
                }

                if (emitBom)
                {
                    byte[] preamble = EncodingVocabulary.BuildEncoding(codePage, emitBom: true).GetPreamble();

                    if (overwrite != null)
                    {
                        overwrite.Write(preamble, 0, preamble.Length);
                    }
                    else
                    {
                        stream!.Write(preamble, 0, preamble.Length);
                    }
                }

                return new ProbedFileWriter(
                    stream, body, path, overwrite, originalLength, appendTargetPath, readOnlyScope, encodingName, resetPrefix);
            }
            catch
            {
                if (overwrite != null)
                {
                    overwrite.Rollback();
                }
                else
                {
                    if (stream != null)
                    {
                        DisposeQuietly(stream);
                    }

                    if (createdByAppend)
                    {
                        DeleteQuietly(appendTargetPath);
                    }
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

        private void WriteBytes(byte[] bytes)
        {
            if (this._pendingPrefix != null && bytes.Length > 0)
            {
                // 戻すシーケンスは、1 バイト以上書くときにだけ先に書く（何も書かない呼び出しでは追記先を変えない）。
                // 補う改行（Export-ProbedCsv の 18.3）も追記する内容もこの後ろになる。取り消しは元の長さへの切り詰めで一緒に消える
                byte[] prefix = this._pendingPrefix;
                this._pendingPrefix = null;
                this._stream!.Write(prefix, 0, prefix.Length);
            }

            if (this._overwrite != null)
            {
                this._overwrite.Write(bytes, 0, bytes.Length);
            }
            else
            {
                this._stream!.Write(bytes, 0, bytes.Length);
            }
        }

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

                if (this._overwrite != null)
                {
                    this._overwrite.Commit();
                }
                else
                {
                    this._stream!.Flush();
                    this._stream.Dispose();
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
        /// ファイルを書き込みの前の状態に戻す（上書きは <see cref="SafeFileOverwrite.Rollback"/>。追記は元の長さに切り詰める）。
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
                if (this._overwrite != null)
                {
                    this._overwrite.Rollback();
                }
                else if (this._originalLength < 0)
                {
                    // 追記先が無かった: 作ったファイル（リンク切れなら、リンクの先に作った実体）を消す
                    DisposeQuietly(this._stream!);
                    DeleteQuietly(this._appendTargetPath);
                }
                else
                {
                    // まだバッファに残っている分も含めて、元の長さに切り詰める
                    this._stream!.SetLength(this._originalLength);
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
                if (this._overwrite == null && this._originalLength >= 0)
                {
                    TruncateQuietly(this._appendTargetPath, this._originalLength);
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

        private static void DisposeQuietly(FileStream stream)
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
                // 書き残しを書き出せなかった。作ったファイルは消すか切り詰めるので問題にならない
            }
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
