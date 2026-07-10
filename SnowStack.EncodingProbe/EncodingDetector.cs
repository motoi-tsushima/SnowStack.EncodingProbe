using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using UtfUnknown;

namespace SnowStack.EncodingProbe
{
    /// <summary>
    /// 改行コードの種類
    /// </summary>
    public enum LineBreakType
    {
        /// <summary>改行コードなし</summary>
        None,
        /// <summary>CR-LF (Windows)</summary>
        CrLf,
        /// <summary>LF (Unix/Mac)</summary>
        Lf,
        /// <summary>CR (旧Mac)</summary>
        Cr,
        /// <summary>LF と CR-LF の混在</summary>
        LfAndCrLf,
        /// <summary>CR と CR-LF の混在</summary>
        CrAndCrLf,
        /// <summary>LF と CR の混在</summary>
        LfAndCr,
        /// <summary>LF・CR・CR-LF の混在</summary>
        LfAndCrAndCrLf,
    }

    /// <summary>
    /// 文字エンコーディング判定
    /// </summary>
    public class EncodingDetector
    {
        /// <summary>コードページ：US-ASCII</summary>
        private const int CodePageAscii = 20127;
        
        /// <summary>コードページ：ISO-2022-JP (JIS)</summary>
        private const int CodePageJis = 50220;
        
        /// <summary>コードページ：ISO-2022-KR</summary>
        private const int CodePageIso2022Kr = 50225;
        
        /// <summary>コードページ：ISO-2022-CN (x-cp50227)</summary>
        private const int CodePageIso2022Cn = 50227;
        
        /// <summary>コードページ：ISO-2022-TW</summary>
        private const int CodePageIso2022Tw = 50229;
        
        /// <summary>コードページ：UTF-8</summary>
        private const int CodePageUtf8 = 65001;
        
        /// <summary>コードページ：UTF-16 Little Endian</summary>
        private const int CodePageUtf16Le = 1200;
        
        /// <summary>コードページ：UTF-16 Big Endian</summary>
        private const int CodePageUtf16Be = 1201;
        
        /// <summary>コードページ：UTF-32 Little Endian</summary>
        private const int CodePageUtf32Le = 12000;
        
        /// <summary>コードページ：UTF-32 Big Endian</summary>
        private const int CodePageUtf32Be = 12001;
        
        /// <summary>コードページ：EUC-JP</summary>
        private const int CodePageEucJp = 20932;
        
        /// <summary>コードページ：EUC-KR</summary>
        private const int CodePageEucKr = 51949;
        
        /// <summary>コードページ：EUC-CN (GB2312)</summary>
        private const int CodePageEucCn = 51936;
        
        /// <summary>コードページ：EUC-TW</summary>
        private const int CodePageEucTw = 51950;
        
        /// <summary>コードページ：Shift_JIS (CP932)</summary>
        private const int CodePageShiftJis = 932;
        
        /// <summary>コードページ：CP949 (韓国)</summary>
        private const int CodePageCp949 = 949;
        
        /// <summary>コードページ：CP936 (GBK, 中国簡体字)</summary>
        private const int CodePageCp936 = 936;
        
        /// <summary>コードページ：GB18030 (中国)</summary>
        private const int CodePageGb18030 = 54936;
        
        /// <summary>コードページ：CP950 (Big5, 台湾・香港繁体字)</summary>
        private const int CodePageCp950 = 950;
        
        /// <summary>マジックナンバー：バッファインデックス2</summary>
        private const int BufferIndex2 = 2;
        
        /// <summary>マジックナンバー：バッファインデックス3</summary>
        private const int BufferIndex3 = 3;
        
        /// <summary>マジックナンバー：0xFF</summary>
        private const byte DefaultBufferValue = 0xFF;

        /// <summary>バッファサイズ</summary>
        public int BufferSize { get; private set; }

        /// <summary>
        /// カルチャー名のキャッシュ（IsChineseSimplifiedCulture 内で一度取得したカルチャー名を保持して、複数回の判定で繰り返し取得するのを防止するため）
        /// </summary>
        private string _cultureName = null;

        /// <summary>
        /// ファイルバイナリ配列
        /// </summary>
        private byte[] _buffer = null;

        /// <summary>
        /// 文字エンコーディング判定の動作モード
        /// </summary>
        private DetectionMode _encodingDetectorMode = DetectionMode.Standard;

        /// <summary>
        /// バイト配列を受け取るコンストラクタ
        /// </summary>
        /// <param name="buffer">判定対象のバイト配列</param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> が null の場合</exception>
        public EncodingDetector(byte[] buffer, DetectionMode encodingDetectorMode = DetectionMode.Standard)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            this._buffer = buffer;
            this.BufferSize = buffer.Length;
            this._encodingDetectorMode = encodingDetectorMode;
        }

