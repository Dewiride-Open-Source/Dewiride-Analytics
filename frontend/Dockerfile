# The dashboard: the screens people look at, and the one address the browser talks to.
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
COPY frontend/package.json frontend/pnpm-workspace.yaml frontend/pnpm-lock.yaml ./
COPY frontend/apps/web/package.json ./apps/web/

RUN pnpm install --frozen-lockfile

COPY frontend/ ./

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
# and then renders without a single stylesheet.
COPY --from=build --chown=node:node /build/apps/web/.next/standalone ./
COPY --from=build --chown=node:node /build/apps/web/.next/static ./apps/web/.next/static

# Runs as the unprivileged account the base image provides, never as root.
USER node

EXPOSE 3000

# The address of the engine is read here, not baked in above, so one built image can be pointed at
# whichever engine it is deployed beside.
CMD ["node", "apps/web/server.js"]
