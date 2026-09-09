# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-bookworm-slim AS build
WORKDIR /src

COPY TwitchDownloader2.CLI/TwitchDownloader2.CLI.csproj TwitchDownloader2.CLI/
RUN dotnet restore TwitchDownloader2.CLI/TwitchDownloader2.CLI.csproj

COPY TwitchDownloader2.CLI/ TwitchDownloader2.CLI/
RUN dotnet publish TwitchDownloader2.CLI/TwitchDownloader2.CLI.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-bookworm-slim
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends \
        ca-certificates \
        ffmpeg \
        yt-dlp \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish/ .
RUN mkdir --parents /app/Data /app/Downloads

ENTRYPOINT ["dotnet", "TwitchDownloader2.CLI.dll"]
