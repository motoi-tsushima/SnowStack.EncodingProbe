using System;
using System.Collections.Generic;
using System.IO;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// ISO-2022 の追記先が、末尾で ASCII 以外の状態のまま終わっているかを調べ、追記の前に書く「ASCII に戻すシーケンス」を求める
    /// （1.3.0 第六次。BUG-0018）。
    /// </summary>
    /// <remarks>
    /// ISO-2022-JP は状態を持つ文字エンコーディングで、RFC 1468 は「行は ASCII の状態で終わる」と定める。
    /// しかし途中で切れたファイルや断片をつないだファイルは、JIS X 0208 の状態（ESC $ B の後ろ）のまま終わることがある。
    /// そこへ ASCII のバイトをそのまま足すと、追記した内容が 2 バイト文字として読まれて失われる（改行も状態を戻さない）。
    /// <br/>
    /// 対象と戻すシーケンス:
    /// <list type="bullet">
    /// <item>ISO-2022-JP（50220・50221・50222）: SO の後に SI が無ければ SI（0x0F）。最後の G0 の指示が ESC ( B 以外なら ESC ( B。この順に書く</item>
    /// <item>ISO-2022-KR（50225）: SO の後に SI が無ければ SI（G0 は常に ASCII）</item>
    /// </list>
    /// G0 の指示は ESC ( F・ESC $ F・ESC $ ( F。G1 の指示（ESC ) F・ESC $ ) F）と、それ以外の ESC は状態に数えない。
    /// ESC ( J（JIS X 0201 ローマ字）と ESC ( I（片仮名）も ASCII 以外として戻す。
    /// <br/>
    /// ISO-2022 では 0x1B・0x0E・0x0F は制御の意味でしか現れない（2 バイト文字は 0x21〜0x7E）ので、末尾から逆向きに探して、
    /// 「最後の G0 の指示」と「最後の SO / SI」が見つかった時点で止める。大きな追記先でも、ふつうは末尾の近くだけを読む。
    /// SO / SI を使わないファイル（.NET の 50220 で書いたものなど）は、SO / SI が無いことを確かめるために先頭まで読む
    /// （追記先から継承する場合は、その前の判定でファイル全体を読んでいるので、同じ程度の費用である）。
    /// </remarks>
    internal static class Iso2022TailState
    {
        /// <summary>逆向きに読むときの 1 回の大きさ</summary>
        private const int BlockSize = 64 * 1024;

        /// <summary>ESC の後ろに必要な最大のバイト数（ESC $ ( F の 3 バイト）</summary>
        private const int LookAhead = 3;

        private const byte Escape = 0x1B;
        private const byte ShiftOut = 0x0E;
        private const byte ShiftIn = 0x0F;

        /// <summary>
        /// 戻すシーケンスを必要とする可能性のあるコードページかどうか
        /// </summary>
        public static bool AppliesTo(int codePage)
            => codePage == 50220 || codePage == 50221 || codePage == 50222 || codePage == 50225;

        /// <summary>
        /// 追記先のファイルを読み、追記の前に書く戻すシーケンスを返す。戻す必要が無ければ空の配列。
        /// </summary>
        /// <param name="path">追記先の実体のパス</param>
        /// <param name="length">追記先の開いたときの長さ（これより後ろは読まない）</param>
        /// <param name="codePage">追記に使うコードページ</param>
        public static byte[] GetResetSequence(string path, long length, int codePage)
        {
            using (var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess))
            {
                return GetResetSequence(stream, length, codePage);
            }
        }

        /// <summary>
        /// ストリームの先頭から <paramref name="length"/> バイトの末尾の状態を調べる（テスト用にストリームを受け取る）。
        /// </summary>
        internal static byte[] GetResetSequence(Stream stream, long length, int codePage)
        {
            if (!AppliesTo(codePage) || length <= 0)
            {
                return Array.Empty<byte>();
            }

            // ISO-2022-KR の G0 は常に ASCII なので、SO / SI だけを見る
            bool g0Found = codePage == 50225;
            bool g0Ascii = true;
            bool shiftFound = false;
            bool shifted = false;

            var buffer = new byte[BlockSize + LookAhead];
            int carried = 0;  // 前に読んだブロック（ファイルでは後ろ）の先頭のバイト数。buffer の n 以降に置く
            long blockEnd = length;

            while (blockEnd > 0 && !(g0Found && shiftFound))
            {
                long start = Math.Max(0, blockEnd - BlockSize);
                int n = (int)(blockEnd - start);

                // 後ろのブロックの先頭（最大 3 バイト）を、このブロックの直後へずらす
                Buffer.BlockCopy(buffer, 0, buffer, n, carried);

                stream.Seek(start, SeekOrigin.Begin);
                ReadExactly(stream, buffer, n);

                int available = n + carried;

                for (int i = n - 1; i >= 0 && !(g0Found && shiftFound); i--)
                {
                    byte b = buffer[i];

                    if (b == ShiftOut || b == ShiftIn)
                    {
                        if (!shiftFound)
                        {
                            shiftFound = true;
                            shifted = b == ShiftOut;
                        }
                    }
                    else if (b == Escape && !g0Found)
                    {
                        bool? ascii = GetG0Designation(buffer, i, available);

                        if (ascii != null)
                        {
                            g0Found = true;
                            g0Ascii = ascii.Value;
                        }
                    }
                }

                carried = Math.Min(LookAhead, n);
                blockEnd = start;
            }

            var reset = new List<byte>(4);

            if (shifted)
            {
                reset.Add(ShiftIn);
            }

            if (!g0Ascii)
            {
                reset.Add(Escape);
                reset.Add((byte)'(');
                reset.Add((byte)'B');
            }

            return reset.ToArray();
        }

        /// <summary>
        /// buffer[index] の ESC が G0 の指示なら、ASCII の指示（ESC ( B）かどうかを返す。G0 の指示でなければ null。
        /// </summary>
        private static bool? GetG0Designation(byte[] buffer, int index, int available)
        {
            int remaining = available - index - 1;

            if (remaining < 2)
            {
                return null;  // ファイルの末尾で途中で切れたエスケープシーケンス
            }

            byte first = buffer[index + 1];
            byte second = buffer[index + 2];

            if (first == (byte)'(')
            {
                return second == (byte)'B';  // ESC ( F: 1 バイトの文字集合を G0 に。ASCII は ESC ( B だけ
            }

            if (first == (byte)'$')
            {
                if (second == (byte)'@' || second == (byte)'A' || second == (byte)'B')
                {
                    return false;  // ESC $ F: 2 バイトの文字集合を G0 に（旧い形）
                }

                if (second == (byte)'(' && remaining >= 3)
                {
                    return false;  // ESC $ ( F: 2 バイトの文字集合を G0 に（F が 'B' でも ASCII ではない）
                }
            }

            return null;  // G1 の指示（ESC ) F・ESC $ ) F）など
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int read = 0;

            while (read < count)
            {
                int n = stream.Read(buffer, read, count - read);

                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }
        }
    }
}