        /// <summary>
        /// ファイルストリームを受け取るコンストラクタ。
        /// ストリームの現在位置から末尾までを読み込んで判定する。
        /// </summary>
        /// <param name="stream">判定対象のファイルストリーム</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> が null の場合</exception>
        /// <exception cref="ArgumentException"><paramref name="stream"/> が読み取り不可の場合</exception>
        public EncodingDetector(Stream stream, DetectionMode encodingDetectorMode = DetectionMode.Standard)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("読み取り不可能なストリームです。", nameof(stream));

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            this._buffer = ms.ToArray();
            this.BufferSize = this._buffer.Length;
            this._encodingDetectorMode = encodingDetectorMode;
        }

        /// <summary>
        /// ファイルパスを受け取るコンストラクタ。
        /// 指定したファイルを開いてバイト配列として読み込んで判定する。
        /// </summary>
        /// <param name="filePath">判定対象のファイルパス</param>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> が null または空の場合</exception>
        public EncodingDetector(string filePath, DetectionMode encodingDetectorMode = DetectionMode.Standard)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            this._buffer = ms.ToArray();
            this.BufferSize = this._buffer.Length;
            this._encodingDetectorMode = encodingDetectorMode;
        }

        /// <summary>
        /// バイト配列から改行コードの種類を判定してセットする
        /// </summary>
        public LineBreakType DetectLineBreak()
        {
            if (this.BufferSize == 0)
            {
                return  LineBreakType.None;
            }

            int countCrLf = 0;
            int countLf = 0;
            int countCr = 0;

            for (int i = 0; i < this.BufferSize; i++)
            {
                if (this._buffer[i] == 0x0D)
                {
                    if (i + 1 < this.BufferSize && this._buffer[i + 1] == 0x0A)
                    {
                        // UTF-8 / ASCII の CR-LF: 0x0D 0x0A
                        countCrLf++;
                        i++; // 0x0A をスキップ
                    }
                    else if (i + 2 < this.BufferSize && this._buffer[i + 1] == 0x00 && this._buffer[i + 2] == 0x0A)
                    {
                        // UTF-16 (LE/BE) の CR-LF: 0x0D 0x00 0x0A (...)
                        countCrLf++;
                        i += 2; // 0x00 0x0A をスキップ
                    }
                    else if (i + 4 < this.BufferSize && this._buffer[i + 1] == 0x00 && this._buffer[i + 2] == 0x00 && this._buffer[i + 3] == 0x00 && this._buffer[i + 4] == 0x0A)
                    {
                        // UTF-32 (LE/BE) の CR-LF: 0x0D 0x00 0x00 0x00 0x0A (...)
                        countCrLf++;
                        i += 4; // 0x00 0x00 0x00 0x0A をスキップ
                    }
                    else
                    {
                        countCr++;
                    }
                }
                else if (this._buffer[i] == 0x0A)
                {
                    countLf++;
                }
            }

            return  DetermineLineBreakType(countCrLf, countLf, countCr);
        }

        /// <summary>
        /// 改行コード出現回数から種類を判定する
        /// </summary>
        private static LineBreakType DetermineLineBreakType(int countCrLf, int countLf, int countCr)
        {
            if (countLf == 0 && countCr == 0 && countCrLf == 0)
            {
                return LineBreakType.None;
            }
            else if (countCrLf > 0 && countCrLf == countLf + countCrLf && countCr == 0 && countLf == 0)
            {
                return LineBreakType.CrLf;
            }
            else if (countLf > 0 && countCr == 0 && countCrLf == 0)
            {
                return LineBreakType.Lf;
            }
            else if (countCr > 0 && countLf == 0 && countCrLf == 0)
            {
                return LineBreakType.Cr;
            }
            else if (countLf > 0 && countCrLf > 0 && countCr == 0)
            {
                return LineBreakType.LfAndCrLf;
            }
            else if (countCr > 0 && countCrLf > 0 && countLf == 0)
            {
                return LineBreakType.CrAndCrLf;
            }
            else if (countCrLf == 0 && countCr > 0 && countLf > 0)
            {
                return LineBreakType.LfAndCr;
            }
            else
            {
                return LineBreakType.LfAndCrAndCrLf;
            }
        }

        /// <summary>
        /// コードページからエンコーディング名を取得する
        /// </summary>
        /// <param name="codePage">コードページ</param>
        /// <returns>エンコーディング名</returns>
        private static string EncodingName(int codePage) =>
            codePage switch
            {
                20127 => "us-ascii",
                50220 => "iso-2022-jp",
                50225 => "iso-2022-kr",
                50227 => "x-cp50227",
                50229 => "iso-2022-tw",
                65001 => "utf-8",
                20932 => "euc-jp",
                51936 => "euc-cn",
                51949 => "euc-kr",
                51950 => "euc-tw",
                  932 => "shift_jis",
                  949 => "cp949",
                  936 => "gbk",
                54936 => "gb18030",
                  950 => "big5",
                 1200 => "utf-16",
                 1201 => "unicodeFFFE",
                12000 => "utf-32",
                12001 => "utf-32BE",
                    _ => "I do not know.",
            };

        /// <summary>
        /// PowerShell 6.2+/7.x の -Encoding にフレンドリ名として直接渡せる値の一覧
        /// (<see cref="PSEncodingName(int, bool)"/> が返しうるフレンドリ名と一致させること)
        /// </summary>
        private static readonly HashSet<string> KnownPSFriendlyNames = new(StringComparer.Ordinal)
        {
            "utf8BOM", "utf8NoBOM",
            "unicode", "bigendianunicode",
            "utf32", "bigendianutf32",
            "ascii", "utf7",
        };

        /// <summary>
        /// コードページ番号と BOM の有無から、PowerShell 6.2+/7.x の
        /// -Encoding に渡せるフレンドリ名を返す。フレンドリ名が存在しない場合は
        /// .NET の WebName(<see cref="EncodingInformation.EncodingWebName"/> と同じ値)を返す。
        /// </summary>
        /// <param name="codePage">エンコーディングのコードページ番号(例: 65001, 932)。</param>
        /// <param name="bom">BOM を伴うか。UTF-8 でのみ名前に反映される。</param>
        /// <returns>-Encoding に渡せるフレンドリ名、またはフレンドリ名が存在しない場合は WebName。</returns>
        internal static string PSEncodingName(int codePage, bool bom)
        {
            if (codePage <= 0)
                throw new ArgumentOutOfRangeException(nameof(codePage), codePage, "コードページ番号が不正です。");

            string psEncodingName = string.Empty;
            psEncodingName = codePage switch
            {
                // UTF-8: PowerShell が名前で BOM 有無を区別する唯一のケース
                65001 => bom ? "utf8BOM" : "utf8NoBOM",

                // UTF-16 / UTF-32: フレンドリ名は常に BOM 付きで、BOM なしの名前は存在しない。
                // よって bom では分岐しない(読み取り時はデコーダが BOM 有無を吸収するため実害なし)。
                1200 => "unicode",          // UTF-16 LE
                1201 => "bigendianunicode", // UTF-16 BE
                12000 => "utf32",            // UTF-32 LE
                12001 => "bigendianutf32",   // UTF-32 BE

                20127 => "ascii",            // US-ASCII (7-bit)
                65000 => "utf7",             // 非推奨(.NET 側の生成は net48 のみ可)

                // フレンドリ名なし: Shift-JIS, EUC-JP, ISO-2022-JP, GB2312, Big5, EUC-KR,
                // ISO-8859-x など。PowerShell の -Encoding フレンドリ名としては渡せないため、
                // 代わりに .NET の WebName を返す(数値コードページは EncodingInformation.CodePage 側で取得可能)。
                _ => EncodingName(codePage),
            };
            return psEncodingName;
        }

        /// <summary>
        /// PSEncodingName が PowerShell 6.2+ の登録済みフレンドリ名である場合のみ UsePSName を true にする
        /// </summary>
        /// <param name="psEncodingName">PSEncodingName で算出した値</param>
        /// <returns>true=PSEncodingName がフレンドリ名としてそのまま -Encoding に渡せる、false=WebName（コードページ経由での指定が必要）</returns>
        private static bool SetUsePSName(string psEncodingName)
        {
            // PSEncodingName がフレンドリ名一覧に含まれる場合のみ、そのまま -Encoding に渡せる。
            // フレンドリ名が存在しない場合、PSEncodingName には WebName が入っており、
            // これは -Encoding に直接渡せないため UsePSName は false とする。
            return !string.IsNullOrEmpty(psEncodingName) && KnownPSFriendlyNames.Contains(psEncodingName);
        }

        /// <summary>
        /// バイト配列の先頭が指定したBOMと一致するかどうかを判定する
        /// </summary>
        /// <param name="data"></param>
        /// <param name="bom"></param>
        /// <returns></returns>
        private bool IsMatched(byte[] data, byte[] bom)
        {
            if (data == null || data.Length < bom.Length)
                return false;

            bool result = true;

            for (int i = 0; i < bom.Length; i++)
            {
                if (bom[i] != data[i])
                {
                    result = false;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 判定実行
        /// </summary>
        /// <param name="culture">使用するカルチャー名（例: "ja-JP"）。null または空の場合は現在のカルチャーを使用する。</param>
        /// <returns>文字エンコーディング判定情報</returns>
        public EncodingInformation Detection(string culture = null)
        {
            bool outOfSpecification;
            EncodingInformation encInfo = new EncodingInformation();
            ByteOrderMarkDetection bomJudg = new ByteOrderMarkDetection();

            // パラメータで与えられたカルチャー名を使用する（未指定の場合は現在のカルチャーにフォールバック）
            _cultureName = !string.IsNullOrEmpty(culture) ? culture : CultureInfo.CurrentCulture.Name;
            encInfo.Culture = _cultureName;

            //改行コードの種類を判定してセットする
            encInfo.LineBreak = DetectLineBreak();

            // BOMチェック
            if (bomJudg.IsBOM(this._buffer))
            {
                encInfo.CodePage = bomJudg.CodePage;
                encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                encInfo.Bom = true;
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                return encInfo;
            }
            else
            {
                encInfo.CodePage = -1;
                encInfo.Bom = false;
            }

            if(this._encodingDetectorMode == DetectionMode.Skippable)
            {
                // Skippable モードでは、BOM がない場合はここで判定を終了する
                return encInfo;
            }

            // ISO-2022 または ASCII 判定
            bool isISO2022;
            int iso2022CodePage;
            outOfSpecification = ISO2022_Detection(out isISO2022, out iso2022CodePage);

            if (outOfSpecification == false)
            {
                if (isISO2022 == true)
                {
                    encInfo.CodePage = iso2022CodePage;
                    encInfo.Bom = false;
                }
                else
                {
                    encInfo.CodePage = CodePageAscii;
                    encInfo.Bom = false;
                }

                encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                return encInfo;
            }

            // UTF-32 判定（UTF-16より先に判定）
            bool isLittleEndian32;
            outOfSpecification = Utf32_Detection(out isLittleEndian32);

            if (outOfSpecification == false)
            {
                if (isLittleEndian32)
                {
                    encInfo.CodePage = CodePageUtf32Le;
                }
                else
                {
                    encInfo.CodePage = CodePageUtf32Be;
                }
                encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                encInfo.Bom = false;
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                return encInfo;
            }

            // UTF-16 判定
            bool isLittleEndian16;
            outOfSpecification = Utf16_Detection(out isLittleEndian16);

            if (outOfSpecification == false)
            {
                if (isLittleEndian16)
                {
                    encInfo.CodePage = CodePageUtf16Le;
                }
                else
                {
                    encInfo.CodePage = CodePageUtf16Be;
                }
                encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                encInfo.Bom = false;
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                return encInfo;
            }

            // UTF-8 判定
            outOfSpecification = Utf8_Detection();

            if (outOfSpecification == false)
            {
                encInfo.CodePage = CodePageUtf8;
                encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                encInfo.Bom = false;
                encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                return encInfo;
            }

            // カルチャー情報を取得して判定順序を決定
            bool isChineseSimplified = IsChineseSimplifiedCulture();

            if (isChineseSimplified)
            {
                // 中国（簡体字）の場合：GB2312 / GBK / GB18030 の3段階アルゴリズムで判定
                int gbCodePage;
                outOfSpecification = GB_Detection(out gbCodePage);

                if (outOfSpecification == false)
                {
                    encInfo.CodePage = gbCodePage;
                    encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                    encInfo.Bom = false;
                    encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                    return encInfo;
                }
            }
            else
            {
                // 中国簡体字以外の場合：EUC → CPxxx の順で判定

                // EUC 判定 (EUC-JP/KR/TW)
                int eucCodePage;
                outOfSpecification = EUCxx_Detection(out eucCodePage);

                if (outOfSpecification == false)
                {
                    // 日本語カルチャーの場合、EUC-JP と Shift-JIS の両方に該当するか確認する
                    if (eucCodePage == CodePageEucJp && !SJIS_Detection())
                    {
                        // 両方に該当 → 改行コードで判定、改行コードがなければOSで判定
                        encInfo.Bom = false;

                        bool useShiftJis;
                        if (encInfo.LineBreak == LineBreakType.None)
                        {
                            // 改行コードなし → OSで判定
                            useShiftJis = PlatformInfo.IsWindows;
                        }
                        else
                        {
                            // 改行コードで判定：CR-LF（Windows仕様）ならShift-JIS
                            useShiftJis = (encInfo.LineBreak == LineBreakType.CrLf);
                        }

                        encInfo.CodePage = useShiftJis ? CodePageShiftJis : CodePageEucJp;
                        encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                        encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                        return encInfo;
                    }

                    // 韓国語カルチャーの場合、EUC-KR と CP949 の両方に該当するか確認する
                    if (eucCodePage == CodePageEucKr && !CP949_Detection())
                    {
                        // 両方に該当 → CP949 を優先
                        encInfo.CodePage = CodePageCp949;
                        encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                        encInfo.Bom = false;
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                        encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                        return encInfo;
                    }

                    // 台湾・香港カルチャーの場合、EUC-TW と CP950 の両方に該当するか確認する
                    if (eucCodePage == CodePageEucTw && !CP950_Detection())
                    {
                        // 両方に該当 → CP950 を優先
                        encInfo.CodePage = CodePageCp950;
                        encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                        encInfo.Bom = false;
                        encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                        encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                        return encInfo;
                    }

                    encInfo.CodePage = eucCodePage;
                    encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                    encInfo.Bom = false;
                    encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);

                    return encInfo;
                }

                // CPxxx 判定（カルチャー別）
                int cpxxxCodePage;
                outOfSpecification = CPxxx_Detection(out cpxxxCodePage);

                if (outOfSpecification == false)
                {
                    encInfo.CodePage = cpxxxCodePage;
                    encInfo.EncodingWebName = EncodingName(encInfo.CodePage);
                    encInfo.Bom = false;
                    encInfo.PSEncodingName = EncodingDetector.PSEncodingName(encInfo.CodePage, encInfo.Bom);
                    encInfo.UsePSName = EncodingDetector.SetUsePSName(encInfo.PSEncodingName);
                    return encInfo;
                }
            }

            // 不明
            return encInfo;
        }

        /// <summary>
        /// 現在のカルチャーが中国簡体字かどうかを判定
        /// </summary>
        /// <returns>true=中国簡体字、false=それ以外</returns>
        private bool IsChineseSimplifiedCulture()
        {
            try
            {
                if (_cultureName == null)
                {
                    CultureInfo currentCulture = CultureInfo.CurrentCulture;
                    _cultureName = currentCulture.Name;
                }

                // 中国語（簡体字）のカルチャー
                if (_cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                    _cultureName.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
                    _cultureName.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// ISO-2022 (JP/KR/CN/TW) 又はASCIIであるか判定する
        /// </summary>
        /// <param name="isISO2022">true=ISO-2022である(出力引数)</param>
        /// <param name="codePage">判別した文字エンコーディングのコードページ（判別できない場合は-1）</param>
        /// <returns>true=ISO-2022又はASCIIでは無い</returns>
        private bool ISO2022_Detection(out bool isISO2022, out int codePage)
        {
            codePage = -1;
            isISO2022 = false;
            bool outOfSpecification = false;    //規格外フラグ
            bool escExist = false;  //ESCシーケンス存在フラグ
            
            // 各ISO-2022バリアントの特徴的なESCシーケンスをスコアリング
            int jpScore = 0;   // ISO-2022-JP
            int krScore = 0;   // ISO-2022-KR
            int cnScore = 0;   // ISO-2022-CN
            int twScore = 0;   // ISO-2022-TW
            
            // 共通のESCシーケンス一覧
            byte[][] commonESC = new byte[][] {
                new byte[] { 0x1B, 0x28, 0x42 }, //ASCII
                new byte[] { 0x1B, 0x2E, 0x41 }, //ISO-8859-1（G2指示）
                new byte[] { 0x1B, 0x2E, 0x46 }  //ISO-8859-7（ギリシャ文字、G2指示）
            };
            
            // ISO-2022-JP 特有のESCシーケンス
            byte[][] jpESC = new byte[][] {
                new byte[] { 0x1B, 0x24, 0x42 },       //JIS X 0208-1983（新JIS）
                new byte[] { 0x1B, 0x24, 0x40 },       //JIS X 0208-1978（旧JIS）
                new byte[] { 0x1B, 0x28, 0x4A },       //JIS X 0201 ローマ字
                new byte[] { 0x1B, 0x24, 0x28, 0x44 }, //JIS X 0212-1990（補助漢字）
                new byte[] { 0x1B, 0x24, 0x28, 0x4F }, //JIS X 0213:2000 第1面
                new byte[] { 0x1B, 0x24, 0x28, 0x50 }, //JIS X 0213:2004 第1面
                new byte[] { 0x1B, 0x24, 0x28, 0x51 }, //JIS X 0213 第2面
                new byte[] { 0x1B, 0x28, 0x49 }        //JIS X 0201 カタカナ
            };
            
            // ISO-2022-KR 特有のESCシーケンス
            byte[][] krESC = new byte[][] {
                new byte[] { 0x1B, 0x24, 0x29, 0x43 }  //KS X 1001（韓国語）
            };
            
            // ISO-2022-CN 特有のESCシーケンス
            byte[][] cnESC = new byte[][] {
                new byte[] { 0x1B, 0x24, 0x29, 0x41 }, //GB2312（中国語簡体字）
                new byte[] { 0x1B, 0x24, 0x41 },       //GB2312（中国語簡体字、別形式）
                new byte[] { 0x1B, 0x24, 0x29, 0x47 }, //CNS 11643-1992 Plane 1
                new byte[] { 0x1B, 0x24, 0x2A, 0x48 }  //CNS 11643-1992 Plane 2
            };
            
            // ISO-2022-TW 特有のESCシーケンス
            byte[][] twESC = new byte[][] {
                new byte[] { 0x1B, 0x24, 0x29, 0x47 }, //CNS 11643-1992 Plane 1
                new byte[] { 0x1B, 0x24, 0x2A, 0x48 }, //CNS 11643-1992 Plane 2
                new byte[] { 0x1B, 0x24, 0x2B, 0x49 }, //CNS 11643-1992 Plane 3
                new byte[] { 0x1B, 0x24, 0x2B, 0x4A }, //CNS 11643-1992 Plane 4
                new byte[] { 0x1B, 0x24, 0x2B, 0x4B }, //CNS 11643-1992 Plane 5
                new byte[] { 0x1B, 0x24, 0x2B, 0x4C }, //CNS 11643-1992 Plane 6
                new byte[] { 0x1B, 0x24, 0x2B, 0x4D }  //CNS 11643-1992 Plane 7
            };
            
            byte[] backESC = { 0, 0, 0, 0 };    //直近4バイト保存バッファ
            bool hasShiftFunction = false;      // SO/SI の存在（KR/CN/TW用）

            // ISO-2022はすべて7bitのみ

            for (int i = 0; i < this.BufferSize; i++)
            {
                if (0x80 <= this._buffer[i])
                {
                    outOfSpecification = true;
                    break;
                }
                
                // SO (Shift Out: 0x0E) / SI (Shift In: 0x0F) の検出
                if (this._buffer[i] == 0x0E || this._buffer[i] == 0x0F)
                {
                    hasShiftFunction = true;
                    // KR, CN, TW で使用される
                    krScore += 5;
                    cnScore += 3;
                    twScore += 3;
                }
                
                //直近4バイト保存
                backESC[0] = backESC[1];
                backESC[1] = backESC[2];
                backESC[2] = backESC[3];
                backESC[3] = this._buffer[i];

                //ESCシーケンス照合
                
                // 共通ESCシーケンス
                for (int j = 0; j < commonESC.Length; j++)
                {
                    if (IsMatched(backESC, commonESC[j]))
                    {
                        escExist = true;
                        break;
                    }
                }
                
                // ISO-2022-JP
                for (int j = 0; j < jpESC.Length; j++)
                {
                    if (IsMatched(backESC, jpESC[j]))
                    {
                        escExist = true;
                        jpScore += 10;
                        break;
                    }
                }
                
                // ISO-2022-KR
                for (int j = 0; j < krESC.Length; j++)
                {
                    if (IsMatched(backESC, krESC[j]))
                    {
                        escExist = true;
                        krScore += 10;
                        break;
                    }
                }
                
                // ISO-2022-CN
                for (int j = 0; j < cnESC.Length; j++)
                {
                    if (IsMatched(backESC, cnESC[j]))
                    {
                        escExist = true;
                        cnScore += 10;
                        break;
                    }
                }
                
                // ISO-2022-TW
                for (int j = 0; j < twESC.Length; j++)
                {
                    if (IsMatched(backESC, twESC[j]))
                    {
                        escExist = true;
                        twScore += 10;
                        break;
                    }
                }
            }

            if (escExist)
            {
                isISO2022 = true;
                
                // 最高スコアのバリアントを選択
                int maxScore = Math.Max(Math.Max(jpScore, krScore), Math.Max(cnScore, twScore));
                
                if (maxScore == jpScore)
                {
                    codePage = CodePageJis;
                }
                else if (maxScore == krScore)
                {
                    codePage = CodePageIso2022Kr;
                }
                else if (maxScore == cnScore)
                {
                    codePage = CodePageIso2022Cn;
                }
                else if (maxScore == twScore)
                {
                    codePage = CodePageIso2022Tw;
                }
                else
                {
                    // スコアがすべて0の場合（共通ESCのみ）、デフォルトでJPを選択
                    codePage = CodePageJis;
                }
            }
            else
            {
                isISO2022 = false;
            }

            return outOfSpecification;
        }

        /// <summary>
        /// UTF-16であるか判定する
        /// </summary>
        /// <param name="isLittleEndian">true=Little Endian、false=Big Endian(出力引数)</param>
        /// <returns>true=UTF-16では無い</returns>
        private bool Utf16_Detection(out bool isLittleEndian)
        {
            isLittleEndian = true;
            bool outOfSpecification = false;

            // ファイルサイズが奇数の場合は対象外
            if (this.BufferSize % 2 != 0)
            {
                return true;
            }

            // ファイルサイズが0または2バイト未満の場合は判定不可
            if (this.BufferSize < 2)
            {
                return true;
            }

            // LE/BEのヒューリスティック判定用カウンタ
            int leScore = 0;
            int beScore = 0;

            // サロゲートペア状態管理
            bool expectLowSurrogate = false;
            ushort highSurrogate = 0;

            // 2バイト単位で読み取り
            for (int i = 0; i < this.BufferSize; i += 2)
            {
                // Little Endian として読み取り
                ushort valueLe = (ushort)(this._buffer[i] | (this._buffer[i + 1] << 8));
                // Big Endian として読み取り
                ushort valueBe = (ushort)((this._buffer[i] << 8) | this._buffer[i + 1]);

                // まずLEとして検証
                if (IsValidUtf16CodeUnit(valueLe, ref expectLowSurrogate, ref highSurrogate, out bool isLeValid))
                {
                    leScore++;
                    // ASCII範囲の文字はスコアを加算（テキストファイルらしさ）
                    if (valueLe < 0x80)
                    {
                        leScore++;
                    }
                }
                else if (!isLeValid)
                {
                    // 明らかに不正な並び
                    leScore = -1000;
                }

                // リセットしてBEとして検証
                expectLowSurrogate = false;
                highSurrogate = 0;

                if (IsValidUtf16CodeUnit(valueBe, ref expectLowSurrogate, ref highSurrogate, out bool isBeValid))
                {
                    beScore++;
                    if (valueBe < 0x80)
                    {
                        beScore++;
                    }
                }
                else if (!isBeValid)
                {
                    beScore = -1000;
                }
            }

            // 最後に高サロゲートが残っていたら不正
            if (expectLowSurrogate)
            {
                outOfSpecification = true;
            }

            // スコアが低すぎる場合はUTF-16ではない
            if (leScore < 0 && beScore < 0)
            {
                outOfSpecification = true;
            }

            // NULLバイトパターンチェックによるUTF-16判定の強化
            if (!outOfSpecification)
            {
                int leNullPattern = 0;
                int beNullPattern = 0;

                for (int i = 0; i < this.BufferSize; i++)
                {
                    byte currentByte = this._buffer[i];
                    
                    // ASCII範囲のバイトをチェック
                    if (IsAsciiRangeByte(currentByte))
                    {
                        // UTF-16LE パターン: XX 00
                        if (i % 2 == 0 && i + 1 < this.BufferSize && this._buffer[i + 1] == 0x00)
                        {
                            leNullPattern++;
                        }
                        
                        // UTF-16BE パターン: 00 XX
                        if (i % 2 == 1 && i - 1 >= 0 && this._buffer[i - 1] == 0x00)
                        {
                            beNullPattern++;
                        }
                    }
                }

                // NULLパターンが見つからない場合はUTF-16ではない
                if (leNullPattern == 0 && beNullPattern == 0)
                {
                    outOfSpecification = true;
                }
                else
                {
                    // NULLパターンに基づいてエンディアンを判定
                    isLittleEndian = (leNullPattern >= beNullPattern);
                }
            }
            else if (!outOfSpecification)
            {
                // スコアのみでエンディアン判定
                isLittleEndian = (leScore >= beScore);
            }

            return outOfSpecification;
        }

        /// <summary>
        /// UTF-16コードユニットの妥当性を検証
        /// </summary>
        /// <param name="value">検証するコードユニット</param>
        /// <param name="expectLowSurrogate">低サロゲート待ちフラグ</param>
        /// <param name="highSurrogate">保持中の高サロゲート</param>
        /// <param name="isValid">明らかに不正かどうか</param>
        /// <returns>true=妥当、false=不正の可能性</returns>
        private bool IsValidUtf16CodeUnit(ushort value, ref bool expectLowSurrogate, ref ushort highSurrogate, out bool isValid)
        {
            isValid = true;

            // 非文字（Noncharacters）のチェック
            if (value == 0xFFFE || value == 0xFFFF || (value >= 0xFDD0 && value <= 0xFDEF))
            {
                isValid = false;
                return false;
            }

            // 高サロゲート (D800-DBFF)
            if (value >= 0xD800 && value <= 0xDBFF)
            {
                if (expectLowSurrogate)
                {
                    // 高サロゲートの後に高サロゲート → 不正
                    isValid = false;
                    return false;
                }
                expectLowSurrogate = true;
                highSurrogate = value;
                return true;
            }

            // 低サロゲート (DC00-DFFF)
            if (value >= 0xDC00 && value <= 0xDFFF)
            {
                if (!expectLowSurrogate)
                {
                    // 低サロゲートが単独で出現 → 不正
                    isValid = false;
                    return false;
                }
                // 正しいサロゲートペア
                expectLowSurrogate = false;
                highSurrogate = 0;
                return true;
            }

            // BMP文字
            if (expectLowSurrogate)
            {
                // 高サロゲートの後に非サロゲート → 不正
                isValid = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// UTF-32であるか判定する
        /// </summary>
        /// <param name="isLittleEndian">true=Little Endian、false=Big Endian(出力引数)</param>
        /// <returns>true=UTF-32では無い</returns>
        private bool Utf32_Detection(out bool isLittleEndian)
        {
            isLittleEndian = true;
            bool outOfSpecification = false;

            // ファイルサイズが4の倍数でない場合は対象外
            if (this.BufferSize % 4 != 0)
            {
                return true;
            }

            // ファイルサイズが0または4バイト未満の場合は判定不可
            if (this.BufferSize < 4)
            {
                return true;
            }

            // LE/BEのヒューリスティック判定用カウンタ
            int leScore = 0;
            int beScore = 0;

            // 4バイト単位で読み取り
            for (int i = 0; i < this.BufferSize; i += 4)
            {
                // Little Endian として読み取り
                uint valueLe = (uint)(this._buffer[i] | 
                                     (this._buffer[i + 1] << 8) | 
                                     (this._buffer[i + 2] << 16) | 
                                     (this._buffer[i + 3] << 24));
                
                // Big Endian として読み取り
                uint valueBe = (uint)((this._buffer[i] << 24) | 
                                     (this._buffer[i + 1] << 16) | 
                                     (this._buffer[i + 2] << 8) | 
                                     this._buffer[i + 3]);

                // LEとして検証
                if (IsValidUtf32CodePoint(valueLe))
                {
                    leScore++;
                    // ASCII範囲の文字はスコアを加算
                    if (valueLe < 0x80)
                    {
                        leScore++;
                    }
                }
                else
                {
                    leScore = -1000;
                }

                // BEとして検証
                if (IsValidUtf32CodePoint(valueBe))
                {
                    beScore++;
                    if (valueBe < 0x80)
                    {
                        beScore++;
                    }
                }
                else
                {
                    beScore = -1000;
                }
            }

            // スコアが低すぎる場合はUTF-32ではない
            if (leScore < 0 && beScore < 0)
            {
                outOfSpecification = true;
            }

            // NULLバイトパターンチェックによるUTF-32判定の強化
            if (!outOfSpecification)
            {
                int leNullPattern = 0;
                int beNullPattern = 0;

                for (int i = 0; i < this.BufferSize; i++)
                {
                    byte currentByte = this._buffer[i];
                    
                    // ASCII範囲のバイトをチェック
                    if (IsAsciiRangeByte(currentByte))
                    {
                        // UTF-32LE パターン: XX 00 00 00
                        if (i % 4 == 0 && i + 3 < this.BufferSize &&
                            this._buffer[i + 1] == 0x00 &&
                            this._buffer[i + 2] == 0x00 &&
                            this._buffer[i + 3] == 0x00)
                        {
                            leNullPattern++;
                        }
                        
                        // UTF-32BE パターン: 00 00 00 XX
                        if (i % 4 == 3 && i - 3 >= 0 &&
                            this._buffer[i - 3] == 0x00 &&
                            this._buffer[i - 2] == 0x00 &&
                            this._buffer[i - 1] == 0x00)
                        {
                            beNullPattern++;
                        }
                    }
                }

                // NULLパターンが見つからない場合はUTF-32ではない
                if (leNullPattern == 0 && beNullPattern == 0)
                {
                    outOfSpecification = true;
                }
                else
                {
                    // NULLパターンに基づいてエンディアンを判定
                    isLittleEndian = (leNullPattern >= beNullPattern);
                }
            }
            else if (!outOfSpecification)
            {
                // スコアのみでエンディアン判定
                isLittleEndian = (leScore >= beScore);
            }

            return outOfSpecification;
        }

        /// <summary>
        /// UTF-32コードポイントの妥当性を検証
        /// </summary>
        /// <param name="value">検証するコードポイント</param>
        /// <returns>true=妥当、false=不正</returns>
        private bool IsValidUtf32CodePoint(uint value)
        {
            // Unicodeの最大値を超える
            if (value > 0x10FFFF)
            {
                return false;
            }

            // サロゲート範囲 (D800-DFFF) はUTF-32では使用禁止
            if (value >= 0xD800 && value <= 0xDFFF)
            {
                return false;
            }

            // 非文字（Noncharacters）
            if ((value & 0xFFFE) == 0xFFFE) // nFFFE, nFFFF
            {
                return false;
            }

            if (value >= 0xFDD0 && value <= 0xFDEF)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// ASCII範囲のバイトかどうかを判定
        /// </summary>
        /// <param name="b">判定するバイト</param>
        /// <returns>true=ASCII範囲のバイト、false=それ以外</returns>
        private bool IsAsciiRangeByte(byte b)
        {
            // 0x20-0x7E: 印字可能文字
            // 0x09: タブ
            // 0x0A: LF
            // 0x0D: CR
            return (b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D;
        }

        /// <summary>
        /// UTF8であるか判定する
        /// </summary>
        /// <returns>true=UTF8では無い</returns>
        private bool Utf8_Detection()
        {
            bool outOfSpecification;

            outOfSpecification = false;
            uint[] byteChar = new uint[6];
            int byteCharCount = 0;

            for (int i = 0; i < this.BufferSize; i++)
            {
                //２バイト文字以上である
                if ((uint)0x80 <= (uint)this._buffer[i])
                {
                    //２バイト文字
                    uint char2byte = (uint)0b11100000 & (uint)this._buffer[i];
                    if (char2byte == 0b11000000)
                    {
                        //セカンドコード数が規格より少なければ規格外
                        outOfSpecification = Utf8OutOfSpecification(byteChar[0], byteCharCount, false);
                        if (outOfSpecification)
                        {
                            break;
                        }

                        byteChar[0] = char2byte;
                        byteCharCount = 1;
                        continue;
                    }

                    //3バイト文字
                    uint char3byte = (uint)0b11110000 & (uint)this._buffer[i];
                    if (char3byte == 0b11100000)
                    {
                        //セカンドコード数が規格より少なければ規格外
                        outOfSpecification = Utf8OutOfSpecification(byteChar[0], byteCharCount, false);
                        if (outOfSpecification)
                        {
                            break;
                        }

                        byteChar[0] = char3byte;
                        byteCharCount = 1;
                        continue;
                    }

                    //4バイト文字
                    uint char4byte = (uint)0b11111000 & (uint)this._buffer[i];
                    if (char4byte == 0b11110000)
                    {
                        //セカンドコード数が規格より少なければ規格外
                        outOfSpecification = Utf8OutOfSpecification(byteChar[0], byteCharCount, false);
                        if (outOfSpecification)
                        {
                            break;
                        }

                        byteChar[0] = char4byte;
                        byteCharCount = 1;
                        continue;
                    }

                    //２バイト目以降のコード
                    uint charSecond = (uint)0b11000000 & (uint)this._buffer[i];
                    if (charSecond == 0b10000000)
                    {
                        // 文字の先頭がセカンドコードなら規格外
                        if (byteCharCount < 1)
                        {
                            outOfSpecification = true;
                            break;
                        }
                        // 1文字が4バイトを超えるなら規格外
                        if (4 < byteCharCount)
                        {
                            outOfSpecification = true;
                            break;
                        }

                        //セカンドコードを保存
                        byteChar[byteCharCount] = charSecond;
                        byteCharCount++;

                        //セカンドコード数が規格より多ければ規格外
                        outOfSpecification = Utf8OutOfSpecification(byteChar[0], byteCharCount, true);
                        if (outOfSpecification)
                        {
                            break;
                        }

                        continue;
                    }

                    //どれにも当てはまらない
                    outOfSpecification = true;
                    break;
                }
                else
                {
                    // 7bit文字
                    byteChar[0] = 0;
                    byteChar[1] = 0;
                    byteChar[2] = 0;
                    byteChar[3] = 0;
                    byteChar[4] = 0;
                    byteChar[5] = 0;
                    byteCharCount = 0;
                }
            }

            return outOfSpecification;
        }

        /// <summary>
        /// UTF8セカンドコード規格判定
        /// </summary>
        /// <param name="topByteChar"></param>
        /// <param name="byteCharCount"></param>
        /// <param name="checkBig"></param>
        /// <returns>true=UTF-8では無い</returns>
        private bool Utf8OutOfSpecification(uint topByteChar, int byteCharCount, bool checkBig)
        {
            bool outOfSpecification = false;

            //セカンドコード数が規格より多ければ規格外
            if (topByteChar == 0b11000000)
            {
                if (checkBig == true)
                {
                    if (byteCharCount > 2) outOfSpecification = true;
                }
                else
                {
                    if (byteCharCount < 2) outOfSpecification = true;
                }
            }
            else if (topByteChar == 0b11100000)
            {
                if (checkBig == true)
                {
                    if (byteCharCount > 3) outOfSpecification = true;
                }
                else
                {
                    if (byteCharCount < 3) outOfSpecification = true;
                }
            }
            else if (topByteChar == 0b11110000)
            {
                if (checkBig == true)
                {
                    if (byteCharCount > 4) outOfSpecification = true;
                }
                else
                {
                    if (byteCharCount < 4) outOfSpecification = true;
                }
            }

            return outOfSpecification;
        }

        /// <summary>
        /// EUCバイト種別
        /// </summary>
        private enum BYTECODE : byte { OneByteCode, TwoByteCode, CodeSet2, CodeSet3 }

        /// <summary>
        /// EUC-JP/KR/CN/TWであるか判定する（カルチャー別）
        /// </summary>
        /// <param name="codePage">判別した文字エンコーディングのコードページ（判別できない場合は-1）</param>
        /// <returns>true=EUCでは無い</returns>
        private bool EUCxx_Detection(out int codePage)
        {
            // カルチャー情報から判定対象のEUCを決定
            int targetEucCodePage = GetEucCodePageFromCulture();
            
            // カルチャーに対応するEUCが無い場合は判定スキップ
            if (targetEucCodePage == -1)
            {
                codePage = -1;
                return true;
            }
            
            // 対象のEUCで判定
            bool outOfSpecification = false;
            codePage = -1;

            BYTECODE beforeCode = BYTECODE.OneByteCode;
            int byteCharCount = 0;

            for (int i = 0; i < this.BufferSize; i++)
            {
                byte currentByte = this._buffer[i];

                // 0x8E (SS2) の検出
                if (currentByte == 0x8E)
                {
                    if (i + 1 < this.BufferSize)
                    {
                        byte nextByte = this._buffer[i + 1];
                        
                        // EUC-TW: 0x8E + 0xA1〜0xB0 + 0xA1〜0xFE + 0xA1〜0xFE (4バイト構造)
                        if (targetEucCodePage == CodePageEucTw)
                        {
                            if (nextByte >= 0xA1 && nextByte <= 0xB0 && i + 3 < this.BufferSize)
                            {
                                byte byte3 = this._buffer[i + 2];
                                byte byte4 = this._buffer[i + 3];
                                if (byte3 >= 0xA1 && byte3 <= 0xFE && byte4 >= 0xA1 && byte4 <= 0xFE)
                                {
                                    i += 3; // 4バイト分スキップ
                                    beforeCode = BYTECODE.OneByteCode;
                                    byteCharCount = 0;
                                    continue;
                                }
                            }
                        }
                        
                        // EUC-JP: 0x8E + 0xA1〜0xFE (半角カナ)
                        if (targetEucCodePage == CodePageEucJp)
                        {
                            if (nextByte >= 0xA1 && nextByte <= 0xFE)
                            {
                                i += 1; // 2バイト分スキップ
                                beforeCode = BYTECODE.OneByteCode;
                                byteCharCount = 0;
                                continue;
                            }
                        }
                    }

                    // 例：EUC-TW 用の追加チェック
                    if (targetEucCodePage == CodePageEucTw ||  // ← EUC-TW も無効な SS2 は規格外
                        targetEucCodePage == CodePageEucKr || targetEucCodePage == CodePageEucCn ||
                        i + 1 >= this.BufferSize ||
                        (this._buffer[i + 1] < 0xA1 || this._buffer[i + 1] > 0xFE))
                    {
                        outOfSpecification = true;
                        break;
                    }
                }
                
                // 0x8F (SS3) の検出 - EUC-JP専用
                if (currentByte == 0x8F)
                {
                    if (targetEucCodePage == CodePageEucJp)
                    {
                        if (i + 2 < this.BufferSize)
                        {
                            byte byte2 = this._buffer[i + 1];
                            byte byte3 = this._buffer[i + 2];
                            if (byte2 >= 0xA1 && byte2 <= 0xFE && byte3 >= 0xA1 && byte3 <= 0xFE)
                            {
                                i += 2; // 3バイト分スキップ
                                beforeCode = BYTECODE.OneByteCode;
                                byteCharCount = 0;
                                continue;
                            }
                        }
                        
                        // 0x8Fの後に適切なバイトが続かない場合は規格外
                        if (i + 2 >= this.BufferSize || 
                            this._buffer[i + 1] < 0xA1 || this._buffer[i + 1] > 0xFE ||
                            this._buffer[i + 2] < 0xA1 || this._buffer[i + 2] > 0xFE)
                        {
                            outOfSpecification = true;
                            break;
                        }
                    }
                    else
                    {
                        // EUC-JP以外で0x8Fが出現したら規格外
                        outOfSpecification = true;
                        break;
                    }
                }

                // 2バイトコード (0xA1〜0xFE)
                if (0xA1 <= currentByte && currentByte <= 0xFE)
                {
                    if (beforeCode == BYTECODE.TwoByteCode)
                    {
                        if (byteCharCount == 1)
                            byteCharCount = 2;
                        else if (byteCharCount == 2)
                            byteCharCount = 1;
                    }
                    else
                    {
                        byteCharCount = 1;
                    }

                    // EUC-CN (GB2312): 第1バイトの有効範囲は 0xA1～0xF7
                    // byteCharCount == 1 のとき、現在のバイトが2バイト文字の第1バイト
                    if (byteCharCount == 1 && targetEucCodePage == CodePageEucCn && currentByte > 0xF7)
                    {
                        outOfSpecification = true;
                        break;
                    }

                    beforeCode = BYTECODE.TwoByteCode;
                }
                // 1バイトコード (ASCII)
                else if (currentByte <= 0x7F)
                {
                    if (beforeCode == BYTECODE.TwoByteCode && byteCharCount == 1)
                    {
                        outOfSpecification = true;
                        break;
                    }

                    beforeCode = BYTECODE.OneByteCode;
                    byteCharCount = 1;
                }
                // 0x80〜0x8D, 0x90〜0x9F, 0xFF など不正なバイト
                else if (currentByte != 0x8E && currentByte != 0x8F)
                {
                    outOfSpecification = true;
                    break;
                }
            }

            // 規格外の場合は終了
            if (outOfSpecification)
            {
                return true;
            }

            // 判定成功
            codePage = targetEucCodePage;
            return false;
        }

        /// <summary>
        /// カルチャー情報から対象となるEUCコードページを取得
        /// </summary>
        /// <returns>EUCコードページ（該当なしの場合は-1）</returns>
        private int GetEucCodePageFromCulture()
        {
            try
            {
                string cultureName = _cultureName ?? CultureInfo.CurrentCulture.Name;

                // カルチャー名から判定
                // 日本語 -> EUC-JP
                if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                {
                    return CodePageEucJp;
                }
                // 韓国語 -> EUC-KR
                else if (cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                {
                    return CodePageEucKr;
                }
                // 中国語（簡体字） -> EUC-CN
                else if (cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
                {
                    return CodePageEucCn;
                }
                // 中国語（繁体字・台湾・香港） -> EUC-TW
                else if (cultureName.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-MO", StringComparison.OrdinalIgnoreCase))
                {
                    return CodePageEucTw;
                }
                
                // 該当なし
                return -1;
            }
            catch
            {
                // エラーが発生した場合は判定不可
                return -1;
            }
        }

        /// <summary>
        /// Shift-JISバイト種別
        /// </summary>
        private enum SJIS_BYTECODE : byte { OneByteCode, TwoByteBefore, TwoByteAfter, KanaOneByte, OutOfSpec, Unknown }

        /// <summary>
        /// Shift-JIS であるか判定する
        /// </summary>
        /// <returns>true=Shift-JISでは無い</returns>
        private bool SJIS_Detection()
        {
            bool outOfSpecification = false; ;
            SJIS_BYTECODE beforeSjisByte = SJIS_BYTECODE.OneByteCode;
            SJIS_BYTECODE sjisByte = SJIS_BYTECODE.OneByteCode;

            // if SJIS

            for (int i = 0; i < this.BufferSize; i++)
            {
                // バイト種別判定
                if (beforeSjisByte != SJIS_BYTECODE.TwoByteBefore
                    && this._buffer[i] <= 0x7F )
                {
                    sjisByte = SJIS_BYTECODE.OneByteCode;
                }
                else if (
                    (beforeSjisByte != SJIS_BYTECODE.TwoByteBefore)
                    && (0xA1 <= this._buffer[i] && this._buffer[i] <= 0xDF)
                    )
                {
                    sjisByte = SJIS_BYTECODE.KanaOneByte;
                }
                else if ((beforeSjisByte != SJIS_BYTECODE.TwoByteBefore) 
                    && (0x81 <= this._buffer[i] && this._buffer[i] <= 0x9F)
                    )
                {
                    sjisByte = SJIS_BYTECODE.TwoByteBefore;
                }
                else if ((beforeSjisByte != SJIS_BYTECODE.TwoByteBefore)
                    && (0xE0 <= this._buffer[i] && this._buffer[i] <= 0xFC)
                    )
                {
                    sjisByte = SJIS_BYTECODE.TwoByteBefore;
                }
                else if ((beforeSjisByte == SJIS_BYTECODE.TwoByteBefore)
                        && (0x40 <= this._buffer[i] && this._buffer[i] <= 0x7E)
                        )
                {
                    sjisByte = SJIS_BYTECODE.TwoByteAfter;
                }
                else if ((beforeSjisByte == SJIS_BYTECODE.TwoByteBefore)
                        && (0x80 <= this._buffer[i] && this._buffer[i] <= 0xFC)
                        )
                {
                    sjisByte = SJIS_BYTECODE.TwoByteAfter;
                }
                else if (this._buffer[i] == 0x80 || this._buffer[i] == 0xA0)
                {
                    sjisByte = SJIS_BYTECODE.OutOfSpec;
                }
                else if (0xFD <= this._buffer[i]  && this._buffer[i] <= 0xFF)
                {
                    sjisByte = SJIS_BYTECODE.OutOfSpec;
                }
                else if (this._buffer[i] == 0x7F)
                {
                    sjisByte = SJIS_BYTECODE.OutOfSpec;
                }
                else
                {
                    sjisByte = SJIS_BYTECODE.Unknown;
                    //Console.WriteLine("SJIS Spec Unkown byte found : 0x{0:X2}", this._buffer[i]);
                }

                // バイト種別規格判定
                if(sjisByte == SJIS_BYTECODE.OutOfSpec)
                {
                    outOfSpecification = true;
                    break;
                }

                if (sjisByte == SJIS_BYTECODE.TwoByteBefore)
                {
                    if(i == (this.BufferSize - 1))
                    {
                        // 最後のバイトで2バイト文字の前半が来たら規格外
                        outOfSpecification = true;
                        break;
                    }
                }
                
                if (beforeSjisByte == SJIS_BYTECODE.TwoByteBefore)
                {
                    // 直前が2バイト文字の前半の場合
                    if (sjisByte != SJIS_BYTECODE.TwoByteAfter)
                    {
                        outOfSpecification = true;
                        break;
                    }
                }

                // 一つ前のバイト種別の保存
                beforeSjisByte = sjisByte;
            }

            return outOfSpecification;
        }

        /// <summary>
        /// カルチャー別のCPxxx判定
        /// </summary>
        /// <param name="codePage">判別した文字エンコーディングのコードページ（判別できない場合は-1）</param>
        /// <returns>true=CPxxxでは無い</returns>
        private bool CPxxx_Detection(out int codePage)
        {
            codePage = -1;
            bool outOfSpecification = true;
            
            try
            {
                string cultureName = _cultureName ?? CultureInfo.CurrentCulture.Name;

                // カルチャー別に対応するCPxxxを判定
                if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                {
                    // 日本 -> CP932 (Shift_JIS)
                    outOfSpecification = SJIS_Detection();
                    if (!outOfSpecification)
                    {
                        codePage = CodePageShiftJis;
                    }
                }
                else if (cultureName.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                {
                    // 韓国 -> CP949
                    outOfSpecification = CP949_Detection();
                    if (!outOfSpecification)
                    {
                        codePage = CodePageCp949;
                    }
                }
                else if (cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-SG", StringComparison.OrdinalIgnoreCase))
                {
                    // 中国（簡体字） -> GB2312 / GB18030
                    outOfSpecification = GB_Detection(out codePage);
                }
                else if (cultureName.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
                {
                    // 台湾（繁体字） -> CP950
                    outOfSpecification = CP950_Detection();
                    if (!outOfSpecification)
                    {
                        codePage = CodePageCp950;
                    }
                }
                else if (cultureName.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
                         cultureName.Equals("zh-MO", StringComparison.OrdinalIgnoreCase))
                {
                    // 香港（繁体字） -> CP950
                    outOfSpecification = CP950_Detection();
                    if (!outOfSpecification)
                    {
                        codePage = CodePageCp950;
                    }
                }
                else
                {
                    // 該当するカルチャーなし
                    outOfSpecification = true;
                }
            }
            catch
            {
                outOfSpecification = true;
            }

            return outOfSpecification;
        }

        /// <summary>
        /// CP949 (韓国) バイト種別
        /// </summary>
        private enum CP949_BYTECODE : byte { OneByteCode, TwoByteBefore, TwoByteAfter, OutOfSpec, Unknown }

        /// <summary>
        /// CP949 (韓国) であるか判定する
        /// </summary>
        /// <returns>true=CP949では無い</returns>
        private bool CP949_Detection()
        {
            bool outOfSpecification = false;
            CP949_BYTECODE beforeByte = CP949_BYTECODE.OneByteCode;
            CP949_BYTECODE currentByteType = CP949_BYTECODE.OneByteCode;

            for (int i = 0; i < this.BufferSize; i++)
            {
                byte b = this._buffer[i];

                // バイト種別判定
                if (beforeByte != CP949_BYTECODE.TwoByteBefore && b <= 0x7F)
                {
                    // ASCII範囲
                    currentByteType = CP949_BYTECODE.OneByteCode;
                }
                else if (beforeByte != CP949_BYTECODE.TwoByteBefore && b >= 0x81 && b <= 0xFE)
                {
                    // 2バイト文字の1バイト目
                    currentByteType = CP949_BYTECODE.TwoByteBefore;
                }
                else if (beforeByte == CP949_BYTECODE.TwoByteBefore &&
                        ((b >= 0x41 && b <= 0x5A) || (b >= 0x61 && b <= 0x7A) || (b >= 0x81 && b <= 0xFE)))
                {
                    // 2バイト文字の2バイト目
                    currentByteType = CP949_BYTECODE.TwoByteAfter;
                }
                else if (b == 0x80)
                {
                    // 未定義領域
                    currentByteType = CP949_BYTECODE.OutOfSpec;
                }
                else
                {
                    currentByteType = CP949_BYTECODE.Unknown;
                }

                // バイト種別規格判定
                if (currentByteType == CP949_BYTECODE.OutOfSpec || currentByteType == CP949_BYTECODE.Unknown)
                {
                    outOfSpecification = true;
                    break;
                }

                if (currentByteType == CP949_BYTECODE.TwoByteBefore)
                {
                    if (i == (this.BufferSize - 1))
                    {
                        // 最後のバイトで2バイト文字の前半が来たら規格外
                        outOfSpecification = true;
                        break;
                    }
                }

                if (beforeByte == CP949_BYTECODE.TwoByteBefore)
                {
                    // 直前が2バイト文字の前半の場合
                    if (currentByteType != CP949_BYTECODE.TwoByteAfter)
                    {
                        outOfSpecification = true;
                        break;
                    }
                }

                // 一つ前のバイト種別の保存
                beforeByte = currentByteType;
            }

            return outOfSpecification;
        }

        /// <summary>
        /// CP936/GB18030 (中国簡体字) バイト種別
        /// </summary>
        private enum CP936_BYTECODE : byte { OneByteCode, TwoByteBefore, TwoByteAfter, FourByte1, FourByte2, FourByte3, FourByte4, OutOfSpec, Unknown }

        /// <summary>
        /// CP936 (GBK) / GB18030 (中国) であるか判定する
        /// </summary>
        /// <param name="codePage">CP936 or GB18030</param>
        /// <returns>true=CP936/GB18030では無い</returns>
        private bool CP936_Detection(out int codePage)
        {
            codePage = -1;
            bool outOfSpecification = false;
            CP936_BYTECODE beforeByte = CP936_BYTECODE.OneByteCode;
            CP936_BYTECODE currentByteType = CP936_BYTECODE.OneByteCode;
            bool hasGB18030FourByte = false; // 4バイトシーケンスが検出されたか

            for (int i = 0; i < this.BufferSize; i++)
            {
                byte b = this._buffer[i];

                // バイト種別判定
                if (beforeByte == CP936_BYTECODE.OneByteCode || beforeByte == CP936_BYTECODE.TwoByteAfter || beforeByte == CP936_BYTECODE.FourByte4)
                {
                    if (b <= 0x7F)
                    {
                        // ASCII範囲
                        currentByteType = CP936_BYTECODE.OneByteCode;
                    }
                    else if (b >= 0x81 && b <= 0xFE)
                    {
                        // 2バイト文字または4バイト文字の1バイト目
                        currentByteType = CP936_BYTECODE.TwoByteBefore;
                    }
                    else
                    {
                        currentByteType = CP936_BYTECODE.OutOfSpec;
                    }
                }
                else if (beforeByte == CP936_BYTECODE.TwoByteBefore)
                {
                    if ((b >= 0x40 && b <= 0x7E) || (b >= 0x80 && b <= 0xFE))
                    {
                        // 2バイト文字の2バイト目（GBK）
                        currentByteType = CP936_BYTECODE.TwoByteAfter;
                    }
                    else if (b >= 0x30 && b <= 0x39)
                    {
                        // 4バイト文字の2バイト目（GB18030）
                        currentByteType = CP936_BYTECODE.FourByte2;
                        hasGB18030FourByte = true;
                    }
                    else
                    {
                        currentByteType = CP936_BYTECODE.OutOfSpec;
                    }
                }
                else if (beforeByte == CP936_BYTECODE.FourByte2)
                {
                    if (b >= 0x81 && b <= 0xFE)
                    {
                        // 4バイト文字の3バイト目（GB18030）
                        currentByteType = CP936_BYTECODE.FourByte3;
                    }
                    else
                    {
                        currentByteType = CP936_BYTECODE.OutOfSpec;
                    }
                }
                else if (beforeByte == CP936_BYTECODE.FourByte3)
                {
                    if (b >= 0x30 && b <= 0x39)
                    {
                        // 4バイト文字の4バイト目（GB18030）
                        currentByteType = CP936_BYTECODE.FourByte4;
                    }
                    else
                    {
                        currentByteType = CP936_BYTECODE.OutOfSpec;
                    }
                }
                else
                {
                    currentByteType = CP936_BYTECODE.Unknown;
                }

                // バイト種別規格判定
                if (currentByteType == CP936_BYTECODE.OutOfSpec || currentByteType == CP936_BYTECODE.Unknown)
                {
                    outOfSpecification = true;
                    break;
                }

                // 一つ前のバイト種別の保存
                beforeByte = currentByteType;
            }

            // 最後が未完成の場合
            if (!outOfSpecification)
            {
                if (beforeByte == CP936_BYTECODE.TwoByteBefore ||
                    beforeByte == CP936_BYTECODE.FourByte2 ||
                    beforeByte == CP936_BYTECODE.FourByte3)
                {
                    outOfSpecification = true;
                }
            }

            if (!outOfSpecification)
            {
                // GB18030の4バイトシーケンスが検出された場合はGB18030、そうでなければCP936
                codePage = hasGB18030FourByte ? CodePageGb18030 : CodePageCp936;
            }

            return outOfSpecification;
        }

        /// <summary>
        /// GB2312 / GBK / GB18030 (中国簡体字) であるか判定する（3段階アルゴリズム）
        /// </summary>
        /// <remarks>
        /// Step 1: GB18030 の4バイトシーケンスを検出したら即座に GB18030 と確定する。
        /// Step 2: GB2312 範囲外バイト（1バイト目 0x81–0xA0 または 2バイト目 0x40–0xA0）が
        ///         1つでも存在する場合、GB2312 を候補から除外する。
        /// Step 3: GB2312 除外なし → GB2312（EUC-CN形式, CP51936）確定。
        ///         GB2312 除外あり → 包摂的に GB18030（CP54936）確定。
        ///         GBK と GB18030（2バイト部）はバイト構造上区別不能なため、常に GB18030 を採用する。
        /// </remarks>
        /// <param name="codePage">GB2312=51936 / GB18030=54936（判別できない場合は-1）</param>
        /// <returns>true=GB系ではない（規格外）</returns>
        private bool GB_Detection(out int codePage)
        {
            codePage = -1;

            if (this.BufferSize == 0)
                return true;

            bool gb2312Excluded = false;
            int i = 0;

            while (i < this.BufferSize)
            {
                byte b = this._buffer[i];

                // 1バイト文字（ASCII: 0x00–0x7F）
                if (b <= 0x7F)
                {
                    i++;
                    continue;
                }

                // 0x80 および 0xFF は GBK/GB18030 の有効な1バイト目ではない
                if (b == 0x80 || b > 0xFE)
                    return true;

                // 1バイト目: 0x81–0xFE。次のバイトが必要
                if (i + 1 >= this.BufferSize)
                    return true;

                byte b2 = this._buffer[i + 1];

                // Step 1: GB18030 4バイトシーケンス検出（最優先・決定的）
                // パターン: [0x81–0xFE][0x30–0x39][0x81–0xFE][0x30–0x39]
                if (b2 >= 0x30 && b2 <= 0x39)
                {
                    if (i + 3 >= this.BufferSize)
                        return true;

                    byte b3 = this._buffer[i + 2];
                    byte b4 = this._buffer[i + 3];

                    if (b3 >= 0x81 && b3 <= 0xFE && b4 >= 0x30 && b4 <= 0x39)
                    {
                        // GB18030 の4バイトシーケンスを確定
                        codePage = CodePageGb18030;
                        return false;
                    }

                    return true;
                }

                // 2バイト文字: [0x81–0xFE][0x40–0xFE（0x7F除く）]
                if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFE))
                {
                    // Step 2: GB2312 範囲外バイトの検出（除外判定・決定的）
                    // 1バイト目が 0xA1 未満、または 2バイト目が 0xA1 未満なら GB2312 を除外
                    if (b < 0xA1 || b2 < 0xA1)
                        gb2312Excluded = true;

                    i += 2;
                    continue;
                }

                // 上記以外は規格外
                return true;
            }

            // Step 3: 残存候補の解決
            // gb2312Excluded == false → 全バイトが GB2312 範囲内（GBK との両方に該当）→ GBK を規定値として採用
            // gb2312Excluded == true  → GB2312 範囲外バイト検出 → 包摂的に GB18030 確定
            codePage = gb2312Excluded ? CodePageGb18030 : CodePageCp936;
            return false;
        }

        /// <summary>
        /// CP950 (Big5, 台湾・香港繁体字) バイト種別
        /// </summary>
        private enum CP950_BYTECODE : byte { OneByteCode, TwoByteBefore, TwoByteAfter, OutOfSpec, Unknown }

        /// <summary>
        /// CP950 (Big5, 台湾・香港) であるか判定する
        /// </summary>
        /// <returns>true=CP950では無い</returns>
        private bool CP950_Detection()
        {
            bool outOfSpecification = false;
            CP950_BYTECODE beforeByte = CP950_BYTECODE.OneByteCode;
            CP950_BYTECODE currentByteType = CP950_BYTECODE.OneByteCode;

            for (int i = 0; i < this.BufferSize; i++)
            {
                byte b = this._buffer[i];

                // バイト種別判定
                if (beforeByte != CP950_BYTECODE.TwoByteBefore && b <= 0x7F)
                {
                    // ASCII範囲
                    currentByteType = CP950_BYTECODE.OneByteCode;
                }
                else if (beforeByte != CP950_BYTECODE.TwoByteBefore && b >= 0x81 && b <= 0xFE)
                {
                    // 2バイト文字の1バイト目
                    currentByteType = CP950_BYTECODE.TwoByteBefore;
                }
                else if (beforeByte == CP950_BYTECODE.TwoByteBefore &&
                        ((b >= 0x40 && b <= 0x7E) || (b >= 0x80 && b <= 0xFE)))
                {
                    // 2バイト文字の2バイト目
                    currentByteType = CP950_BYTECODE.TwoByteAfter;
                }
                else if (b == 0x80 || b == 0xFF)
                {
                    // 未定義領域
                    currentByteType = CP950_BYTECODE.OutOfSpec;
                }
                else
                {
                    currentByteType = CP950_BYTECODE.Unknown;
                }

                // バイト種別規格判定
                if (currentByteType == CP950_BYTECODE.OutOfSpec || currentByteType == CP950_BYTECODE.Unknown)
                {
                    outOfSpecification = true;
                    break;
                }

                if (currentByteType == CP950_BYTECODE.TwoByteBefore)
                {
                    if (i == (this.BufferSize - 1))
                    {
                        // 最後のバイトで2バイト文字の前半が来たら規格外
                        outOfSpecification = true;
                        break;
                    }
                }

                if (beforeByte == CP950_BYTECODE.TwoByteBefore)
                {
                    // 直前が2バイト文字の前半の場合
                    if (currentByteType != CP950_BYTECODE.TwoByteAfter)
                    {
                        outOfSpecification = true;
                        break;
                    }
                }

                // 一つ前のバイト種別の保存
                beforeByte = currentByteType;
            }

            return outOfSpecification;
        }

    }
}


