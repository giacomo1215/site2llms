# site2llms
![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![License](https://img.shields.io/github/license/giacomo1215/site2llms)
![Repo Size](https://img.shields.io/github/repo-size/giacomo1215/site2llms)
![Issues](https://img.shields.io/github/issues/giacomo1215/site2llms)
![Stars](https://img.shields.io/github/stars/giacomo1215/site2llms?style=social)
![LLM Ready](https://img.shields.io/badge/LLM-ready-brightgreen)
![AI Summarization](https://img.shields.io/badge/AI-summarization-blueviolet)
![Ollama](https://img.shields.io/badge/Ollama-supported-black)
![Markdown Output](https://img.shields.io/badge/output-Markdown-blue)
![llms.txt](https://img.shields.io/badge/index-llms.txt-important)
![Open Source](https://img.shields.io/badge/open--source-yes-brightgreen)
![Maintained](https://img.shields.io/badge/maintained-yes-success)
![Built with Love](https://img.shields.io/badge/built%20with-love-red)

Universal website summarizer for generating AI-friendly Markdown pages and a host-level `llms.txt` index.

`site2llms` discovers pages from a site, extracts readable content, summarizes each page via Ollama, and writes structured output under `output/<host>/`.

## What it does

- Discovers URLs using ordered strategies: **WordPress REST API → Sitemap → Crawl fallback**.
- Detects WordPress sites automatically and uses the REST API to get server-rendered content (bypasses JS-dependent themes like Elementor).
- Fetches page HTML with browser-like headers; automatically retries with a **headless Chromium browser** when bot-protection or challenge pages are detected.
- Supports **cookie injection** from a Netscape/JSON cookie file to bypass CAPTCHAs and authentication gates.
- Extracts main content (`main`, `article`, role/content selectors, then `body`).
- Converts extracted content to Markdown.
- Calls Ollama `/api/generate` to produce structured summaries.
- Writes one summary file per page in `output/<host>/ai/pages/*.md`.
- Builds/updates `output/<host>/llms.txt`.
- Maintains `output/<host>/manifest.json` for content-hash caching.

## Tech stack

- .NET `net10.0` console app
- [AngleSharp](https://www.nuget.org/packages/AngleSharp) (HTML parsing)
- [Microsoft.Playwright](https://www.nuget.org/packages/Microsoft.Playwright) (headless Chromium for JS-rendered/protected sites)
- [ReverseMarkdown](https://www.nuget.org/packages/ReverseMarkdown) (HTML → Markdown)
- [System.ServiceModel.Syndication](https://www.nuget.org/packages/System.ServiceModel.Syndication) (RSS/Atom parsing)
- Ollama API for summarization

## Requirements

- .NET SDK 10.x
- Running Ollama instance (local or remote)
- Reachable model in Ollama (default in app: `minimax-m2.5:cloud`)
- Playwright browsers installed (run `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` after first build)

## Quick start

1. Restore/build:

```bash
dotnet build
```

2. Run:

```bash
dotnet run
```

3. Answer prompts:

- `Root URL` (default: `https://example.com`)
- `Max pages` (default: `200`)
- `Max depth for crawl fallback` (default: `3`)
- `Delay ms between requests` (default: `250`)
- `Ollama base URL` (default: `http://localhost:11434`)
- `Ollama model` (default: `minimax-m2.5:cloud`)
- `Cookie file` (optional — path to a Netscape or JSON cookie file, blank to skip)

## Example run

```text
site2llms - Universal website summarizer
Root URL [https://example.com]: https://example.com
Max pages [200]: 3
Max depth for crawl fallback [3]: 2
Delay ms between requests [250]: 100
Ollama base URL [http://localhost:11434]:
Ollama model [minimax-m2.5:cloud]:
Cookie file (Netscape/JSON, blank to skip) []:
WP REST detected: no
Discovered 3 pages.
Processing: https://example.com/
...
Run completed.
Discovered: 3
Processed:  2
Skipped:    1 (cache hits: 1)
Failed:     0
Output:     C:\...\output\example.com
```

### Example with a protected site

```text
Processing: https://protected-site.com/
  Protection detected: SiteGround CAPTCHA (SGCaptcha) — retrying with headless browser...
  Headless browser also blocked: SiteGround CAPTCHA (SGCaptcha)
  Tip: supply a cookie file (--cookies) from a real browser session to bypass this protection.
  Skipped: Extracted markdown too short (<50 chars)
```

## Output structure

For a root URL like `https://example.com`, outputs are:

```text
output/
	example.com/
		llms.txt
		manifest.json
		ai/
			pages/
				home.md
				index_asp.md
				contattaci_php.md
```

### `ai/pages/*.md`

Each page file contains:

- YAML frontmatter:
	- `title`
	- `source_url`
	- `fetched_at`
	- `content_hash`
	- `generator`
- Summarized Markdown body from Ollama

### `llms.txt`

Generated index with:

- Site root and short description
- Sorted list of page title → public URL-like path (`/ai/pages/<file>.md`)

### `manifest.json`

Per-URL cache metadata:

- `url`
- `contentHash`
- `relativeOutputPath`
- `lastGeneratedAt`
- `title`

If extracted content hash is unchanged, the page is skipped as a cache hit.

## Processing pipeline

1. **Discover** candidate URLs (`CompositeDiscovery`)
   - WordPress REST API → Sitemap → Crawl fallback
2. **Fetch** page content
   - WordPress REST `content.rendered` (if WP detected) → HTTP fetch → Headless Chromium fallback
   - Cookies injected into both HTTP and headless paths when provided
3. **Extract** readable content (`HeuristicContentExtractor`)
4. **Summarize** with Ollama (`OllamaSummarizer`)
5. **Write** summary page and update manifest (`FileOutputWriter`, `ManifestStore`)
6. **Build** `llms.txt` index (`LlmsTxtBuilder`)

## Discovery strategy details

Strategies are tried in order; the first one that returns results wins.

- **WordPressRestDiscovery** (preferred for WP sites)
	- Probes `/wp-json/` and `/?rest_route=/` for WP REST API availability
	- Fetches `wp/v2/pages` and `wp/v2/posts` with pagination (`X-WP-TotalPages`)
	- Skips media attachments and password-protected posts
	- Falls back on 401/403 with clear log message
	- Caches `content.rendered` HTML in-memory so the fetch stage doesn't need a second request
- **SitemapDiscovery**
	- Tries `/sitemap.xml`, `/sitemap_index.xml`, `/wp-sitemap.xml`
	- Supports both `sitemapindex` and `urlset`
- **CrawlDiscovery** (fallback)
	- BFS crawl from root URL
	- Honors `MaxDepth`, `MaxPages`, `DelayMs`, and same-host filtering

## Extraction heuristics

- Preferred containers: `main`, `article`, `[role='main']`, `.content/.entry-content/.post-content`, fallback `body`
- Removes boilerplate tags: `script`, `style`, `noscript`, `nav`, `footer`, `header`, `aside`
- Converts to Markdown and normalizes spacing
- Skips pages with extracted markdown shorter than 50 characters

## Fetching & protection bypass

The fetch pipeline has three layers:

1. **HTTP fetch** — fast, lightweight, uses `HttpClient` with browser-like headers and automatic gzip/brotli decompression.
2. **Headless Chromium** — automatic fallback when the HTTP response looks blocked or too thin (<600 bytes). Uses Playwright with `NetworkIdle` wait.
3. **Cookie injection** — if a cookie file is provided, cookies are injected into both `HttpClient` (via `CookieContainer`) and the Playwright browser context.

### Challenge detection

The app recognizes 13 common protection patterns and reports each one explicitly:

| Pattern | Label |
|---|---|
| SGCaptcha / `.well-known/sgcaptcha` | SiteGround CAPTCHA |
| `cf-challenge` / "Just a moment" | Cloudflare challenge |
| "Attention Required" | Cloudflare block page |
| hCaptcha / g-recaptcha | CAPTCHA challenges |
| "Checking your browser" | Browser verification |
| "DDoS protection by" | DDoS interstitial |
| "enable javascript" | JS-required gate |

### Cookie file format

Two formats are supported:

**Netscape/Mozilla cookie.txt** (exported by browser extensions like "Get cookies.txt LOCALLY"):

```text
# Netscape HTTP Cookie File
.example.com	TRUE	/	FALSE	0	session_id	abc123
.example.com	TRUE	/	TRUE	0	__cf_bm	xyz789
```

**JSON array**:

```json
[
  { "name": "session_id", "value": "abc123", "domain": ".example.com", "path": "/" },
  { "name": "__cf_bm", "value": "xyz789", "domain": ".example.com", "path": "/" }
]
```

### How to bypass a protected site

1. Open the target site in a real browser and solve the CAPTCHA/challenge.
2. Export cookies using a browser extension (e.g., "Get cookies.txt LOCALLY") as a `.txt` file.
3. Run site2llms and provide the path when prompted:
   ```
   Cookie file (Netscape/JSON, blank to skip) []: cookies.txt
   Cookies loaded from: cookies.txt
   ```

## Configuration notes

- `SameHostOnly` is currently set to `true` in `Program.cs`.
- HTTP client timeout is currently set to **90 seconds**.
- Both website fetching and Ollama calls use browser-like HTTP headers.
- Automatic decompression (gzip, deflate, Brotli) is enabled.
- WordPress REST requests include retry with exponential backoff on 429/503 responses.

## Troubleshooting

### Timeout errors

If pages fail with timeout errors:

- Check website responsiveness and network reachability.
- Reduce `MaxPages` for first runs.
- Increase `DelayMs` only for politeness/rate-limits (it does not increase timeout).
- If needed, increase `HttpClient.Timeout` in `Program.cs`.

### Ollama errors

- Ensure Ollama is reachable at the configured base URL.
- Verify the selected model exists (`ollama list`).
- Pull model if needed (`ollama pull <model-name>`).

### Empty or skipped content

- Some pages are mostly navigation or script-rendered and may be skipped.
- Try specific content URLs instead of generic landing pages.
- For WordPress/Elementor sites, the WP REST API path usually gets real content even when HTML fetch fails.

### Protected / CAPTCHA sites

If you see messages like:

```
Protection detected: SiteGround CAPTCHA (SGCaptcha) — retrying with headless browser...
Tip: supply a cookie file (--cookies) from a real browser session to bypass this protection.
```

1. Visit the site in a real browser and complete the challenge.
2. Export cookies (Netscape `.txt` or JSON) and provide the file path when running the app.
3. Cookies are injected into both HTTP and headless browser requests automatically.

## Development

Build:

```bash
dotnet build
```

Run:

```bash
dotnet run
```

Solution files:

- `site2llms.sln`
- `site2llms.csproj`
- `Program.cs`
- `Core/` (models, pipeline, services, utils)

## Current limitations

- Interactive prompts only (no CLI flags yet)
- Single model provider (Ollama)
- Heuristic extraction may miss content in complex SPA frameworks
- Cookie files must be manually exported from a browser session
- Headless browser adds latency (~5-15s per page) when triggered

---

Possible next steps: CLI arguments (`--url`, `--max-pages`, `--cookies`, etc.), external cache source fallback (Google Cache / Wayback Machine), and stealth browser patches for more aggressive bot protection.
