# Mealplan MCP - Plan: expose the stored data

The original build plan (git history, PLAN.md before this branch) shipped in
full: two sources crawling and normalising, union views, four MCP tools. This
plan covers the next phase - surfacing everything the database already holds.

Nothing here needs a re-crawl or a normaliser change. Every field below is
already stored; the gap is projection and tooling.

## The gap

| Stored | Surfaced today |
|---|---|
| 9-nutrient panel per portion, both sources | kcal only |
| Rating average + count, both sources | nothing |
| HelloFresh contains vs may-contain-traces allergen flag | collapsed into one array |
| HelloFresh utensils, serving weight, website URL, ingredient families | nothing |
| Gousto per-100g nutrition panel | nothing (stays that way for now) |
| Allergen / cuisine / tag vocabularies with display names | slugs inside recipe rows only |

## Decisions

1. **Nutrition is per portion, guaranteed by the view.** The shared `v_recipe`
   columns are defined as per-portion values. Both current sources publish per
   portion, so today this is selection, not arithmetic: Gousto contributes its
   `basis = per_portion` row, HelloFresh contributes its published panel. If a
   future source publishes only per-100g plus net weight, dividing is mechanical
   conversion of published data, not invention, and belongs in that source's
   view SQL. Callers never see a mixed basis.
2. **Gousto per-100g stays unsurfaced.** Nothing consumes it yet; it is stored
   and can be projected later without a re-crawl. Not worth widening every read
   model for now.
3. **Traces are not contains.** The allergen array splits in two:
   `allergens` (contains) and `trace_allergens` (may contain traces).
   Gousto does not publish the distinction, so its `trace_allergens` is always
   empty and a capability flag says so - an empty traces list from Gousto means
   "unknown", not "none", and agents must be able to tell.
4. **Filtering excludes traces by default.** `exclude_allergens` matches
   contains OR traces unless the caller relaxes it. Over-excluding is the only
   safe default in an allergen-bearing dataset.
5. **Vocabulary tools carry display names; recipe rows keep slugs.** Rather
   than widening `v_recipe` with parallel name arrays, new list tools return
   slug-to-name mappings with usage counts. Agents filter on slugs and label
   with names.
6. **`max_kcal` is replaced, not kept.** The generalised nutrient filter covers
   it. The tool surface is young and has one consumer; no compatibility shim.
7. **Every list-shaped tool pages, with one envelope.** A shared
   `Page<T>(Items, Total, Skip, Take)` replaces the bespoke `SearchResult` and
   wraps the vocabulary tools too. The consumer is a model with a context
   window, not a UI with a scrollbar: an unpaged 800-row tag list is worse
   than useless, it crowds out the recipes. `take` is capped at 100
   everywhere; vocab tools default to 50 and order by `recipe_count` desc so
   the first page is the one that matters. `Total` always reports the full
   count, so an agent knows it saw a page and not the world.
   `list_sources` and `get_scrape_status` stay unpaged - they return one row
   per source by construction.
8. **Prompts, not resources.** The server ships MCP prompts (parameterised
   flow templates, surfaced as slash-commands in clients) but no MCP
   resources: agents drive this server through tools, and a resource surface
   would duplicate `get_recipe` for nothing.
9. **The server stays a stateless read layer.** No plan storage. The one
   compute tool, `get_shopping_list`, aggregates ingredient rows for chosen
   recipes but does not merge across recipes or sources - cross-source
   ingredient identity stays deliberately unsolved, and the calling model
   does the merging. Rows are tagged by recipe so nothing is ambiguous.

## View changes

### `v_recipe` - new columns

All per portion. Each source's `RecipeViewSql` contributes them; the canonical
list in `RecipeViewColumns` (ISourceSchema.cs) and the empty-view typing in
`UnionViewBuilder` grow to match.

| Column | Gousto | HelloFresh |
|---|---|---|
| `energy_kj` | `n.energy_kj` | pivot `'Energy (kJ)'` |
| `fat_g` | `n.fat_grams` | pivot `'Fat'` |
| `saturates_g` | `n.saturated_fat_grams` | pivot `'of which saturates'` |
| `carbs_g` | `n.carbs_grams` | pivot `'Carbohydrate'` |
| `sugars_g` | `n.sugars_grams` | pivot `'of which sugars'` |
| `fibre_g` | `n.fibre_grams` | pivot `'Dietary Fibre'` |
| `protein_g` | `n.protein_grams` | pivot `'Protein'` |
| `salt_g` | `n.salt_grams` | pivot `'Salt'` |
| `serving_size_g` | `n.net_weight_grams` | `r.serving_size_grams` |
| `rating_avg` | `r.rating_average` | `r.average_rating` |
| `rating_count` | `r.rating_count` | `r.ratings_count` |
| `trace_allergens` | `ARRAY[]::text[]` | lateral agg where `traces_of` |
| `website_url` | derived from slug | `r.website_url` |

