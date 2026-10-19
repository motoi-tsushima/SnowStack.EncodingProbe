// Portions of this file are derived from PowerShell
// (https://github.com/PowerShell/PowerShell),
// src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs
// at commit db1d0a56cbe41adf05d0bad90be5190a6655404f.
// Copyright (c) Microsoft Corporation. Licensed under the MIT License.
// Modified by Motoi Tsushima for SnowStack.EncodingProbe.PowerShell.
// See THIRD-PARTY-NOTICES.txt for the full license text.

namespace SnowStack.EncodingProbe.PowerShell
{
    /// <summary>
    /// CSV の値を引用符で囲む方法（Export-ProbedCsv -UseQuotes）。
    /// </summary>
    /// <remarks>
    /// 移植元の <c>BaseCsvWritingCommand.QuoteKind</c>。値の名前と順序は移植元と同じ。
    /// Windows PowerShell 5.1 でも同じ値を使えるように、本モジュールで定義する。
    /// </remarks>
    public enum CsvQuoteKind
    {
        /// <summary>
        /// Never quote output.
        /// </summary>
        Never,

        /// <summary>
        /// Always quote output.
        /// </summary>
        Always,

        /// <summary>
        /// Quote output as needed (a field contains used delimiter).
        /// </summary>
        AsNeeded
    }
}
