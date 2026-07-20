# Mealplan MCP

An MCP server that serves recipes scraped from HelloFresh and Gousto, so an AI
assistant can build a meal plan from real recipe data.

See [PLAN.md](PLAN.md) for the design.

## Getting started

Open the repo in VS Code and reopen in the devcontainer. Postgres comes up
alongside it and `post-create.sh` restores packages.

```sh
dotnet build
dotnet test
```

Integration tests bring up their own Postgres with Testcontainers and never touch
the network; normalisers run against payloads captured from the source APIs.

Connection string for local development:

```
Host=db;Port=5432;Database=mealplanmcp;Username=mealplanmcp;Password=mealplanmcp
```

## Ports

| Port | Service |
|---|---|
| 5205 | MCP server (`/mcp`, `/health`) |
| 5206 | Scraper: Hangfire dashboard (`/jobs`), `/health` |
| 5432 | Postgres |

## Running the stack

```sh
cp .env.example .env   # fill in WireGuard and Postgres values
docker compose up -d --build
```

Scraper egress goes through a `gluetun` container holding the ProtonVPN config.
The scraper shares gluetun's network namespace, so if the tunnel drops it has no
network at all rather than falling back to your real IP. The MCP server is on the
normal bridge network and never touches the VPN.

Crawls then run themselves. A source with no completed crawl is queued
immediately, so a fresh deployment fills itself rather than serving nothing until
its scheduled night - HelloFresh crawls Sunday 03:00 UTC and Gousto Wednesday
03:00 UTC, so the wait would otherwise be up to a week. A redeploy against an
existing database starts nothing, because the sources already have successful
runs. Set `Sources__<slug>__CrawlOnStartup=false` to deploy quiet.

Normalising runs hourly per source, independently of crawling. Watch both on the
Hangfire dashboard at `/jobs`, which is behind basic auth: `JOBS_USER` and
`JOBS_PASSWORD` from `.env`. Deployed it is routed by the Coolify proxy and is
not published on the host; locally it is <http://localhost:5206/jobs>.

Day to day development and the test suite need neither the VPN nor credentials.

## First run

Before letting a scheduled crawl loose on a few thousand recipes, run a capped
one and check what lands:

```sh
docker compose run --rm scraper crawl gousto --max 5
docker compose run --rm scraper normalize gousto
```

`--max` counts documents, not recipes: a Gousto crawl alternates list pages and
recipe details, so 5 covers a page plus its first few recipes. Omit `--max` for
a full pass. The same works for `hellofresh`.

If HelloFresh returns 403, the VPN exit is the problem, not the code. Cloudflare
blocks some ProtonVPN IPs outright - "Sorry, you have been blocked", with no
challenge to solve, so a headless browser does not help. Reconnect for a
different exit and try again:

```sh
docker compose restart gluetun
docker compose logs gluetun | grep -i "public ip"
```

Check it arrived:

```sh
docker compose exec db psql -U mealplanmcp -c 'SELECT source, count(*) FROM scrape.document GROUP BY source'
```

## Connecting an MCP client

The server speaks streamable HTTP at `http://localhost:5205/mcp`. For Claude
Code:

```sh
claude mcp add --transport http mealplan http://localhost:5205/mcp
```

The tool descriptions served over MCP are canonical; this table is for humans
browsing the repo.

| Tool | Returns |
|---|---|
| `search_recipes` | A page of summaries with the per-portion nutrition panel, rating and allergens split into contains and may-contain-traces. Filters: free text (name, description, ingredients), sources, portions, cuisines, tags, allergens (traces excluded too unless `excludeTraces` is false), ingredients, prep time, nutrient ranges, minimum rating. Sorts by name, rating, kcal, or seeded random. |
| `get_recipe` | Full detail at one portion count: ingredients, steps, utensils, nutrition, serving size, offered portion counts, website URL. A wrong portion count fails naming the counts that work. |
| `list_allergens` | Allergen slugs and display names per source with contains and traces counts. The authoritative input for `excludeAllergens`; `traceCount` is 0 where `hasTraceAllergens` is false - unknown, not none. |
| `list_cuisines` | Cuisine slugs, names and recipe counts per source. |
| `list_tags` | Tag slugs, names and recipe counts per source. Tokens differ per source; feed them to the `tags` filter. |
| `search_ingredients` | Text-filtered ingredient catalogue: what each source calls things, with family where published. Resolves names for `includeIngredients`. |
| `list_sources` | One row per source: recipe count and capability flags. |
| `get_scrape_status` | Last crawl and pending-normalisation counts per source. |

Call `list_sources` first. It reports what each source can and cannot say - most
importantly `hasIngredientQuantities`, which is false for Gousto, because Gousto
ships boxes and publishes no amounts. An absent quantity there means "not
published", not "none needed". Likewise `hasTraceAllergens` is false for Gousto:
an empty `traceAllergens` there means "unknown", not "none". The same rule runs
through every field: null is "not published", and filters exclude recipes
missing the filtered value.

## Adding a recipe source

Add a project under `src/Mealplan.Infrastructure.<Name>/` implementing
`ISourceCrawler`, `ISourceNormalizer`, `ISourceSchema` and `ISourceModule`. It is
discovered by assembly scan; no host, migration or view definition needs
changing. See `Mealplan.Infrastructure.Gousto` for the shape.
