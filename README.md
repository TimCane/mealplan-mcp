# Mealplan MCP

An MCP server that serves recipes scraped from HelloFresh and Gousto, so an AI
assistant can build a meal plan from real recipe data.

See [PLAN.md](PLAN.md) for the design.

## Getting started

Open the repo in VS Code and reopen in the devcontainer. Postgres comes up
alongside it and `post-create.sh` restores packages.

Connection string for local development:

```
Host=db;Port=5432;Database=mealplanmcp;Username=mealplanmcp;Password=mealplanmcp
```

## Ports

| Port | Service |
|---|---|
| 5205 | MCP server |
| 5206 | Hangfire dashboard |
| 5432 | Postgres |

## Running a live crawl

Scraping egresses through a `gluetun` container holding the ProtonVPN config, so
the scraper has no network at all if the tunnel drops. It sits behind a compose
profile and is off by default:

```sh
cp .env.example .env   # fill in the WireGuard values
docker compose --profile vpn up -d
```

Day to day development and the test suite need neither the VPN nor credentials.
