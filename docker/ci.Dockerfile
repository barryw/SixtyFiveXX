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
# BUILD IT FOR BOTH ARCHITECTURES AND PUSH IT:
#
#   docker buildx build --platform linux/amd64,linux/arm64 \
#     -f docker/ci.Dockerfile -t ghcr.io/barryw/sixtyfivexx-ci:1 --push .
#
# linux/amd64 is what the Kubernetes agents run; arm64 only exists so the image
# is usable on an Apple Silicon machine. Building it without --platform on a Mac
# produces an arm64-only image the agents cannot run, and a step that cannot pull
# or start its image fails with NO LOG OUTPUT AT ALL — which looks like anything
# except a bad image. Bump the tag when this file changes; agents cache by tag.
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
