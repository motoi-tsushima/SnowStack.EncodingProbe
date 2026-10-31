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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management.Automation;
using System.Text;

namespace SnowStack.EncodingProbe.PowerShell.Internal.Csv
{
    /// <summary>
    /// Helper class for Export-Csv and ConvertTo-Csv.
    /// </summary>
    /// <remarks>
    /// 移植元の <c>ExportCsvHelper</c>。書き出しの規則は移植元のとおりとし、独自の改良を加えない。
    /// 移植元からの変更（PowerShell の内部 API・net48 に無い機能の置き換え）は、該当箇所に「移植元:」として記す。
    /// </remarks>
    internal sealed class ExportCsvHelper : IDisposable
    {
        private readonly char _delimiter;
        private readonly CsvQuoteKind _quoteKind;
        private readonly HashSet<string> _quoteFields;
        private readonly StringBuilder _outputString;

        // 移植元: value.AsSpan().IndexOfAny(_delimiter, '\n', '"')。
        // net48 で System.Memory への依存を持ち込まないよう、string.IndexOfAny(char[]) にした
        private readonly char[] _asNeededQuoteTriggers;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportCsvHelper"/> class.
        /// </summary>
        /// <param name="delimiter">Delimiter char.</param>
        /// <param name="quoteKind">Kind of quoting.</param>
        /// <param name="quoteFields">List of fields to quote.</param>
        internal ExportCsvHelper(char delimiter, CsvQuoteKind quoteKind, string[] quoteFields)
        {
            _delimiter = delimiter;
            _quoteKind = quoteKind;
            // 移植元: StringComparer.OrdinalIgnoreCase。5.1 でも .NET 10 と同じ結果にするため、本プロジェクトの比較器にした（第六次。BUG-0020）
            _quoteFields = quoteFields == null ? null : new HashSet<string>(quoteFields, CsvNameComparer.Instance);
            _outputString = new StringBuilder(128);
            _asNeededQuoteTriggers = new[] { delimiter, '\n', '"' };
        }

        // Name of properties to be written in CSV format

        /// <summary>
        /// Get the name of properties from source PSObject and add them to _propertyNames.
        /// </summary>
        internal static IList<string> BuildPropertyNames(PSObject source, IList<string> propertyNames)
        {
            if (propertyNames != null)
            {
                throw new InvalidOperationException("BuildPropertyNames method should be called only once per cmdlet instance.");
            }

            propertyNames = new Collection<string>();
            if (source.BaseObject is IDictionary dictionary)
            {
                foreach (var key in dictionary.Keys)
                {
                    propertyNames.Add(LanguagePrimitives.ConvertTo<string>(key));
                }

                // Add additional extended members added to the dictionary object, if any
                // 移植元: new PSMemberInfoIntegratingCollection<PSPropertyInfo>(source,
                //   PSObject.GetPropertyCollection(PSMemberViewTypes.Extended))（いずれも internal）
                foreach (var prop in CsvPropertyViews.GetExtendedProperties(source))
                {
                    propertyNames.Add(prop.Name);
                }
            }
            else
            {
                // serialize only Extended and Adapted properties.
                // 移植元: new PSMemberInfoIntegratingCollection<PSPropertyInfo>(source,
                //   PSObject.GetPropertyCollection(PSMemberViewTypes.Extended | PSMemberViewTypes.Adapted))
                foreach (PSPropertyInfo prop in CsvPropertyViews.GetExtendedAndAdaptedProperties(source))
                {
                    propertyNames.Add(prop.Name);
                }
            }

            return propertyNames;
        }

        /// <summary>
        /// Converts PropertyNames in to a CSV string.
        /// </summary>
        /// <returns>Converted string.</returns>
        internal string ConvertPropertyNamesCSV(IList<string> propertyNames)
        {
            // 移植元: ArgumentNullException.ThrowIfNull(propertyNames)
            if (propertyNames == null)
            {
                throw new ArgumentNullException(nameof(propertyNames));
            }

            _outputString.Clear();
            bool first = true;

            foreach (string propertyName in propertyNames)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    _outputString.Append(_delimiter);
                }

                if (_quoteFields != null)
                {
                    if (_quoteFields.TryGetValue(propertyName, out _))
                    {
                        AppendStringWithEscapeAlways(_outputString, propertyName);
                    }
                    else
                    {
                        _outputString.Append(propertyName);
                    }
                }
                else
                {
                    switch (_quoteKind)
                    {
                        case CsvQuoteKind.Always:
                            AppendStringWithEscapeAlways(_outputString, propertyName);
                            break;
                        case CsvQuoteKind.AsNeeded:

                            if (propertyName.IndexOfAny(_asNeededQuoteTriggers) != -1)
                            {
                                AppendStringWithEscapeAlways(_outputString, propertyName);
                            }
                            else
                            {
                                _outputString.Append(propertyName);
                            }

                            break;
                        case CsvQuoteKind.Never:
                            _outputString.Append(propertyName);
                            break;
                    }
                }
            }

