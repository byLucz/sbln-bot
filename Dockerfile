FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

ARG SBLN_CHANNEL=
ARG SBLN_VERSION=
ARG SBLN_COMMIT=

COPY sblngavnav5X.csproj Directory.Build.props ./
RUN dotnet restore sblngavnav5X.csproj

COPY . .
RUN if [ -n "$SBLN_VERSION" ]; then set -- "-p:SblnVersion=$SBLN_VERSION"; else set --; fi; \
    dotnet publish sblngavnav5X.csproj -c Release -o /app --no-restore \
      "-p:SblnChannel=$SBLN_CHANNEL" "-p:SourceRevisionId=$SBLN_COMMIT" "$@"

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
HEALTHCHECK --interval=5s --timeout=3s --start-period=15s --retries=12 CMD test -f /tmp/sbln-ready || exit 1

ENTRYPOINT ["sh", "-eu", "-c", "rm -f /tmp/sbln-ready; mkdir -p /opt/sbln/data /opt/sbln/logs \"${SBLN_AUDIO_DIR:-/opt/sbln/audio/stable}\"; exec dotnet /app/sblngavnav5X.dll \"$@\"", "sbln"]
