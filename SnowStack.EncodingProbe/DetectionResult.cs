using System.Text;

namespace SnowStack.EncodingProbe;

public sealed record DetectionResult(
    string EncodingName,
    bool HasBom,
    double Confidence,
    LineEndingKind LineEnding)
{
    /// <summary>
    /// .NET の Encoding オブジェクトとして取得します。
    /// </summary>
    public Encoding GetEncoding() => Encoding.GetEncoding(EncodingName);
}

public enum LineEndingKind
{
    Unknown,
    Lf,      // Unix系 (LF)
    CrLf,    // Windows (CRLF)
    Cr,      // 古典Mac (CR)
    Mixed    // 混在
}