- HelloFresh pivot: one lateral join,
  `SELECT max(amount) FILTER (WHERE name = '...') AS ...` per nutrient,
  replacing the current single-row `'Energy (kcal)'` join. Row names pinned to
  the fixture-verified strings above.
- HelloFresh `allergens` lateral gains `WHERE NOT ra.traces_of`; a sibling
  aggregate builds `trace_allergens`.
- Gousto `website_url`: the slug is taken from the recipe URL at normalise
  time, so `'https://www.gousto.co.uk/cookbook/recipes/' || r.slug`
  reconstructs it. Verify the pattern against the committed fixtures during
  implementation; if the payload URL differs, store it instead (migration +
  re-normalise) rather than guessing.
- `search_text` gains the recipe's distinct ingredient names (aggregated
  string per source). Free text then finds recipes by what is in them -
  "lemongrass curry" currently misses every recipe where lemongrass is only
  an ingredient. `includeIngredients` stays for strict must-contain filtering.

### Performance gate

Search computes `to_tsvector` plus five LATERAL aggregates per row over
roughly 75k view rows, twice per query (count + page), with no supporting
indexes. PR 1's integration tests time a representative search against a
realistic row count (fixtures multiplied). Under ~300ms the plain view
ships; over, `v_recipe` becomes a materialized view refreshed at startup and
after each normalise run. The fallback is designed now, built only if the
number demands it.

### New vocabulary views

`ISourceSchema` gains one SQL property per view, same pattern as
`RecipeViewSql`. `UnionViewBuilder` unions them.

| View | Columns |
|---|---|
| `v_allergen` | source, slug, name, recipe_count, trace_count |
| `v_cuisine` | source, slug, name, recipe_count |
| `v_tag` | source, slug, name, recipe_count |
| `v_ingredient` | source, name, family, recipe_count |

- Gousto contributes its categories to `v_tag` (matching how `v_recipe.tags`
  already presents them) and nothing to `family` in `v_ingredient`.
- `trace_count` is 0 for Gousto; the capability flag explains why.

## Capability changes

`SourceCapabilities` gains:

| Flag | Gousto | HelloFresh |
|---|---|---|
| `HasTraceAllergens` | false | true |
| `HasUtensils` | false | true |

Both surface through `list_sources` and `RecipeDetail` notes. The Gousto
caveat text extends to state that trace allergens are not published.

## Read model changes

New record, all fields nullable, all per portion:

```
NutritionPanel(Kcal, Kj, FatGrams, SaturatesGrams, CarbsGrams, SugarsGrams,
               FibreGrams, ProteinGrams, SaltGrams)
```

- `RecipeSummary` adds: `Nutrition` (full panel - agents pick recipes on
  macros without N `get_recipe` calls), `RatingAverage`, `RatingCount`,
  `TraceAllergens`. The bare `Kcal` field is absorbed into the panel.
- `RecipeDetail` adds the same, plus `ServingSizeGrams`, `WebsiteUrl`,
  `Utensils` (per-source read like pantry items, empty + flagged for Gousto),
  `OfferedPortions` (every portion count the recipe is offered at), and
  `UpdatedAt` (when the recipe last changed upstream).
- `get_recipe` failures become distinguishable: an unknown id is not-found;
  a known id at an unoffered portion count returns an error naming the
  offered counts, so the agent learns the fix from the failure.
- `SourceInfo` and `SourceNotes` add the two new flags.
- `Page<T>(Items, Total, Skip, Take)` (decision 7) carries every list
  response: `Page<RecipeSummary>` retires `SearchResult`, and the vocabulary
  tools return `Page<AllergenInfo>` etc. rather than bare lists.
- `ShoppingListRow(RecipeName, Source, Ingredient, Amount, Unit,
  IsPantryItem)` backs `get_shopping_list`; a null amount carries the same
  meaning as everywhere else - not published, not zero.

## Search changes

`search_recipes` parameter changes:

- `nutrientFilters`: array of `{nutrient, min?, max?}` with `nutrient` an enum
  of `kcal | kj | fat | saturates | carbs | sugars | fibre | protein | salt`.
  Values are grams per portion (kcal/kj in their own units). A filter on a
  nutrient excludes recipes with no published value for it - same rule as
  `maxPrepMinutes` today. Replaces `maxKcal`.
- `excludeTraces` (default `true`): whether `excludeAllergens` also matches
  may-contain-traces. Setting it false narrows to confirmed contains only.
