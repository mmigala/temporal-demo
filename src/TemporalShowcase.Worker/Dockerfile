FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TemporalShowcase.Contracts/TemporalShowcase.Contracts.csproj src/TemporalShowcase.Contracts/
COPY src/TemporalShowcase.Application/TemporalShowcase.Application.csproj src/TemporalShowcase.Application/
COPY src/TemporalShowcase.Worker/TemporalShowcase.Worker.csproj src/TemporalShowcase.Worker/
RUN dotnet restore src/TemporalShowcase.Worker/TemporalShowcase.Worker.csproj
COPY src/TemporalShowcase.Contracts/ src/TemporalShowcase.Contracts/
COPY src/TemporalShowcase.Application/ src/TemporalShowcase.Application/
COPY src/TemporalShowcase.Worker/ src/TemporalShowcase.Worker/
RUN dotnet publish src/TemporalShowcase.Worker/TemporalShowcase.Worker.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TemporalShowcase.Worker.dll"]