            return _outputString.ToString();
        }

        /// <summary>
        /// Convert PSObject to CSV string.
        /// </summary>
        /// <param name="mshObject">PSObject to convert.</param>
        /// <param name="propertyNames">Property names.</param>
        /// <returns></returns>
        internal string ConvertPSObjectToCSV(PSObject mshObject, IList<string> propertyNames)
        {
            // 移植元: ArgumentNullException.ThrowIfNull(propertyNames)
            if (propertyNames == null)
            {
                throw new ArgumentNullException(nameof(propertyNames));
            }

            _outputString.Clear();
            bool first = true;

            foreach (string propertyName in propertyNames)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    _outputString.Append(_delimiter);
                }

                string value = null;
                if (mshObject.BaseObject is IDictionary dictionary)
                {
                    if (dictionary.Contains(propertyName))
                    {
                        value = dictionary[propertyName]?.ToString();
                    }
                    // 移植元: mshObject.Properties[propertyName]。列名の照合と同じ規則で引く（第六次。BUG-0020）
                    else if (CsvPropertyViews.FindProperty(mshObject, propertyName) is PSPropertyInfo property)
                    {
                        value = GetToStringValueForProperty(property);
                    }
                }
                // 移植元: mshObject.Properties[propertyName]。列名の照合と同じ規則で引く（第六次。BUG-0020）
                else if (CsvPropertyViews.FindProperty(mshObject, propertyName) is PSPropertyInfo property)
                {
                    value = GetToStringValueForProperty(property);
                }

                // If value is null, assume property is not present and skip it.
                if (value != null)
                {
                    if (_quoteFields != null)
                    {
                        if (_quoteFields.TryGetValue(propertyName, out _))
                        {
                            AppendStringWithEscapeAlways(_outputString, value);
                        }
                        else
                        {
                            _outputString.Append(value);
                        }
                    }
                    else
                    {
                        switch (_quoteKind)
                        {
                            case CsvQuoteKind.Always:
                                AppendStringWithEscapeAlways(_outputString, value);
                                break;
                            case CsvQuoteKind.AsNeeded:
                                if (value != null && value.IndexOfAny(_asNeededQuoteTriggers) != -1)
                                {
                                    AppendStringWithEscapeAlways(_outputString, value);
                                }
                                else
                                {
                                    _outputString.Append(value);
                                }

                                break;
                            case CsvQuoteKind.Never:
                                _outputString.Append(value);
                                break;
                            default:
                                // 移植元: Diagnostics.Assert（PowerShell の内部 API）
                                Debug.Assert(false, "CsvQuoteKind has new item.");
                                break;
                        }
                    }
                }
            }

            return _outputString.ToString();
        }

        /// <summary>
        /// Get value from property object.
        /// </summary>
        /// <param name="property"> Property to convert.</param>
        /// <returns>ToString() value.</returns>
        internal static string GetToStringValueForProperty(PSPropertyInfo property)
        {
            // 移植元: ArgumentNullException.ThrowIfNull(property)
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            string value = null;
            try
            {
                object temp = property.Value;
                if (temp != null)
                {
                    value = temp.ToString();
                }
            }
            catch (Exception)
            {
                // If we cannot read some value, treat it as null.
            }

            return value;
        }

        /// <summary>
        /// Prepares string for writing type information.
        /// </summary>
        /// <param name="source">PSObject whose type to determine.</param>
        /// <returns>String with type information.</returns>
        internal static string GetTypeString(PSObject source)
        {
            string type = null;

            // get type of source
            Collection<string> tnh = source.TypeNames;
            if (tnh == null || tnh.Count == 0)
            {
                type = "#TYPE";
            }
            else
            {
                if (tnh[0] == null)
                {
                    throw new InvalidOperationException("Type hierarchy should not have null values.");
                }

                string temp = tnh[0];

                // If type starts with CSV: remove it. This would happen when you export
                // an imported object. import-csv adds CSV. prefix to the type.
                if (temp.StartsWith(ImportExportCSVHelper.CSVTypePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    temp = temp.Substring(4);
                }

                // 移植元: string.Create(CultureInfo.InvariantCulture, $"#TYPE {temp}")（net48 に無い）
                type = string.Format(CultureInfo.InvariantCulture, "#TYPE {0}", temp);
            }

            return type;
        }

        /// <summary>
        /// Escapes the " in string if necessary.
        /// Encloses the string in double quotes if necessary.
        /// </summary>
        internal static void AppendStringWithEscapeAlways(StringBuilder dest, string source)
        {
            if (source == null)
            {
                return;
            }

            // Adding Double quote to all strings
            dest.Append('"');
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                // Double quote in the string is escaped with double quote
                if (c == '"')
                {
                    dest.Append('"');
                }

                dest.Append(c);
            }

            dest.Append('"');
        }

        #region IDisposable Members

        /// <summary>
        /// Set to true when object is disposed.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Public dispose method.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                GC.SuppressFinalize(this);
            }

            _disposed = true;
        }

        #endregion IDisposable Members
    }
}
