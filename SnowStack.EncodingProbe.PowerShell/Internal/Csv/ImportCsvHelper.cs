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
using System.IO;
using System.Management.Automation;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal.Csv
{
    /// <summary>
    /// Helper class to import single CSV file.
    /// </summary>
    /// <remarks>
    /// 移植元の <c>ImportCsvHelper</c>。解析の規則（空白・引用符・<c>#</c> で始まる行の扱いなど）は移植元のとおりとし、
    /// 独自の改良を加えない。移植元からの変更は、該当箇所に「移植元:」として記す。主なものは次のとおり。
    /// <list type="bullet">
    /// <item>入力を <see cref="StreamReader"/> から <see cref="TextReader"/> に広げた（ファイル以外の入力でも試験できるように）。
    /// 末尾の判定は <see cref="StreamReader.EndOfStream"/> の代わりに <see cref="TextReader.Peek"/> が -1 かどうかで行う</item>
    /// <item>コマンドレット（<see cref="PSCmdlet"/>）の代わりに、オブジェクトと警告の出力先を受け取る</item>
    /// <item>メッセージは移植元のリソースではなく <see cref="MessageCatalog"/> から取る</item>
    /// </list>
    /// </remarks>
    internal sealed class ImportCsvHelper
    {
        #region constructor

        /// <summary>
        /// Output of the imported objects.
        /// </summary>
        /// <remarks>移植元: PSCmdlet _cmdlet の WriteObject</remarks>
        private readonly Action<PSObject> _writeObject;

        /// <summary>
        /// Output of the warning.
        /// </summary>
        /// <remarks>移植元: PSCmdlet _cmdlet の WriteWarning</remarks>
        private readonly Action<string> _writeWarning;

        /// <summary>
        /// CSV delimiter (default is the "comma" / "," character).
        /// </summary>
        private readonly char _delimiter;

        /// <summary>
        /// Use "UnspecifiedName" when the name is null or empty.
        /// </summary>
        private const string UnspecifiedName = "H";

        /// <summary>
        /// Avoid writing out duplicate warning messages when there are one or more unspecified names.
        /// </summary>
        private bool _alreadyWarnedUnspecifiedName = false;

        /// <summary>
        /// Gets reference to header values.
        /// </summary>
        internal IList<string> Header { get; private set; }

        /// <summary>
        /// Gets ETS type name from the first line / comment in the CSV.
        /// </summary>
        internal string TypeName { get; private set; }

        /// <summary>
        /// Reader of the csv content.
        /// </summary>
        private readonly TextReader _sr;

        // Initial sizes of the value list and the line stringbuilder.
        // Set to reasonable initial sizes. They may grow beyond these,
        // but this will prevent a few reallocations.
        private const int ValueCountGuestimate = 16;
        private const int LineLengthGuestimate = 256;

        internal ImportCsvHelper(
            Action<PSObject> writeObject,
            Action<string> writeWarning,
            char delimiter,
            IList<string> header,
            string typeName,
            TextReader streamReader)
        {
            // 移植元: ArgumentNullException.ThrowIfNull(cmdlet) / ThrowIfNull(streamReader)
            if (writeObject == null)
            {
                throw new ArgumentNullException(nameof(writeObject));
            }

            if (writeWarning == null)
            {
                throw new ArgumentNullException(nameof(writeWarning));
            }

            if (streamReader == null)
            {
                throw new ArgumentNullException(nameof(streamReader));
            }

            _writeObject = writeObject;
            _writeWarning = writeWarning;
            _delimiter = delimiter;
            Header = header;
            TypeName = typeName;
            _sr = streamReader;
        }

        #endregion constructor

        #region reading helpers

        /// <summary>
        /// This is set to true when end of file is reached.
        /// </summary>
        /// <remarks>移植元: _sr.EndOfStream</remarks>
        private bool EOF => _sr.Peek() == -1;

        private char ReadChar()
        {
            if (EOF)
            {
                throw new InvalidOperationException("EOF is reached.");
            }

            int i = _sr.Read();
            return (char)i;
        }

        /// <summary>
        /// Peeks the next character in the stream and returns true if it is same as passed in character.
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        private bool PeekNextChar(char c)
        {
            int i = _sr.Peek();
            if (i == -1)
            {
                return false;
            }

            return c == (char)i;
        }

        /// <summary>
        /// Reads a line from file. This consumes the end of line.
        /// Only use it when end of line chars are not important.
        /// </summary>
        /// <returns>Line from file.</returns>
        private string ReadLine() => _sr.ReadLine();

        #endregion reading helpers

        internal void ReadHeader()
        {
            // Read #Type record if available
            if ((TypeName == null) && (!this.EOF))
            {
                TypeName = ReadTypeInformation();
            }

            var values = new List<string>(ValueCountGuestimate);
            var builder = new StringBuilder(LineLengthGuestimate);
            while ((Header == null) && (!this.EOF))
            {
                ParseNextRecord(values, builder);

                // Trim all trailing blankspaces and delimiters ( single/multiple ).
                // If there is only one element in the row and if its a blankspace we dont trim it.
                // A trailing delimiter is represented as a blankspace while being added to result collection
                // which is getting trimmed along with blankspaces supplied through the CSV in the below loop.
                while (values.Count > 1 && values[values.Count - 1].Equals(string.Empty))
                {
                    values.RemoveAt(values.Count - 1);
                }

                // File starts with '#' and contains '#Fields:' is W3C Extended Log File Format
                // 移植元: values[0].StartsWith("#Fields: ")（カルチャー依存の比較）。ASCII だけの比較なので序数比較にした
                if (values.Count != 0 && values[0].StartsWith("#Fields: ", StringComparison.Ordinal))
                {
                    values[0] = values[0].Substring(9);
                    Header = values;
                }
                // 移植元: values[0].StartsWith('#')（net48 に無い）
                else if (values.Count != 0 && values[0].StartsWith("#", StringComparison.Ordinal))
                {
                    // Skip all lines starting with '#'
                }
                else
                {
                    // This is not W3C Extended Log File Format
                    // By default first line is Header
                    Header = values;
                }
            }

            if (Header != null && Header.Count > 0)
            {
                ValidatePropertyNames(Header);
            }
        }

        internal void Import(ref bool alreadyWriteOutWarning)
        {
            _alreadyWarnedUnspecifiedName = alreadyWriteOutWarning;
            ReadHeader();
            var prevalidated = false;
            var values = new List<string>(ValueCountGuestimate);
            var builder = new StringBuilder(LineLengthGuestimate);
            while (true)
            {
                ParseNextRecord(values, builder);
                if (values.Count == 0)
                    break;

                if (values.Count == 1 && string.IsNullOrEmpty(values[0]))
                {
                    // skip the blank lines
                    continue;
                }

                PSObject result = BuildMshobject(TypeName, Header, values, _delimiter, prevalidated);
                prevalidated = true;
                _writeObject(result);
            }

            alreadyWriteOutWarning = _alreadyWarnedUnspecifiedName;
        }

        /// <summary>
        /// Validate the names of properties.
        /// </summary>
        /// <param name="names"></param>
        private static void ValidatePropertyNames(IList<string> names)
        {
            if (names != null)
            {
                if (names.Count == 0)
                {
                    // If there are no names, it is an error
                }
                else
                {
                    HashSet<string> headers = new(StringComparer.OrdinalIgnoreCase);
                    foreach (string currentHeader in names)
                    {
                        if (!string.IsNullOrEmpty(currentHeader))
                        {
                            if (!headers.Add(currentHeader))
                            {
                                // throw a terminating error as there are duplicate headers in the input.
                                // 移植元: ExtendedTypeSystem.MemberAlreadyPresent（PowerShell のリソース）
                                string memberAlreadyPresentMsg = ValidationMessages.CsvHeaderAlreadyPresent(currentHeader);

                                ExtendedTypeSystemException exception = new(memberAlreadyPresentMsg);
                                throw exception;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read the type information, if present.
        /// </summary>
        /// <returns>Type string if present else null.</returns>
        private string ReadTypeInformation()
        {
            string type = null;
            if (PeekNextChar('#'))
            {
                string temp = ReadLine();
                if (temp.StartsWith("#Type", StringComparison.OrdinalIgnoreCase))
                {
                    type = temp.Substring(5);
                    type = type.Trim();
                    if (type.Length == 0)
                    {
                        type = null;
                    }
                }
            }

            return type;
        }

        /// <summary>
        /// Reads the next record from the file and returns parsed collection of string.
        /// </summary>
        /// <returns>
        /// Parsed collection of strings.
        /// </returns>
        private void ParseNextRecord(List<string> result, StringBuilder current)
        {
            result.Clear();

            // current string
            current.Clear();

            bool seenBeginQuote = false;

            while (!EOF)
            {
                // Read the next character
                char ch = ReadChar();

                if (ch == _delimiter)
                {
                    if (seenBeginQuote)
                    {
                        // Delimiter inside double quotes is part of string.
                        // Ex:
                        // "foo, bar"
                        // is parsed as
                        // ->foo, bar<-
                        current.Append(ch);
                    }
                    else
                    {
                        // Delimiter outside quotes is end of current word.
                        result.Add(current.ToString());
                        current.Remove(0, current.Length);
                    }
                }
                else if (ch == '"')
                {
                    if (seenBeginQuote)
                    {
                        if (PeekNextChar('"'))
                        {
                            // "" inside double quote are single quote
                            // ex: "foo""bar"
                            // is read as
                            // ->foo"bar<-

                            // PeekNextChar only peeks. Read the next char.
                            ReadChar();
                            current.Append('"');
                        }
                        else
                        {
                            // We have seen a matching end quote.
                            seenBeginQuote = false;

                            // Read
                            // everything till we hit next delimiter.
                            // In correct CSV,1) end quote is followed by delimiter
                            // 2)end quote is followed some whitespaces and
                            // then delimiter.
                            // We eat the whitespaces seen after the ending quote.
                            // However if there are other characters, we add all of them
                            // to string.
                            // Ex: ->"foo bar"<- is read as ->foo bar<-
                            // ->"foo bar"  <- is read as ->foo bar<-
                            // ->"foo bar" ab <- is read as ->"foo bar" ab <-
                            bool endofRecord = false;
                            ReadTillNextDelimiter(current, ref endofRecord, true);
                            result.Add(current.ToString());
                            current.Remove(0, current.Length);
                            if (endofRecord)
                                break;
                        }
                    }
                    else if (current.Length == 0)
                    {
                        // We are at the beginning of a new word.
                        // This quote is the first quote.
                        seenBeginQuote = true;
                    }
                    else
                    {
                        // We are seeing a quote after the start of
                        // the word. This is error, however we will be
                        // lenient here and do what excel does:
                        // Ex: foo "ba,r"
                        // In above example word read is ->foo "ba<-
                        // Basically we read till next delimiter
                        bool endOfRecord = false;
                        current.Append(ch);
                        ReadTillNextDelimiter(current, ref endOfRecord, false);
                        result.Add(current.ToString());
                        current.Remove(0, current.Length);
                        if (endOfRecord)
                            break;
                    }
                }
                else if (ch == ' ' || ch == '\t')
                {
                    if (seenBeginQuote)
                    {
                        // Spaces in side quote are valid
                        current.Append(ch);
                    }
                    else if (current.Length == 0)
                    {
                        // ignore leading spaces
                        continue;
                    }
                    else
                    {
                        // We are not in quote and we are not at the
                        // beginning of a word. We should not be seeing
                        // spaces here. This is an error condition, however
                        // we will be lenient here and do what excel does,
                        // that is read till next delimiter.
                        // Ex: ->foo <- is read as ->foo<-
                        // Ex: ->foo bar<- is read as ->foo bar<-
                        // Ex: ->foo bar <- is read as ->foo bar <-
                        // Ex: ->foo bar "er,ror"<- is read as ->foo bar "er<-
                        bool endOfRecord = false;
                        current.Append(ch);
                        ReadTillNextDelimiter(current, ref endOfRecord, true);
                        result.Add(current.ToString());
                        current.Remove(0, current.Length);

                        if (endOfRecord)
                        {
                            break;
                        }
                    }
                }
                else if (IsNewLine(ch, out string newLine))
                {
                    if (seenBeginQuote)
                    {
                        // newline inside quote are valid
                        current.Append(newLine);
                    }
                    else
                    {
                        result.Add(current.ToString());
                        current.Remove(0, current.Length);

                        // New line outside quote is end of word and end of record
                        break;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }

            if (current.Length != 0)
            {
                result.Add(current.ToString());
            }
        }

        // If we detect a newline we return it as a string "\r", "\n" or "\r\n"
        private bool IsNewLine(char ch, out string newLine)
        {
            newLine = string.Empty;
            if (ch == '\r')
            {
                if (PeekNextChar('\n'))
                {
                    ReadChar();
                    newLine = "\r\n";
                }
                else
                {
                    newLine = "\r";
                }
            }
            else if (ch == '\n')
            {
                newLine = "\n";
            }

            return newLine != string.Empty;
        }

        /// <summary>
        /// This function reads the characters till next delimiter and adds them to current.
        /// </summary>
        /// <param name="current"></param>
        /// <param name="endOfRecord">
        /// This is true if end of record is reached
        /// when delimiter is hit. This would be true if delimiter is NewLine.
        /// </param>
        /// <param name="eatTrailingBlanks">
        /// If this is true, eat the trailing blanks. Note:if there are non
        /// whitespace characters present, then trailing blanks are not consumed.
        /// </param>
        private void ReadTillNextDelimiter(StringBuilder current, ref bool endOfRecord, bool eatTrailingBlanks)
        {
            StringBuilder temp = new();

            // Did we see any non-whitespace character
            bool nonWhiteSpace = false;

            while (true)
            {
                if (EOF)
                {
                    endOfRecord = true;
                    break;
                }

                char ch = ReadChar();

                if (ch == _delimiter)
                {
                    break;
                }
                else if (IsNewLine(ch, out string newLine))
                {
                    endOfRecord = true;
                    break;
                }
                else
                {
                    temp.Append(ch);
                    if (ch != ' ' && ch != '\t')
                    {
                        nonWhiteSpace = true;
                    }
                }
            }

            if (eatTrailingBlanks && !nonWhiteSpace)
            {
                string s = temp.ToString();
                s = s.Trim();
                current.Append(s);
            }
            else
            {
                current.Append(temp);
            }
        }

        private PSObject BuildMshobject(string type, IList<string> names, List<string> values, char delimiter, bool preValidated = false)
        {
            // 移植元: new PSObject(names.Count)。容量を指定するコンストラクターは
            // Windows PowerShell 5.1 に無い（PowerShellStandard.Library 5.1.1 にも無い）。容量の指定は結果に影響しない
            PSObject result = new();
            char delimiterlocal = delimiter;
            int unspecifiedNameIndex = 1;
            for (int i = 0; i <= names.Count - 1; i++)
            {
                string name = names[i];
                string value = null;

                // if name is null and delimiter is '"', use a default property name 'UnspecifiedName'
                if (name.Length == 0 && delimiterlocal == '"')
                {
                    name = UnspecifiedName + unspecifiedNameIndex;
                    unspecifiedNameIndex++;
                }

                // if name is null and delimiter is not '"', use a default property name 'UnspecifiedName'
                if (string.IsNullOrEmpty(name))
                {
                    name = UnspecifiedName + unspecifiedNameIndex;
                    unspecifiedNameIndex++;
                }

                // If no value was present in CSV file, we write null.
                if (i < values.Count)
                {
                    value = values[i];
                }

                result.Properties.Add(new PSNoteProperty(name, value), preValidated);
            }

            if (!_alreadyWarnedUnspecifiedName && unspecifiedNameIndex != 1)
            {
                // 移植元: CsvCommandStrings.UseDefaultNameForUnspecifiedHeader（PowerShell のリソース）
                _writeWarning(ValidationMessages.CsvUnspecifiedHeader());
                _alreadyWarnedUnspecifiedName = true;
            }

            if (!string.IsNullOrEmpty(type))
            {
                result.TypeNames.Clear();
                result.TypeNames.Add(type);
                result.TypeNames.Add(ImportExportCSVHelper.CSVTypePrefix + type);
            }

            return result;
        }
    }
}
