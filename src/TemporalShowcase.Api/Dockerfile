FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TemporalShowcase.Contracts/TemporalShowcase.Contracts.csproj src/TemporalShowcase.Contracts/
COPY src/TemporalShowcase.Application/TemporalShowcase.Application.csproj src/TemporalShowcase.Application/
COPY src/TemporalShowcase.Api/TemporalShowcase.Api.csproj src/TemporalShowcase.Api/
RUN dotnet restore src/TemporalShowcase.Api/TemporalShowcase.Api.csproj
COPY src/TemporalShowcase.Contracts/ src/TemporalShowcase.Contracts/
COPY src/TemporalShowcase.Application/ src/TemporalShowcase.Application/
COPY src/TemporalShowcase.Api/ src/TemporalShowcase.Api/
RUN dotnet publish src/TemporalShowcase.Api/TemporalShowcase.Api.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TemporalShowcase.Api.dll"]
