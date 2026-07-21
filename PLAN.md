# Plan

Two independent workstreams. The link work lands first: it is small and ships
value on the sources already in the database.

1. Recipe links on the MCP surface.
2. Grafana for analytics and alerting.

---

## 1. Recipe links on the MCP surface

`website_url` is already in the `v_recipe` column contract and populated by both
sources - Gousto from `seo.canonical`, HelloFresh from the payload's
`websiteUrl`. It reaches the wire on `RecipeDetail` only. `search_recipes`
returns none, so an agent that wants to cite a link has to call `get_recipe`
once per candidate.

### Changes

- `RecipeSummary` gains `WebsiteUrl`. Add `r.website_url` to the search
  projection in `RecipeQueryService.SearchAsync` and read it in the row mapper.
  The column is already in the union view, so no source, migration or view
  change is needed.
- `ServerInstructions` carries the rule. It is delivered at initialize, before
  any tool call - the one channel guaranteed to reach every agent, whether or
  not it ever invokes a prompt.
- Tool descriptions in `RecipeTools` repeat it where the field appears.
  `websiteUrl` is the source's own recipe page, to be presented as the link, and
  never constructed from the slug, id or external key: a guessed URL is a broken
  link with the source's name on it. Where `websiteUrl` is null, the agent names
  the source in text instead.
- `PlanningPrompts` say it at the point of presentation. `plan_week` links each
  pick's name to its `websiteUrl` when presenting the week for approval;
  `find_recipe` links the two or three offers and the chosen recipe;
  `whats_available` is untouched - it presents sources, not recipes.
- `ShoppingListRow` stays without a URL. The shopping list is keyed by recipe
  name and source, and the agent already holds the search results that carry the
  link.
- README's tool table gains the field.

### Tests

- `McpRoundTripTests`: a search hit carries `websiteUrl` over the wire.
- `PlanningPromptTests`: the rendered `plan_week` and `find_recipe` templates
  ask for links and carry the never-invent rule.

---

## 2. Grafana

History, trends and alerting. Live status stays where it is - the Hangfire
dashboard on 5206 and `get_scrape_status`.

### Dashboards

Ten, in three groups.

**Usage**, over `audit.tool_call` and `audit.session`:

- *Overview* - calls by tool and by prompt, over time and in total; split of
  tool calls against prompt renders.
- *Query shapes* - which filters callers actually reach for, read out of `args`:
  how often `excludeAllergens` is set and whether `excludeTraces` is ever
  relaxed, portions requested, sort chosen, paging depth, whether
  `nutrientFilters` is used at all. The panel that justifies the dashboard is
  the **guessed-slug rate**: the allergen, cuisine and tag slugs passed in
  `args`, joined against `v_allergen`, `v_cuisine` and `v_tag`, counting the
  ones that match nothing. The surface is built around the warning that a
  guessed slug silently matches nothing, and this is the only way to see it
  happening.
- *Failure modes* - zero-result searches by tool and by argument shape,
  `error_kind` breakdown, rate of wrong-portion-count `get_recipe` calls.
  Zero-result searches are the strongest surface-improvement signal and the
  reason the audit stores `result_count`.
- *Latency* - p50, p95 and p99 of `duration_ms` per tool, against `result_count`
  and the requested `take`.
- *Clients* - sessions over time, client name and version mix, calls per
  session, distinct `ip_hash` per day.

Aggregate shapes only. No panel reads the free-text `query` out of `args`: a
top-searches panel would turn an operations dashboard into a log of what people
typed, held for over a year.

**Scrape**, over `scrape.run` and `scrape.document`:

- *Health* - run outcomes per source, documents fetched per run, current
  normalisation backlog, last error.
- *Freshness and churn* - `documents_changed` against `documents_fetched` per
  run, document version distribution, normalisation lag
  (`normalized_at - first_seen_at`), `normalize_error` counts.

**Catalogue**, over the public union views:

- *Contents* - recipe counts per source, cuisine and tag distribution, allergen
  coverage, nutrition spread. `v_recipe` is materialized, so these are cheap.
- *Gaps* - null coverage per field per source: recipes with no kcal, no prep
  time, no rating, no `website_url`. The numeric counterpart to the capability
  flags, and the thing that says whether a flag is telling the truth.
- *Database growth* - table, index and matview sizes over time. Needs no extra
  grants; `pg_total_relation_size` does not require SELECT on the relation.

No Hangfire dashboard. The dashboard on 5206 is the tool built for that job, and
`scrape.run` already carries the outcome that matters - so the grafana role
never needs a grant on the hangfire schema.

Alerts on scrape staleness: age of the last successful run per source, and a
normalisation backlog that stops draining.

### Access

A dedicated `grafana` role with SELECT on the public union views, the `audit`
tables and the `scrape` tables, created idempotently at startup alongside
`AuditSchema`. Source schemas stay unreadable, so a new source reaches Grafana
through the union views and still needs no edits outside its own project. The
role can never write: a Grafana query editor is an arbitrary SQL console, and
anyone who reaches the UI would otherwise have write access to everything.

Its password is a new required compose variable.

### Deployment

Same shape as the MCP server: `expose` only with no host port, `SERVICE_FQDN` so
Coolify routes it, admin password required, anonymous access off. Always up
rather than behind a profile - scheduled alerts only fire while it runs.

### Provisioning

Everything is loaded from the repo at container start; nothing is built in the
UI. `grafana/` is mounted at `/etc/grafana/provisioning` and
`/var/lib/grafana/dashboards`:

    grafana/
      provisioning/
        datasources/postgres.yaml
        dashboards/dashboards.yaml
        alerting/scrape.yaml
      dashboards/
        usage-overview.json
        usage-query-shapes.json
        usage-failures.json
        usage-latency.json
        usage-clients.json
        scrape-health.json
        scrape-freshness.json
        catalogue-contents.json
        catalogue-gaps.json
        database-growth.json

Details that decide whether this works on a cold container:

- The datasource declares a fixed `uid` and every panel references it by that
  uid. Grafana generates a random one otherwise, and dashboards provisioned
  against a generated uid bind to no datasource on a fresh volume.
- Its password comes from the env var, not the file, so the committed yaml holds
  no secret. `sslmode=disable` - the connection never leaves the compose
  network.
- The dashboard provider sets `allowUiUpdates: false`. The repo is the source of
  truth, so a UI edit cannot be silently discarded by the next restart; changing
  a dashboard means changing its JSON.
- Alert rules are provisioned too. Rules created in the UI live only in
  Grafana's own database and would not survive a rebuild.
- Dashboard JSON is ASCII, per the repo rule - Grafana's own exports will
  happily put unicode in panel titles.

### Retention

`AuditOptions.RetentionDays` goes from 90 to 400, so a panel can compare a month
against the same month a year earlier. The address hash is salted daily and the
salt is not kept, so old rows cannot be linked to a person, or to each other
across days, however long they live.

---

## Delivery

1. Links on the MCP surface.
2. Grafana, on its own branch.
