using System;
using System.Collections.Generic;
using System.Text;

namespace SnowStack.EncodingProbe
{
    /// <summary>
    /// 文字エンコーディング判定のオプション
    /// </summary>
    public sealed class EncodingDetectorOptions
    {
        public DetectionStrategy Strategy { get; set; } = DetectionStrategy.Combined;
        public string? Culture { get; set; } = null;
    }

    /// <summary>
    /// 文字エンコーディング判定の戦略
    /// </summary>
    public enum DetectionStrategy
    {
        Combined,       // デフォルト：両方を統合
        UtfUnknownOnly, // UTF.Unknownのみ
        NativeOnly      // 独自実装のみ
    }

    /// <summary>
    /// 文字エンコーディング判定の動作モード
    /// </summary>
    public enum DetectionMode
    {
        Standard,   // 標準モード
        Skippable   // スキップ可能モード（標準機能の一部をスキップ）
    }

}
