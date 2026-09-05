FROM node:24.16.0-bookworm-slim AS build
WORKDIR /repo
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
WORKDIR /repo/src/web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/web ./
ARG CRITICAL_ALERTS_API_URL=http://api:8080
ENV CRITICAL_ALERTS_API_URL=$CRITICAL_ALERTS_API_URL
RUN npm run build

FROM node:24.16.0-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /repo/src/web/package.json /repo/src/web/package-lock.json ./
RUN npm ci --omit=dev --no-audit --no-fund
COPY --from=build /repo/src/web/.next ./.next
COPY --from=build /repo/src/web/public ./public
EXPOSE 3000
CMD ["npm", "run", "start"]
