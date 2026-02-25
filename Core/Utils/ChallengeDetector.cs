namespace site2llms.Core.Utils;

/// <summary>
/// Detects common bot-protection / challenge pages by inspecting raw HTML.
/// Returns a human-readable reason when a challenge is detected, or null when content looks normal.
/// </summary>
public static class ChallengeDetector
{
    private static readonly (string Pattern, string Label)[] Signatures =
    {
        ("sgcaptcha",              "SiteGround CAPTCHA (SGCaptcha)"),
        ("/.well-known/sgcaptcha", "SiteGround CAPTCHA redirect"),
        ("cf-challenge",           "Cloudflare challenge"),
        ("Just a moment",          "Cloudflare JS challenge"),
        ("attention required",     "Cloudflare Attention Required"),
        ("enable javascript",      "JavaScript-required gate"),
        ("captcha",                "Generic CAPTCHA"),
        ("hCaptcha",               "hCaptcha challenge"),
        ("g-recaptcha",            "Google reCAPTCHA"),
        ("Checking your browser",  "Browser verification gate"),
        ("DDoS protection by",     "DDoS protection interstitial"),
    };

    /// <summary>
    /// Inspects the first portion of HTML and returns the detected challenge label, or null if clean.
    /// </summary>
    public static string? Detect(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var sampleLength = Math.Min(html.Length, 4096);
        var sample = html[..sampleLength];

        foreach (var (pattern, label) in Signatures)
        {
            if (sample.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns true when the HTML looks like a challenge/interstitial rather than real content.
    /// </summary>
    public static bool IsChallenge(string? html)
    {
        return Detect(html) is not null;
    }

    /// <summary>
    /// Returns true when response is suspiciously small (likely a redirect stub or empty shell).
    /// </summary>
    public static bool IsTooThin(string? html, int threshold = 600)
    {
        return string.IsNullOrWhiteSpace(html) || html.Length < threshold;
    }
}
