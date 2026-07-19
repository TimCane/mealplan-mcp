# Build from the repo root: docker build -f src/Mealplan.Mcp/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props Mealplan.slnx ./
COPY src/Mealplan.Domain/*.csproj src/Mealplan.Domain/
COPY src/Mealplan.Infrastructure/*.csproj src/Mealplan.Infrastructure/
COPY src/Mealplan.Infrastructure.Gousto/*.csproj src/Mealplan.Infrastructure.Gousto/
COPY src/Mealplan.Infrastructure.HelloFresh/*.csproj src/Mealplan.Infrastructure.HelloFresh/
COPY src/Mealplan.Scraper/*.csproj src/Mealplan.Scraper/
COPY src/Mealplan.Mcp/*.csproj src/Mealplan.Mcp/
COPY tests/Mealplan.Tests/*.csproj tests/Mealplan.Tests/
COPY tests/Mealplan.IntegrationTests/*.csproj tests/Mealplan.IntegrationTests/
RUN dotnet restore src/Mealplan.Mcp/Mealplan.Mcp.csproj

COPY . .
RUN dotnet publish src/Mealplan.Mcp/Mealplan.Mcp.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

USER $APP_UID

EXPOSE 5205
ENTRYPOINT ["dotnet", "Mealplan.Mcp.dll"]