- `excludeIngredients`: dislikes ("no mushrooms"). Same ILIKE substring
  semantics as `includeIngredients`; any match excludes. Over-excluding is
  the right direction for dislikes as it is for allergens.
- `maxTotalMinutes`: same null-excludes rule as `maxPrepMinutes`. Difficulty
  stays output-only: it is HelloFresh-only, so under the null-excludes rule a
  difficulty filter would silently drop every Gousto recipe.
- `minRating`: recipes with no rating are excluded when set.
- `sort`: `name | rating | kcal | random`. Default `name` (current
  behaviour). `rating` orders desc with count as tie-break; `kcal` asc.
  `random` takes an optional integer `seed` (setseed-based ordering) so
  paging is stable within a seed and a new seed reshuffles - agents pass
  e.g. the ISO week number and each week's plan draws from a fresh page one
  instead of the same alphabetical top slice.

SQL side: `RecipeQueryService` builds the nutrient clauses from a whitelist
map of enum to column - the enum never reaches the SQL string.

## New tools

All four page per decision 7 (`skip`/`take`, `recipe_count` desc, take
capped at 100) and accept an optional `sources` filter.

| Tool | Purpose |
|---|---|
| `list_allergens` | Every allergen slug + display name per source, with recipe and traces counts. The authoritative input for `excludeAllergens` - agents check it before filtering so a misspelt slug does not silently match nothing. Allergen vocabularies are small enough that page one is normally the whole list. |
| `list_cuisines` | Cuisine slugs + names + counts, for `cuisines`. |
| `list_tags` | Tag slugs + names + counts. Also becomes a `tags` filter on `search_recipes` (cheap: column already in the view). |
| `search_ingredients` | Text-filtered ingredient catalogue: source, name, family, recipe count. Lets agents resolve "garlic" to what each source actually calls it before using `includeIngredients`. |
| `get_shopping_list` | Takes `[{source, recipeId, portions}]` (up to ~14 refs), returns flat rows of `{recipe, source, ingredient, amount, unit, isPantryItem}` - shipped ingredients plus Gousto pantry items flagged, since a shopper must know what the box will not contain. No steps, no nutrition, no merging (decision 9). Not paged: bounded by the ref cap. |

There is no batch `get_recipe`: planning works off summaries (which carry the
nutrition panel), shopping off `get_shopping_list`, and cooking fetches one
recipe at a time.

Tool descriptions state the safety semantics explicitly: traces default,
Gousto's unknown-traces caveat, null-excluding filter behaviour, and that
`total` may exceed the page returned.

## MCP prompts

Registered via `WithPrompts` alongside the tools (decision 8). Each renders a
parameterised instruction template:

| Prompt | Args | Flow it encodes |
|---|---|---|
| `plan_week` | people, nights, allergens, dislikes, kcal/protein targets | `list_sources` for capabilities, `list_allergens` to resolve slugs, `search_recipes` with `excludeAllergens` + `nutrientFilters` + seeded random sort, finish with `get_shopping_list`. |
| `find_recipe` | craving free text, constraints | Lighter single-recipe flow: resolve constraints, search, end in `get_recipe`. |
| `whats_available` | none | Orientation: sources, counts, capability caveats, top cuisines and tags via the vocab tools. |

The prompts are the documentation of record for the safe flow - allergen slug
resolution before filtering, traces semantics, portion validity.

## MCP capabilities

Beyond prompts, three capability upgrades ride along:

- **Server instructions.** `ServerInstructions` text delivered at initialize,
  before any tool call - the one channel guaranteed to reach every agent.
  A short paragraph: resolve allergen slugs via `list_allergens` before
  filtering, traces semantics, null means not-published, portion counts vary
  by source, page one is not the world. Written incrementally - each PR
  extends it to match the surface it ships.

- **Tool annotations.** Every tool is marked `ReadOnly = true`,
  `Idempotent = true`, `OpenWorld = false` on its `[McpServerTool]`
  attribute. Clients use the hints for permission UX - a read-only tool can
  be auto-approved instead of prompting per call, which is the correct
  treatment for a server that cannot write.
- **Structured output.** `UseStructuredContent = true` on every tool, so
  results return as `structuredContent` with an output schema generated from
  the DTOs rather than JSON-in-a-text-block. The schema also encodes which
  fields are nullable, reinforcing the null-means-not-published contract.
- **Completions.** A completion handler backs the prompt arguments: allergen,
  cuisine and source args autocomplete from the vocab views as the user
  types. Thin, since `v_allergen` and friends are exactly the needed lookup.

Not adopted: sampling (a read layer generates nothing), elicitation (every
input is a plain argument), logging notifications (`get_scrape_status`
answers freshness), `list_changed` (the tool list is static), resources
(decision 8).

