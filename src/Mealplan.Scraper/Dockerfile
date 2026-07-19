# Build from the repo root: docker build -f src/Mealplan.Scraper/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the manifests alone, so a code-only change reuses the layer.
COPY Directory.Build.props Directory.Packages.props Mealplan.slnx ./
COPY src/Mealplan.Domain/*.csproj src/Mealplan.Domain/
COPY src/Mealplan.Infrastructure/*.csproj src/Mealplan.Infrastructure/
COPY src/Mealplan.Infrastructure.Gousto/*.csproj src/Mealplan.Infrastructure.Gousto/
COPY src/Mealplan.Infrastructure.HelloFresh/*.csproj src/Mealplan.Infrastructure.HelloFresh/
COPY src/Mealplan.Scraper/*.csproj src/Mealplan.Scraper/
COPY src/Mealplan.Mcp/*.csproj src/Mealplan.Mcp/
COPY tests/Mealplan.Tests/*.csproj tests/Mealplan.Tests/
COPY tests/Mealplan.IntegrationTests/*.csproj tests/Mealplan.IntegrationTests/
RUN dotnet restore src/Mealplan.Scraper/Mealplan.Scraper.csproj

COPY . .
RUN dotnet publish src/Mealplan.Scraper/Mealplan.Scraper.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Non-root: the scraper only makes outbound requests and writes to Postgres.
USER $APP_UID

EXPOSE 5206
ENTRYPOINT ["dotnet", "Mealplan.Scraper.dll"]
