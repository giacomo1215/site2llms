# site2llms

![.NET](https://img.shields.io/badge/.NET-8.0-purple)
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

A developer-friendly tool that turns any website into a structured, deployable set of AI-ready Markdown artifacts with a host-level `llms.txt` index.

`site2llms` discovers pages, extracts readable content, summarizes each page via a local Ollama model, and writes structured output under `output/<host>/` — ready to serve, embed, or feed into any LLM workflow.

## Why site2llms?

Most tools in this space either stop at SEO analysis, produce a single flat index file, or require manual curation. **site2llms is different:**

- **Deployable artifacts, not reports.** The output is a complete directory of Markdown files with YAML frontmatter and a `llms.txt` index — ready to drop into a static site, a docs bundle, or a RAG pipeline. It's not a one-off analysis; it's a repeatable build step.
- **Full pages, not just links.** Every discovered page gets its own structured summary with TL;DR, key points, FAQ, and metadata. An LLM consuming these files gets real content, not a table of contents.
- **Incremental by default.** A content-hash manifest (`manifest.json`) tracks what changed. Re-running the tool only processes new or updated pages — suitable for CI/CD or scheduled regeneration.
- **Handles the real web.** Cloudflare challenges, SiteGround CAPTCHAs, JS-rendered SPAs, WordPress Elementor themes — the layered fetch pipeline (HTTP → headless Chromium → cookie injection) deals with sites as they actually exist, not as they ideally should.
- **Developer-friendly.** Interactive prompts for quick runs, structured output for automation, clean separation of discover → fetch → extract → summarize → write stages. No external SaaS dependencies — just .NET, Ollama, and optionally Playwright.

## What it does

- Discovers URLs using ordered strategies: **WordPress REST API → Sitemap → RSS/Atom feeds → Crawl fallback**.
- Detects WordPress sites automatically and uses the REST API to get server-rendered content (bypasses JS-dependent themes like Elementor).
- Fetches page HTML with browser-like headers; automatically retries with a **headless Chromium browser** when bot-protection or challenge pages are detected.
- Supports **cookie injection** from a Netscape/JSON cookie file to bypass CAPTCHAs and authentication gates.
- Extracts main content (`main`, `article`, role/content selectors, then `body`) and strips boilerplate.
- Converts extracted content to Markdown.
- Calls Ollama `/api/generate` to produce structured summaries (TL;DR, key points, FAQ, context).
- Writes one summary file per page in `output/<host>/ai/pages/*.md` with YAML frontmatter.
- Builds/updates `output/<host>/llms.txt` — a sorted, host-level index of all summarized pages.
- Maintains `output/<host>/manifest.json` for content-hash caching so unchanged pages are skipped on re-runs.

## Tech stack

| Component | Purpose |
|---|---|
| .NET 8.0 console app | Runtime |
| [AngleSharp](https://www.nuget.org/packages/AngleSharp) 1.4.0 | HTML parsing & DOM querying |
| [Microsoft.Playwright](https://www.nuget.org/packages/Microsoft.Playwright) 1.55.0 | Headless Chromium for JS-rendered/protected sites |
| [ReverseMarkdown](https://www.nuget.org/packages/ReverseMarkdown) 5.2.0 | HTML → Markdown conversion |
| [System.ServiceModel.Syndication](https://www.nuget.org/packages/System.ServiceModel.Syndication) | RSS/Atom feed parsing |
| Ollama API | Local LLM summarization |

## Requirements

- .NET SDK 8.x
- Running Ollama instance (local or remote)
- Reachable model in Ollama (default: `minimax-m2.5:cloud`)
- Playwright browsers installed (run `pwsh bin/Debug/net8.0/playwright.ps1 install chromium` after first build)

## Quick start

1. Build:

```bash
dotnet build
```

2. Run:

```bash
dotnet run
```

3. Answer the interactive prompts:

| Prompt | Default |
|---|---|
| Root URL | `https://example.com` |
| Max pages | `200` |
| Max depth for crawl fallback | `3` |
| Delay ms between requests | `250` |
| Ollama base URL | `http://localhost:11434` |
| Ollama model | `minimax-m2.5:cloud` |
| Cookie file (Netscape/JSON) | *(blank to skip)* |

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

For a root URL like `https://example.com`:

```text
output/
  example.com/
    llms.txt              # host-level index of all summarized pages
    manifest.json         # content-hash cache for incremental runs
    ai/
      pages/
        home.md           # structured summary with YAML frontmatter
        about_us.md
        contattaci_php.md
```

### `ai/pages/*.md`

Each page file contains YAML frontmatter and a structured Markdown body:

```yaml
---
title: "Page Title"
source_url: "https://example.com/page"
fetched_at: "2025-01-15T10:30:00Z"
content_hash: "sha256hex..."
generator: "site2llms + Ollama"
---
```

The body follows a consistent template: **TL;DR** (2–4 bullets), **Key points** (5–10 bullets), **Useful context** (content type, services, deliverables), **FAQ** (5–8 Q&A pairs), and a **Reference** link back to the source URL.

### `llms.txt`

A sorted index with the site root, a short description, and one entry per page:

```text
# llms.txt for example.com

Site root: https://example.com
Short description: AI-friendly markdown summaries generated by site2llms.

## Index
- About Us: https://example.com/ai/pages/about_us.md
- Home: https://example.com/ai/pages/home.md
```

### `manifest.json`

Per-URL cache metadata (`url`, `contentHash`, `relativeOutputPath`, `lastGeneratedAt`, `title`). If a page's content hash hasn't changed since the last run, it's skipped as a cache hit.

## Processing pipeline

```text
Discover → Fetch → Extract → Summarize → Write → Build index
```

1. **Discover** — `CompositeDiscovery` tries strategies in order (WP REST → Sitemap → RSS/Atom → Crawl); first non-empty result wins.
2. **Fetch** — WordPress REST `content.rendered` (if WP detected) → HTTP with browser headers → Headless Chromium fallback. Cookies injected into both HTTP and headless paths.
3. **Extract** — `HeuristicContentExtractor` selects the best content container, strips boilerplate, converts to Markdown.
4. **Cache check** — SHA-256 content hash compared against `manifest.json`; unchanged pages are skipped.
5. **Summarize** — `OllamaSummarizer` calls `/api/generate` (temperature 0.2) with a structured prompt template.
6. **Write** — `FileOutputWriter` persists the summary file; `ManifestStore` updates the cache.
7. **Build index** — `LlmsTxtBuilder` generates the `llms.txt` file (sorted by title, deduplicated by filename slug).

## Discovery strategies

Strategies are tried in order; the first one that returns results wins.

| Strategy | When it's used | How it works |
|---|---|---|
| **WordPress REST** | WP sites (auto-detected) | Probes `/wp-json/` and `/?rest_route=/`, fetches `wp/v2/pages` + `wp/v2/posts` with pagination, skips attachments and password-protected posts, caches `content.rendered` in-memory |
| **Sitemap** | Any site with XML sitemaps | Tries `/sitemap.xml`, `/sitemap_index.xml`, `/wp-sitemap.xml`; supports both `sitemapindex` and `urlset` |
| **RSS/Atom** | Feed-enabled sites | Tries `/feed/`, `/rss`, `/rss.xml`, `/feed.xml`; extracts page links from feed items |
| **Crawl** | Fallback for all other sites | BFS crawl from root URL; honors `MaxDepth`, `MaxPages`, `DelayMs`, and same-host filtering |

## Extraction heuristics

- **Preferred containers:** `main` → `article` → `[role='main']` → `.content` / `.entry-content` / `.post-content` → `body`
- **Boilerplate removal:** strips `script`, `style`, `noscript`, `nav`, `footer`, `header`, `aside`
- **Markdown conversion:** ReverseMarkdown with GitHub-flavored output; plain-text fallback if HTML→MD yields empty
- **Skip threshold:** pages with extracted markdown shorter than 50 characters are skipped

## Fetching & protection bypass

The fetch pipeline has three layers:

| Layer | Description |
|---|---|
| **HTTP fetch** | Fast, lightweight `HttpClient` with browser-like headers and automatic gzip/brotli decompression |
| **Headless Chromium** | Automatic fallback when the HTTP response is blocked or too thin (<600 bytes). Uses Playwright with `NetworkIdle` wait and stealth settings (`--disable-blink-features=AutomationControlled`, `navigator.webdriver` removal) |
| **Cookie injection** | Cookies from a Netscape/JSON file are injected into both `HttpClient` (`CookieContainer`) and the Playwright browser context, domain-filtered |

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

When a challenge is detected on the root URL, a warm-up session launches a headless browser to solve it. If successful, the browser session is reused for all subsequent requests.

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

- `SameHostOnly` is set to `true` — only same-host URLs are discovered and processed.
- HTTP client timeout is **90 seconds**.
- Both website fetching and Ollama calls use browser-like HTTP headers.
- Automatic decompression (gzip, deflate, Brotli) is enabled.
- WordPress REST requests include retry with exponential backoff on 429/503 responses.
- Ollama summarization uses temperature **0.2** for deterministic, low-variance output.

## Troubleshooting

### Timeout errors

- Check website responsiveness and network reachability.
- Reduce `MaxPages` for first runs.
- Increase `DelayMs` for rate-limited sites (does not increase HTTP timeout).
- If needed, increase `HttpClient.Timeout` in `Program.cs`.

### Ollama errors

- Ensure Ollama is reachable at the configured base URL.
- Verify the selected model exists (`ollama list`).
- Pull the model if needed (`ollama pull <model-name>`).

### Empty or skipped content

- Some pages are mostly navigation or script-rendered and may be skipped.
- Try specific content URLs instead of generic landing pages.
- For WordPress/Elementor sites, the WP REST API path usually gets real content even when HTML fetch fails.

### Protected / CAPTCHA sites

If you see:

```
Protection detected: SiteGround CAPTCHA (SGCaptcha) — retrying with headless browser...
Tip: supply a cookie file (--cookies) from a real browser session to bypass this protection.
```

1. Visit the site in a real browser and complete the challenge.
2. Export cookies (Netscape `.txt` or JSON) and provide the file path when running the app.
3. Cookies are injected into both HTTP and headless browser requests automatically.

## Project structure

```text
Program.cs                          # Entry point — interactive prompts, DI wiring, run
Core/
  Models/                           # Data records (CrawlOptions, PageContent, Manifest, …)
  Pipeline/
    SummarizationPipeline.cs        # Orchestrates discover → fetch → extract → summarize → write
  Services/
    Discovery/                      # URL discovery strategies (WP REST, Sitemap, RSS, Crawl)
    Extraction/                     # Heuristic HTML → Markdown content extraction
    Fetching/                       # HTTP, Headless Chromium, WP REST content fetchers
    Output/                         # File writer, manifest store, llms.txt builder
    Summarization/                  # Ollama summarizer
    WordPress/                      # WP REST API client
  Utils/                            # Challenge detection, cookie loading, hashing, Playwright session
```

## Development

```bash
dotnet build
dotnet run
```

## Current limitations

- Interactive prompts only (no CLI flags yet)
- Single model provider (Ollama)
- Heuristic extraction may miss content in complex SPA frameworks
- Cookie files must be manually exported from a browser session
- Headless browser adds latency (~5–15s per page) when triggered

---

**Possible next steps:** CLI arguments (`--url`, `--max-pages`, `--cookies`, etc.), external cache source fallback (Google Cache / Wayback Machine), and stealth browser patches for more aggressive bot protection.
