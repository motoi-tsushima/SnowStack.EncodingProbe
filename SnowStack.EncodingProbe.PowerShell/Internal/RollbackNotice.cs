using System.Management.Automation;

namespace SnowStack.EncodingProbe.PowerShell.Internal
{
    /// <summary>
    /// 元に戻せなかったことを、報告するエラーのメッセージに添える（1.3.0 第五次の後半）。
    /// </summary>
    /// <remarks>
    /// 写してから直接書く方法（<see cref="SafeFileOverwrite"/>。ハードリンク・一時ファイルを作れないフォルダー）で失敗し、
    /// さらに写しから書き戻せなかった場合、元のファイルは書きかけの状態で、無事な内容は一時フォルダーの写しにしか無い。
    /// エラーの ID と例外はそのまま（WriteFailed・UnrepresentableCharacter・EncodingChangeOnAppend など）にし、
    /// 表示されるメッセージ（<see cref="ErrorRecord.ErrorDetails"/>）に、写しのパスと、写しから元に戻す必要があることを足す。
    /// </remarks>
    internal static class RollbackNotice
    {
        /// <summary>
        /// 写しを残した場合は、エラーのメッセージに写しのパスを添える。残していなければ何もしない。
        /// </summary>
        /// <param name="record">報告するエラー</param>
        /// <param name="path">書き込み先のパス</param>
        /// <param name="keptBackupPath">残した写しのパス（<see cref="ProbedFileWriter.KeptBackupPath"/>）。無ければ null</param>
        public static ErrorRecord Attach(ErrorRecord record, string path, string? keptBackupPath)
        {
            if (keptBackupPath != null)
            {
                record.ErrorDetails = new ErrorDetails(
                    record.Exception.Message + " " + ValidationMessages.RestoreFromBackupFailed(path, keptBackupPath));
            }

            return record;
        }
    }
}
