using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace site2llms.Core.Utils;

/// <summary>
/// Loads cookies from a Netscape/Mozilla cookie-jar text file (common export format)
/// or from a simple JSON array of {name, value, domain, path} objects.
/// </summary>
public static class CookieLoader
{
    /// <summary>
    /// Loads cookies from the specified file into a <see cref="CookieContainer"/>.
    /// Supports Netscape cookie.txt format and JSON array format.
    /// Returns an empty container when the path is null/empty or the file doesn't exist.
    /// </summary>
    public static CookieContainer Load(string? cookieFilePath, ILogger? logger = null)
    {
        var container = new CookieContainer();

        if (string.IsNullOrWhiteSpace(cookieFilePath) || !File.Exists(cookieFilePath))
        {
            return container;
        }

        var content = File.ReadAllText(cookieFilePath).Trim();

        if (content.StartsWith('['))
        {
            LoadJson(content, container, logger);
        }
        else
        {
            LoadNetscape(content, container);
        }

        return container;
    }

    /// <summary>
    /// Converts cookies into Playwright-compatible cookie objects.
    /// </summary>
    public static IReadOnlyList<CookieEntry> LoadAsList(string? cookieFilePath, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(cookieFilePath) || !File.Exists(cookieFilePath))
        {
            return Array.Empty<CookieEntry>();
        }

        var content = File.ReadAllText(cookieFilePath).Trim();

        return content.StartsWith('[')
            ? ParseJsonEntries(content, logger)
            : ParseNetscapeEntries(content);
    }

    private static void LoadJson(string json, CookieContainer container, ILogger? logger = null)
    {
        foreach (var entry in ParseJsonEntries(json, logger))
        {
            try
            {
                container.Add(new Cookie(entry.Name, entry.Value, entry.Path, entry.Domain));
            }
            catch
            {
                // Skip malformed entries silently.
            }
        }
    }

    private static List<CookieEntry> ParseJsonEntries(string json, ILogger? logger = null)
    {
        var entries = new List<CookieEntry>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = element.TryGetProperty("value", out var v) ? v.GetString() : null;
                var domain = element.TryGetProperty("domain", out var d) ? d.GetString() : null;
                var path = element.TryGetProperty("path", out var p) ? p.GetString() : "/";

                if (!string.IsNullOrWhiteSpace(name) && domain is not null)
                {
                    entries.Add(new CookieEntry(name!, value ?? string.Empty, domain, path ?? "/"));
                }
            }
        }
        catch (JsonException)
        {
            logger?.LogWarning("Failed to parse cookie JSON file");
        }

        return entries;
    }

    private static void LoadNetscape(string content, CookieContainer container)
    {
        foreach (var entry in ParseNetscapeEntries(content))
        {
            try
            {
                container.Add(new Cookie(entry.Name, entry.Value, entry.Path, entry.Domain));
            }
            catch
            {
                // Skip malformed entries silently.
            }
        }
    }

    private static List<CookieEntry> ParseNetscapeEntries(string content)
    {
        var entries = new List<CookieEntry>();

        foreach (var line in content.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            // Netscape format: domain \t flag \t path \t secure \t expiration \t name \t value
            var parts = line.Split('\t');
            if (parts.Length < 7)
            {
                continue;
            }

            var domain = parts[0];
            var path = parts[2];
            var name = parts[5];
            var value = parts[6];

            if (!string.IsNullOrWhiteSpace(name))
            {
                entries.Add(new CookieEntry(name, value, domain, path));
            }
        }

        return entries;
    }
}

/// <summary>
/// Simple cross-format cookie entry used for both HttpClient and Playwright injection.
/// </summary>
public record CookieEntry(string Name, string Value, string Domain, string Path);
