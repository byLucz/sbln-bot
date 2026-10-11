FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

ARG TARGETARCH
ARG SBLN_CHANNEL=
ARG SBLN_VERSION=
ARG SBLN_COMMIT=

COPY sblngavnav6.csproj Directory.Build.props ./
COPY external/DTF/Directory.Build.props external/DTF/
COPY external/DTF/DTF.csproj external/DTF/
COPY TelegramExtensions/TelegramExtensions.csproj TelegramExtensions/
RUN --mount=type=cache,id=sbln-nuget,target=/root/.nuget/packages \
    rid=linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64); \
    dotnet restore sblngavnav6.csproj -r "$rid"

COPY . .
RUN --mount=type=cache,id=sbln-nuget,target=/root/.nuget/packages \
    rid=linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64); \
    if [ -n "$SBLN_VERSION" ]; then set -- "-p:SblnVersion=$SBLN_VERSION"; else set --; fi; \
    dotnet publish sblngavnav6.csproj -c Release -o /app --no-restore -r "$rid" --self-contained false \
      -p:DebugType=none -p:DebugSymbols=false \
      "-p:SblnChannel=$SBLN_CHANNEL" "-p:SourceRevisionId=$SBLN_COMMIT" "$@"

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl gnupg \
        libfontconfig1 fontconfig fonts-liberation fonts-dejavu-core fonts-noto-color-emoji ffmpeg tesseract-ocr tesseract-ocr-rus tesseract-ocr-eng \
    && install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" > /etc/apt/sources.list.d/docker.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends docker-ce-cli \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
HEALTHCHECK --interval=10s --timeout=3s --start-period=120s --retries=6 CMD test -f /tmp/sbln-ready || exit 1

ENTRYPOINT ["sh", "-eu", "-c", "rm -f /tmp/sbln-ready; mkdir -p /opt/sbln/data /opt/sbln/logs \"${SBLN_AUDIO_DIR:-/opt/sbln/audio/stable}\"; exec dotnet /app/sblngavnav6.dll \"$@\"", "sbln"]
