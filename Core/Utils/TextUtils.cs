using System.Text.RegularExpressions;

namespace site2llms.Core.Utils;

/// <summary>
/// Text cleanup and escaping helpers shared across extraction/output.
/// </summary>
public static class TextUtils
{
    /// <summary>
    /// Normalizes markdown line endings and collapses long blank-line runs.
    /// </summary>
    /// <param name="md">Raw markdown text.</param>
    /// <returns>Trimmed markdown with normalized spacing.</returns>
    public static string CleanMarkdown(string md)
    {
        md = md ?? "";
        md = md.Replace("\r\n", "\n");
        md = Regex.Replace(md, @"\n{3,}", "\n\n");
        return md.Trim();
    }

    /// <summary>
    /// Escapes characters that can break quoted YAML scalar values.
    /// </summary>
    /// <param name="s">Raw string value.</param>
    /// <returns>YAML-safe escaped string.</returns>
    public static string EscapeYaml(string s)
    {
        return (s ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}