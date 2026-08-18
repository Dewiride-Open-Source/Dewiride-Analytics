# The dashboard: the screens people look at, the one address the browser talks to, and the beacon
# that goes on the websites being measured.
#
# The beacon is compiled and served here rather than by the engine, because the address somebody
# pastes into their own website has to be one that is certainly reachable from outside — and that
# is this one. Everything the engine answers is forwarded from here.
#
# Built from the repository root so the build context matches the engine's:
#
#   docker build -f frontend/Dockerfile .

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
FROM node:24-alpine AS build

# pnpm installs itself at the version pinned in package.json, so the version this image is built
# with is the version the repository declares and there is no second place to keep in step.
RUN corepack enable

WORKDIR /build

# The manifests alone first. They change far less often than the code, so an ordinary edit reuses
# the installed packages instead of fetching them again.
COPY package.json pnpm-workspace.yaml pnpm-lock.yaml ./
COPY frontend/apps/web/package.json ./frontend/apps/web/
COPY tracker/package.json ./tracker/

RUN pnpm install --frozen-lockfile

COPY tracker/ ./tracker/
COPY frontend/ ./frontend/

# The beacon first: compiling it writes the file the dashboard then serves, and building in the
# other order produces a dashboard with nothing to hand out.
RUN pnpm --filter @dewiride/tracker build
RUN pnpm --filter @dewiride/web build

# ---------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------
FROM node:24-alpine AS final

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0

WORKDIR /app

# The self-contained server, and then the two things it does not include: the compiled assets and
# anything served as-is. Both are silently omitted otherwise, and the result is a site that starts
# and then renders without a single stylesheet — or, since the beacon is served as-is, one that
# hands every measured website a page that is not there.
COPY --from=build --chown=node:node /build/frontend/apps/web/.next/standalone ./
COPY --from=build --chown=node:node /build/frontend/apps/web/.next/static ./frontend/apps/web/.next/static
COPY --from=build --chown=node:node /build/frontend/apps/web/public ./frontend/apps/web/public

# Runs as the unprivileged account the base image provides, never as root.
USER node

EXPOSE 3000

# The address of the engine is read here, not baked in above, so one built image can be pointed at
# whichever engine it is deployed beside.
CMD ["node", "frontend/apps/web/server.js"]
