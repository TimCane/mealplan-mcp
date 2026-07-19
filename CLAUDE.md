# Mealplan MCP

An MCP server serving recipes scraped from UK meal-kit sites. See PLAN.md for the
full design.

## Architecture decisions

These are settled. Do not re-litigate them without a reason that is written down.

- **Two hosts, shared libraries.** `Mealplan.Scraper` runs behind the VPN,
  `Mealplan.Mcp` does not. They share `Mealplan.Domain` and
  `Mealplan.Infrastructure`.
- **Raw first, normalise second.** Everything scraped lands in `scrape.document`
  as jsonb with a content hash. A separate job normalises it. An unchanged
  payload costs a hash comparison and nothing else.
- **No cross-source normalised tables.** Each source owns a Postgres schema and
  models its own shape. `hellofresh.ingredient` and `gousto.ingredient` are
  unrelated on purpose. Cross-source reads go through union views in `public`.
- **A source is a project.** Implement `ISourceCrawler`, `ISourceNormalizer` and
  `ISourceSchema`; DI scan picks it up. Adding a source must not require edits to
  Domain, Scraper or Mcp.
- **Never fabricate recipe data.** Gousto has no ingredient quantities. Those
  columns stay null and `list_sources` reports the gap through capability flags.
  Estimating amounts in an allergen-bearing dataset is not acceptable.
- **Per-source DbContext.** Each source has its own migration history table in
  its own schema, so migrations never serialise across sources.

## Conventions

- House style in `~/.claude/CLAUDE.md` applies. Layering follows the
  `dotnet-style` skill.
- This repo is **ASCII only** - enforced by `.claude/hooks/ascii-only-lint.py`.
  Test fixtures under `tests/**/Fixtures/` are exempt; they hold real upstream
  payloads with accented recipe names.
- `tmp/` is gitignored and holds the source HAR captures. They contain live
  session cookies and a bearer token - never commit them.

## Scraping

- Serial, delayed, resumable. Politeness settings live in `SourceOptions`.
- Tests never hit the network. Normalisers run against committed fixtures.
- Live crawls need the VPN profile: `docker compose --profile vpn up -d`.
