// Portions of this file are derived from PowerShell
// (https://github.com/PowerShell/PowerShell),
// src/Microsoft.PowerShell.Commands.Utility/commands/utility/CsvCommands.cs
// at commit db1d0a56cbe41adf05d0bad90be5190a6655404f.
// Copyright (c) Microsoft Corporation. Licensed under the MIT License.
// Modified by Motoi Tsushima for SnowStack.EncodingProbe.PowerShell.
// See THIRD-PARTY-NOTICES.txt for the full license text.

// 移植元に合わせて null 許容の注釈を付けない
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SnowStack.EncodingProbe.PowerShell.Internal.Csv
{
    /// <summary>
    /// Helper class for CSV conversion.
    /// </summary>
    /// <remarks>
    /// 移植元の <c>ImportExportCSVHelper</c> に、<c>ExportCsvCommand.ReconcilePreexistingPropertyNames</c> の論理を加えたもの。
    /// </remarks>
    internal static class ImportExportCSVHelper
    {
        internal const char CSVDelimiter = ',';
        internal const string CSVTypePrefix = "CSV:";

        /// <remarks>
        /// 移植元: <c>SetDelimiter(PSCmdlet cmdlet, string parameterSetName, char explicitDelimiter, bool useCulture)</c>。
        /// 移植元でも cmdlet 引数は使われていないため、受け取らない。
        /// <br/>
        /// <paramref name="useCulture"/> は -UseCulture（区切り文字にカルチャーのリスト区切り文字を使う）であり、
        /// 文字エンコーディングの判定に使う -Culture とは別のものである。
        /// </remarks>
        internal static char SetDelimiter(string parameterSetName, char explicitDelimiter, bool useCulture)
        {
            char delimiter = explicitDelimiter;
            switch (parameterSetName)
            {
                case "Delimiter":
                case "DelimiterPath":
                case "DelimiterLiteralPath":

                    // if delimiter is not given, it should take , as value
                    if (explicitDelimiter == '\0')
                    {
                        delimiter = ImportExportCSVHelper.CSVDelimiter;
                    }

                    break;
                case "UseCulture":
                case "CulturePath":
                case "CultureLiteralPath":
                    if (useCulture)
                    {
                        // ListSeparator is apparently always a character even though the property returns a string, checked via:
                        // [CultureInfo]::GetCultures("AllCultures") | % { ([CultureInfo]($_.Name)).TextInfo.ListSeparator } | ? Length -ne 1
                        delimiter = CultureInfo.CurrentCulture.TextInfo.ListSeparator[0];
                    }

                    break;
                default:
                    {
                        delimiter = ImportExportCSVHelper.CSVDelimiter;
                    }

                    break;
            }

            return delimiter;
        }

        /// <summary>
        /// 追記するオブジェクトの列を、追記先のヘッダーの列に合わせる。
        /// </summary>
        /// <remarks>
        /// 移植元: <c>ExportCsvCommand.ReconcilePreexistingPropertyNames</c>（インスタンスメソッド）。
        /// コマンドレットの状態を引数で受け取り、結果の列を返す形にした。
        /// 不一致は <see cref="CsvAppendMismatchException"/> で知らせ、ErrorRecord（ID
        /// <c>CannotAppendCsvWithMismatchedPropertyNames</c>）への変換はコマンド側で行う。
        /// </remarks>
        /// <param name="propertyNames">追記する最初のオブジェクトから求めた列</param>
        /// <param name="preexistingPropertyNames">追記先のヘッダーの列</param>
        /// <param name="force">-Force（足りない列を空にして書く）</param>
        /// <param name="path">エラーメッセージに示す追記先のパス</param>
        /// <returns>書き出しに使う列（追記先のヘッダーの順序と表記）</returns>
        internal static IList<string> ReconcilePreexistingPropertyNames(
            IList<string> propertyNames, IList<string> preexistingPropertyNames, bool force, string path)
        {
            if (preexistingPropertyNames == null)
            {
                throw new InvalidOperationException(
                    "ReconcilePreexistingPropertyNames method should only get called when preexisting property names have been read successfully.");
            }

            // 移植元: StringComparer.OrdinalIgnoreCase。5.1 でも .NET 10 と同じ結果にするため、本プロジェクトの比較器にした（第六次。BUG-0020）
            HashSet<string> appendedPropertyNames = new(CsvNameComparer.Instance);
            foreach (string appendedPropertyName in propertyNames)
            {
                appendedPropertyNames.Add(appendedPropertyName);
            }

            foreach (string preexistingPropertyName in preexistingPropertyNames)
            {
                if (!appendedPropertyNames.Contains(preexistingPropertyName))
                {
                    if (!force)
                    {
                        // 移植元: CsvCommandStrings.CannotAppendCsvWithMismatchedPropertyNames（PowerShell のリソース）
                        string errorMessage = ValidationMessages.CsvAppendMismatchedPropertyName(
                            preexistingPropertyName,
                            path);
                        throw new CsvAppendMismatchException(errorMessage, preexistingPropertyName);
                    }
                }
            }

            return preexistingPropertyNames;
        }
    }

    /// <summary>
    /// 追記するオブジェクトに、追記先の列に当たるプロパティが無いことを表す例外。
    /// </summary>
    /// <remarks>
    /// 移植元は <c>InvalidOperationException</c> を <c>ThrowTerminatingError</c> で投げる（対象は列名）。
    /// コマンド側で同じ ErrorRecord を組み立てられるよう、列名を持たせる。
    /// </remarks>
    internal sealed class CsvAppendMismatchException : InvalidOperationException
    {
        public CsvAppendMismatchException(string message, string propertyName)
            : base(message)
        {
            PropertyName = propertyName;
        }

        /// <summary>追記するオブジェクトに無かった列の名前</summary>
        public string PropertyName { get; }
    }
}
