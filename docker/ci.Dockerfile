# CI image for SixtyFiveXX.
#
# Two things the stock SDK image lacks:
#   64tass         - assembles Klaus Dormann's interrupt test, which upstream
#                    ships as source only, so the conformance suite builds it
#                    on demand. It is also invoked during the run itself, by the
#                    disassembler round-trip gate.
#   .NET 8 runtime - the SDK 10 image can BUILD net8.0 but not RUN it, so the
#                    net8.0 half of the test matrix would not execute.
#
# DO NOT BUILD THIS BY HAND. `.woodpecker/ci-image.yaml` builds and pushes it
# whenever this file changes, tagging both `:1` — the moving tag the pipeline
# template references — and the commit sha.
#
# It was built by hand once, and the result was an image that had never been
# pushed at all, at linux/arm64 while every agent is linux/amd64. A step that
# cannot pull or start its image produces NO LOG OUTPUT WHATSOEVER, so the
# failure looked like anything except a bad image.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# Links the GHCR package to the repository, so repository permissions govern who
# can pull it instead of per-package collaborator lists.
LABEL org.opencontainers.image.source=https://github.com/barryw/SixtyFiveXX

RUN apt-get update \
 && apt-get install -y --no-install-recommends 64tass git ca-certificates \
 && rm -rf /var/lib/apt/lists/*

RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

# Fail at image build time rather than confusingly mid-pipeline.
RUN 64tass --version \
 && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 8\.' \
 && dotnet --list-runtimes | grep -q 'Microsoft.NETCore.App 10\.'
