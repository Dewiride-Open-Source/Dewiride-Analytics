# The product's engine: the collector, the dashboard's data, and the schema migrations.
#
# Built from the repository root, because the build settings, the single package-version file and
# the banned-API list all live there and every project inherits them.
#
#   docker build -f backend/Dockerfile .

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

WORKDIR /src

# The files that decide how everything compiles, copied first. They change far less often than
# the code does, so restoring stays cached across ordinary edits.
#
# .editorconfig is one of them, not an editor preference: it is where the generated migration
# files are declared generated, and without it the compiler applies hand-written-code rules to
# scaffolded output and the build fails on files nobody is allowed to edit.
COPY global.json Directory.Build.props Directory.Packages.props BannedSymbols.txt .editorconfig ./
COPY backend/src/ backend/src/
COPY backend/migrations/ backend/migrations/

# Restored separately so a code change does not re-download every package.
RUN dotnet restore backend/src/Dewiride.Analytics.Api/Dewiride.Analytics.Api.csproj

# Which edition this becomes is decided here and nowhere else. Community is the open-source
# product; Cloud additionally compiles the commercially licensed projects, which are not present
# in this build context.
ARG DEWIRIDE_EDITION=Community

RUN dotnet publish backend/src/Dewiride.Analytics.Api/Dewiride.Analytics.Api.csproj \
    --configuration Release \
    --no-restore \
    -p:DewirideEdition=${DEWIRIDE_EDITION} \
    --output /app

# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------
# Deliberately the full runtime image rather than a chiselled or distroless one. Those exclude
# the international components and the time-zone database, which forces a build to run with
# globalization switched off — and this product buckets every site's days in that site's own time
# zone and reports traffic by country. Neither works without them.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final

# The one tool added to the image, and only so the container can answer whether it is ready.
# Without it "bring the stack up and tell me when it is usable" cannot be answered, and a
# self-hoster is left watching logs to find out when to open the page.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Where the downloaded country, town and network data is kept, created here rather than left to
# the volume mounted over it. Docker takes a new named volume's ownership from the directory it
# covers, and a directory that only exists because the mount created it belongs to root — which
# the account below is not, so every download would fail with a permission error on a path the
# service was explicitly told to use.
RUN mkdir -p /var/lib/dewiride/reference && chown -R $APP_UID:$APP_UID /var/lib/dewiride

# Runs as the unprivileged account the base image provides, never as root.
USER $APP_UID

EXPOSE 8080

ENTRYPOINT ["dotnet", "Dewiride.Analytics.Api.dll"]
