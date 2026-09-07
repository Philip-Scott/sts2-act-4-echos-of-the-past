# Build environment for The Architect (Slay the Spire 2 mod).
#
# The image contains everything needed to build the mod: the .NET SDK, the Godot mono
# editor used to export the asset .pck and the common native dependencies.
#
# It intentionally does NOT contain the Slay the Spire 2 assemblies (sts2.dll and
# 0Harmony.dll); those ship with the game and cannot be redistributed. Mount a folder
# containing them into the container and point STS2_DATA_DIR at it, e.g.
#
#   docker build -t sts2-the-architect-build .
#   docker run --rm \
#       -v "$PWD":/mod \
#       -v "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2":/sts2:ro \
#       -e STS2_PATH=/sts2 \
#       -e MODS_PATH=/mod/artifacts/mods \
#       sts2-the-architect-build
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Godot must match the version the game uses; a newer version produces a .pck the game refuses to load.
ARG GODOT_VERSION=4.5.1

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_PACKAGES=/nuget-cache \
    DOTNET_CLI_HOME=/tmp \
    GODOT_BIN=/opt/godot/godot

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        unzip \
        # Shared libraries the Godot binary links against, even when running headless.
        libasound2 \
        libegl1 \
        libfontconfig1 \
        libfreetype6 \
        libgl1 \
        libx11-6 \
        libxcursor1 \
        libxi6 \
        libxinerama1 \
        libxrandr2 \
    && rm -rf /var/lib/apt/lists/*

RUN set -eux; \
    archive="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64"; \
    curl -fsSL -o /tmp/godot.zip \
        "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/${archive}.zip"; \
    unzip -q /tmp/godot.zip -d /opt; \
    mv "/opt/${archive}" /opt/godot; \
    ln -s "/opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" "$GODOT_BIN"; \
    rm /tmp/godot.zip

# World writable so the container can also be run as the calling user (see scripts/docker-build.sh).
RUN mkdir -p /nuget-cache && chmod 777 /nuget-cache

WORKDIR /mod

ENTRYPOINT ["scripts/build.sh"]
