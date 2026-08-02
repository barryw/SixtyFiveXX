# CI image for SixtyFiveXX.
#
# Two things the stock SDK image lacks:
#   64tass         - assembles Klaus Dormann's interrupt test, which upstream
#                    ships as source only, so the conformance suite builds it
#                    on demand.
#   .NET 8 runtime - the SDK 10 image can BUILD net8.0 but not RUN it, so the
#                    net8.0 half of the test matrix would not execute.
FROM mcr.microsoft.com/dotnet/sdk:10.0

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