## Usage audit

Analytics, not a security trail: which tools, filters and prompts get used,
what returns nothing, what errors agents hit. Rows are droppable - writes are
fire-and-forget and never fail or slow a call.

### Identity - semi-anonymous by construction

There is no auth and none is added. "Who" is what the transport provides:

- `audit.session` - one row at first contact per MCP session: session id,
  first_seen_at, client name and version (from the initialize handshake),
  user agent, and `sha256(ip + daily-rotating salt)`.
- Within a session, ordering `tool_call` rows by time is the trace of one
  agent walking the surface. Across days, linking a person is impossible by
  construction: the salt rotates daily and is never stored.
- Because identity degrades by design, args are stored verbatim - the
  health-adjacent filters (allergens, nutrients) cannot be tied to a person
  beyond one day. This trade is deliberate and this line documents it.

### Schema and capture

```
audit.session   (id, first_seen_at, client_name, client_version,
                 user_agent, ip_hash)
audit.tool_call (id, session_id, kind, name, args jsonb, called_at,
                 duration_ms, result_count, is_error, error_kind)
```

- `kind` is `tool` or `prompt`. Completions are not logged: per-keystroke
  volume, partial typing, no insight.
- `result_count` is the page row count (null hit/miss for `get_recipe`), so
  zero-result searches - the strongest surface-improvement signal - are
  queryable directly.
- Implemented once via the SDK's call filter, not per tool: capture, enqueue
  to a channel, background writer inserts. The MCP host creates the `audit`
  schema idempotently at startup alongside the views.
- Retention: a daily hosted-service sweep deletes rows older than 90 days.
  No dashboard and no MCP tool - this is read with SQL.

## Hardening

The server is public and unauthenticated, and every search reaches Postgres.
ASP.NET's built-in rate limiter, fixed window per client IP (the forwarded
header is already configured), set generously - around 120 requests a minute
- so no real agent ever sees a 429 but a misbehaving loop cannot hammer the
database. No new dependency.

## Delivery

Five PRs, each leaving main working. PR 5 is independent of the rest and can
merge first - a baseline week of current-surface usage would show what PRs
1-4 change:

| # | PR | Contents |
|---|---|---|
| 1 | `feat: per-portion nutrition and ratings through the shared views` | v_recipe columns, ingredient-aware search_text, performance gate, NutritionPanel, nutrientFilters, minRating, sort, maxKcal removal, Page envelope replacing SearchResult, tool annotations + structured output, server instructions, offeredPortions/updatedAt/portion-error on detail, MCP client round-trip test |
| 2 | `feat: distinguish trace allergens from contains` | allergen split, capability flags, excludeTraces, utensils on detail |
| 3 | `feat: vocabulary tools` | four vocab views, four tools, tags filter |
| 4 | `feat: shopping list, prompts and planning filters` | get_shopping_list, the three prompts, prompt-argument completions, excludeIngredients, maxTotalMinutes, seeded random sort |
| 5 | `feat: usage audit for the mcp surface` | audit schema, call filter + background writer, salt rotation, retention sweep, rate limiter |

Each PR that changes the surface also updates a terse tools table in the
README - the MCP-native descriptions stay canonical; the table is for humans
browsing the repo.

## Tests

- Integration (Testcontainers): view projection per source against normalised
  fixture data - the HelloFresh pivot names and Gousto basis selection are the
  riskiest lines in this plan and get explicit assertions. Nutrient range,
  rating and traces filter behaviour, including the null-excludes rule.
- Unit: query clause building from `nutrientFilters` (whitelist mapping,
  min/max combinations), read model mapping, paging envelope (take clamp,
  total vs page size, count-desc ordering).
- Integration: seeded random sort is stable across pages within a seed and
  differs across seeds; `get_shopping_list` returns pantry rows flagged for
  Gousto and measured rows for HelloFresh; prompt templates render with all
  argument combinations; completions return live slugs from the vocab views.
- Audit: one session row per MCP session however many calls; a failing or
  slow audit write never fails or delays the tool call; the sweep deletes
  only rows past retention; same IP hashes differently across salt days.
- Protocol round-trip: one integration test hosts the real server in-process
  and drives it with the SDK's McpClient - initialize (instructions and
  capabilities), tools/list schemas, one call per tool, one render per
  prompt. The serialization contract agents actually consume, tested where
  nothing else covers it.
- Performance: the PR 1 benchmark doubles as a regression test - search
  against the multiplied fixture set must stay under the gate.
- Fixture-driven normaliser tests are untouched - no normaliser changes.

## Open questions

- Whether Gousto's per-100g panel earns a place on `RecipeDetail` once a
  consumer wants it (decision 2 defers, does not reject).
