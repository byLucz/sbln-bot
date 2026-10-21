FROM mcr.microsoft.com/dotnet/sdk:8.0.426-noble AS build
WORKDIR /src

ARG TARGETARCH

COPY global.json sblngavnav5X.csproj ./
RUN --mount=type=cache,id=sbln5x-nuget,target=/root/.nuget/packages \
    rid=linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64); \
    dotnet restore sblngavnav5X.csproj -r "$rid"

COPY . .
RUN --mount=type=cache,id=sbln5x-nuget,target=/root/.nuget/packages \
    rid=linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64); \
    dotnet publish sblngavnav5X.csproj -c Release -o /app --no-restore -r "$rid" --self-contained false \
      -p:DebugType=none -p:DebugSymbols=false -p:SatelliteResourceLanguages=ru

FROM mcr.microsoft.com/dotnet/runtime:8.0.32-noble-chiseled-extra AS runtime
WORKDIR /app

COPY --from=build /app ./

ARG SBLN_VERSION=
ARG SBLN_CHANNEL=
ARG SBLN_COMMIT=
LABEL sbln.version="$SBLN_VERSION" sbln.channel="$SBLN_CHANNEL" sbln.commit="$SBLN_COMMIT" sbln.line="v5X"

ENV TZ=Europe/Moscow \
    SBLN_V5X_CONFIG=/opt/sbln/config-v5x.json \
    SBLN_READY_FILE=/tmp/sbln-ready \
    SBLN_DATA_DIR=/opt/sbln/data \
    SBLN_LOG_DIR=/opt/sbln/logs \
    SBLN_AUDIO_DIR=/opt/sbln/audio/v5x \
    DOTNET_GCConserveMemory=7

USER 0:0

HEALTHCHECK --interval=15s --timeout=5s --start-period=120s --retries=6 CMD ["dotnet", "/app/sblngavnav5X.dll", "--health"]

ENTRYPOINT ["dotnet", "/app/sblngavnav5X.dll"]
