# Mealplan MCP - Plan

An MCP server that serves recipes scraped from UK meal-kit sites, so a calling
AI can build a meal plan from real recipe data.

Two hosts share one Postgres database:

- **Scraper** - background jobs that crawl source APIs and normalise the results.
  Runs behind a VPN.
- **MCP** - a read-only MCP server over the normalised data. Direct network.

## Principles

1. **Scrape raw, normalise separately.** Every response lands in `scrape.document`
   as jsonb, keyed by a content hash. Normalisation is a second, independent job.
   Re-scraping an unchanged recipe costs one hash comparison and no writes.
2. **No cross-source normalised tables.** Each source owns a Postgres schema and
   models its own real shape. `hellofresh.ingredient` and `gousto.ingredient` are
   deliberately unrelated. Cross-source reads happen through union views only.
3. **A new source is a new project.** It implements three interfaces and is
   discovered by DI scan. No edits to the domain, scraper or MCP hosts.
4. **Never invent data.** Where a source lacks a field, it stays null and the
   gap is reported through source capability flags.

## Sources

### Gousto

`https://production-api.gousto.co.uk/cmsreadbroker/v1/` - public, no auth.

| Endpoint | Use |
|---|---|
| `/recipes?category=recipes&limit=16&offset=N` | list pages, offset paging |
| `/recipe/{slug}` | full detail |
| `/category/recipes`, `/themes` | taxonomy |

The list response is thin (`uid, title, url, media, prep_times, portion_sizes,
rating`), so the crawl is two phase: page the list to discover slugs, then fetch
each detail. List pages are stored as `recipe_summary` documents and act as cheap
change detection.

Gousto quirks:

- Ingredients are box SKUs with **no amounts**. Only `in_box` counts per portion
  size, under `portion_sizes[].ingredients_skus[]`.
- Pantry items are separate, in `basics[]`.
- Allergens are nested inside each ingredient, not listed at recipe level.
- `prep_times` varies by portion count: `{for_2: 40, for_4: 45}`.

### HelloFresh

`https://www.hellofresh.co.uk/gw/recipes/recipes/search?country=GB&locale=en-GB&skip=N&take=8`

The search response returns **complete** recipe objects (ingredients, yields,
steps, nutrition, allergens, utensils, tags), so no detail call is needed. One
paginated pass over ~3,880 GB recipes is the whole crawl.

Requires `Authorization: Bearer <jwt>` plus `x-requested-by: organic-growth`. The
token is anonymous (`iss: senf`, no user claim, roughly 30 day expiry) and is
embedded in the payload of `https://www.hellofresh.co.uk/recipes`. The crawler
fetches that page with a browser user agent, extracts the token, caches it until
`exp - 1 day`, and refetches once on a 401/403 before failing the run.

Cloudflare bot management is in front of this host, so the crawler keeps a cookie
container across the run and stays serial and slow.

HelloFresh quirks:

- Quantities live in `yields[]` per portion count, referencing `ingredients[]` by
  id: `{yields: 2, ingredients: [{id, amount, unit}]}`.
- `prepTime` / `totalTime` are ISO 8601 durations (`PT20M`).
- Nutrition is a typed array, per portion.

## Architecture

```
src/
  Mealplan.Domain/                    entities, enums, source interfaces
  Mealplan.Infrastructure/            EF contexts, Hangfire, HTTP, normalise jobs
  Mealplan.Infrastructure.HelloFresh/ crawler + normaliser + schema
  Mealplan.Infrastructure.Gousto/     crawler + normaliser + schema
  Mealplan.Scraper/                   worker host      (behind VPN)
  Mealplan.Mcp/                       MCP server host  (direct network)
tests/
  Mealplan.Tests/                     xUnit, normalisers against real fixtures
  Mealplan.IntegrationTests/          Testcontainers Postgres
```

Dependencies flow inward. `Domain` references nothing.

### Source contract

A source project implements three interfaces and is picked up by an assembly
scan of `Mealplan.Infrastructure.*`:

```csharp
public interface ISourceCrawler
{
    string Source { get; }
    IAsyncEnumerable<RawDocument> CrawlAsync(CrawlRequest request, CancellationToken ct);
}

public interface ISourceNormalizer
{
    string Source { get; }
    Task NormalizeAsync(RawDocument document, CancellationToken ct);
}

public interface ISourceSchema
{
    string Source { get; }
    string Schema { get; }
    SourceCapabilities Capabilities { get; }
    string RecipeViewProjection { get; }
}
```

`CrawlAsync` yields documents lazily, so Gousto's two phase crawl and
HelloFresh's single pass both fit without a shared page template. Paging, auth,
rate limiting and retry policy are the source's own business.

### Raw layer - schema `scrape`

```
scrape.run       (id, source, kind, started_at, finished_at, status, stats jsonb,
                  cursor jsonb, error)
scrape.document  (id, source, doc_type, source_key, version,
                  payload jsonb, content_hash bytea,
                  first_seen_at, last_seen_at, run_id,
                  normalized_at, normalize_error)
```

`doc_type` is one of `recipe_summary`, `recipe`, `taxonomy`.

On each fetch, the payload is hashed:

| Case | Action |
|---|---|
| hash matches latest version | `UPDATE last_seen_at` only, no normalisation |
| hash differs | `INSERT` at `version + 1`, queue normalisation |
| never seen | `INSERT` at version 1, queue normalisation |

