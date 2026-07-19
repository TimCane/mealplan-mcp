FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

# tmux for the claude/dev panes; psql for poking at the scrape tables. The base
# image ships python3-minimal without the stdlib, so pull the full interpreter -
# the ascii-only hook needs it, as does anything that reads the source HARs.
RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
     && apt-get -y install --no-install-recommends tmux postgresql-client python3 \
     && rm -rf /var/lib/apt/lists/*
