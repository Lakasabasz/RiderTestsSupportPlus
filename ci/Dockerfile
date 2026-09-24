# CI image for Buddy pipelines: .NET SDK + trimmed Rider SDK + warmed Gradle/NuGet caches.
# The pipeline filesystem is limited to 5 GB, so nothing big is downloaded or cached per run:
# the Rider SDK alone is ~4 GB to download and ~10 GB extracted.
#
# Build and push from the repository root (after docker login container.eu.registry.sh -u buddy).
# Push a plain Docker v2 manifest: Buddy showed no versions for an OCI index with attestations.
#   docker buildx build -f ci/Dockerfile --provenance=false --sbom=false \
#     --output type=registry,oci-mediatypes=false \
#     -t container.eu.registry.sh/lakasabasz/build-image:2026.2.2 \
#     -t container.eu.registry.sh/lakasabasz/build-image:latest .
# Rebuild when ProductVersion (Rider SDK) changes.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG RIDER_VERSION=2026.2.2

ENV RIDER_SDK_PATH=/opt/rider \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends unzip \
 && rm -rf /var/lib/apt/lists/*

# Multi-platform Rider from JetBrains' Maven repository (the Linux installer lacks lib/testFramework.jar and
# lib/rd/rider-model.jar). Plugins not needed to build or test the plugin are left out (~4.7 GB, 3.6 GB of it
# native C++ debuggers). Verified with ./gradlew buildPlugin test.
RUN curl -fsSL -o /tmp/rider.zip \
      "https://cache-redirector.jetbrains.com/intellij-repository/releases/com/jetbrains/intellij/rider/riderRD/${RIDER_VERSION}/riderRD-${RIDER_VERSION}.zip" \
 && unzip -q /tmp/rider.zip -d /opt/rider \
      -x 'plugins/cidr-debugger-plugin/*' 'plugins/fullLine/*' 'plugins/code-provenance/*' 'plugins/rider-plugins-renderdoc/*' \
 && rm /tmp/rider.zip

# Native libraries the JetBrains Runtime needs even when headless (integration tests)
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfreetype6 fontconfig \
 && rm -rf /var/lib/apt/lists/*

# Caches live outside /root: Buddy mounts pipeline cache dirs (cached_dirs) over paths like /root/.gradle, which would
# hide the warmed caches and send every download into the size-limited pipeline cache.
# gradlew keeps its JDK in $HOME/.local/share/gradle-jvm, hence HOME.
ENV HOME=/opt/ci-home \
    GRADLE_USER_HOME=/opt/ci-home/.gradle \
    NUGET_PACKAGES=/opt/ci-home/.nuget/packages

# Warm-up: a full build (and the integration tests, allowed to fail here) downloads everything else into the image:
# JDK 21 for gradlew, JDK 25 toolchain, Gradle, JetBrains Runtime, Maven and NuGet dependencies.
COPY . /tmp/warmup
RUN cd /tmp/warmup \
 && ./gradlew buildPlugin --no-daemon --console=plain \
 && (./gradlew test --no-daemon --console=plain || echo "Warm-up tests failed, the caches are filled anyway") \
 && cd / && rm -rf /tmp/warmup "$GRADLE_USER_HOME/daemon" "$GRADLE_USER_HOME/.tmp" /tmp/* \
 # Restore reads packages from $NUGET_PACKAGES, the HTTP cache and the JDK archive are not needed
 && rm -rf "$HOME/.local/share/NuGet/http-cache" "$GRADLE_USER_HOME"/jdks/*.tar.gz \
 # Runtimes for other platforms and clang-tidy (~1.7 GB) come with the SDK but the build never reads them.
 # Keep only the marker files, so restore still treats them as installed and doesn't download them again.
 && cd "$NUGET_PACKAGES" \
 && for v in jetbrains.dotnetcore.runtime.*/*/ jetbrains.clangtidy.*/*/; do \
      find "$v" -mindepth 1 -maxdepth 1 ! -name '.nupkg.metadata' ! -name '*.sha512' ! -name '*.nuspec' -exec rm -rf {} +; \
    done