`scrape.run.cursor` holds the last completed page so an interrupted crawl
resumes rather than restarting.

### Normalised layer - one schema per source

Per source, e.g. `hellofresh`:

```
recipe                   (id, source_key, name, headline, description,
                          difficulty, total_minutes, image_url, search_vector)
recipe_yield             (recipe_id, portions, prep_minutes, is_offered)
recipe_yield_ingredient  (recipe_yield_id, ingredient_id, amount, unit)
ingredient               (id, source_key, name, family)
recipe_step              (recipe_id, index, instruction)
recipe_pantry_item       (recipe_id, name)
allergen / recipe_allergen
nutrition                (recipe_id, portions, type, name, amount, unit)
cuisine / recipe_cuisine
tag / recipe_tag
```

Both sources are portion dimensioned, so quantities hang off `recipe_yield`
rather than `recipe`. Gousto rows carry null `amount` and `unit` - it has no
quantity data and none is invented.

Each source gets its own `DbContext` with its own migration history table inside
its own schema, so adding a source never collides with another's migrations.
`ScrapeDbContext` owns the `scrape` schema and the public views.

### Cross-source reads - union views

```sql
CREATE VIEW public.v_recipe AS
  SELECT 'hellofresh' AS source, id, name, headline, total_minutes,
         portions, prep_minutes, search_vector
    FROM hellofresh.recipe JOIN hellofresh.recipe_yield ...
  UNION ALL
  SELECT 'gousto', ... FROM gousto.recipe JOIN gousto.recipe_yield ...;
```

Views default to 2 portions unless a portion count is requested.

The views are not written into a migration. Each source contributes its own
`SELECT` through `ISourceSchema`, and the views are rebuilt from every registered
source at MCP startup. A migration would have to be edited for each new source -
exactly the coupling per-source schemas exist to avoid. Adding a source therefore
costs nothing here at all.

Text search runs `to_tsvector` over the view's `search_text`, with a `pg_trgm`
word-similarity fallback for typos. The original plan called for a stored
generated `tsvector` column with a GIN index per source; at a few thousand
recipes the computed version is fast enough and keeps the source schemas simpler.
Add the stored column and index when the row count makes it worth it.

## MCP surface

Streamable HTTP, read only.

| Tool | Purpose |
|---|---|
| `search_recipes` | query, sources, maxPrepMinutes, portions, cuisines, excludeAllergens, includeIngredients, kcalMax, skip, take |
| `get_recipe` | full normalised recipe as a stable DTO, same shape for every source |
| `list_sources` | recipe counts and capability flags |
| `get_scrape_status` | last run per source, freshness |

Capability flags exist so the calling model knows what a source cannot tell it:

```json
[
  {"source": "hellofresh", "hasIngredientQuantities": true,  "hasPantryItems": false},
  {"source": "gousto",     "hasIngredientQuantities": false, "hasPantryItems": true}
]
```

Meal plans are not persisted. The calling AI holds the plan; this server is a
read layer over recipe data.

## Scraping operations

Hangfire with Postgres storage, so jobs, retries and history live in the same
database and there is a dashboard to watch and requeue crawls.

```
RequestDelay    00:00:02 plus 50% jitter
MaxConcurrency  1 per source
PageSize        8 (HelloFresh) / 16 (Gousto)
Schedule        0 3 * * 0   Sunday 03:00
MaxRetries      5, exponential backoff capped at 5 minutes
```

A full HelloFresh pass is roughly 485 requests, about 20 minutes at this pace.

### VPN

All scraper egress goes through a `gluetun` container holding the ProtonVPN
WireGuard config. The scraper joins its network namespace:

```yaml
scraper:
  network_mode: "service:gluetun"
```

If the tunnel drops, the scraper has no network at all - the killswitch is
structural, not something the application has to remember. The MCP host stays on
the normal bridge network and never touches the VPN.

In the devcontainer, `gluetun` sits behind a compose profile, so day to day work
needs neither VPN credentials nor `NET_ADMIN`. Enable the profile only to run a
live crawl:

```sh
docker compose --profile vpn up -d
```

Tests never hit the network. Normalisers are tested against real payloads
captured from the source APIs and committed as fixtures.

## Configuration

Compose reads a gitignored `.env`; `.env.example` documents every key with dummy
values. Application config uses the Options pattern, bound from environment
variables in containers and user-secrets locally. Startup fails loudly on a
missing required setting.

## Delivery

Each PR leaves `main` working and is independently reviewable. Gousto lands first
because it needs no auth, which proves the source abstraction; HelloFresh then
stresses it with token handling and anti-bot measures.

| # | PR |
|---|---|
| 1 | `chore: devcontainer and project plan` |
| 2 | `feat: solution skeleton, build props and CI` |
| 3 | `feat: scrape schema and raw document store` |
| 4 | `feat: source abstractions and scraper host` |
| 5 | `feat: gousto crawler, normaliser and schema` |
| 6 | `feat: hellofresh crawler, normaliser and schema` |
| 7 | `feat: union views and mcp read tools` |
| 8 | `feat: gluetun compose and deploy docs` |

## Open questions

- ProtonVPN WireGuard config and preferred exit country.
- Deployment host for the two containers.
- Whether persisted meal plans and shopping lists are wanted later. That needs
  cross-source ingredient identity, which today's schema deliberately omits.
