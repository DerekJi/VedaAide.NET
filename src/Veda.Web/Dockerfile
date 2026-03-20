# syntax=docker/dockerfile:1

# ──────────────────────────────────────────────────────────────
# Stage 1: build Angular app
# ──────────────────────────────────────────────────────────────
FROM node:24-alpine AS build
WORKDIR /app

COPY src/Veda.Web/package.json src/Veda.Web/package-lock.json* ./
RUN npm ci --prefer-offline

COPY src/Veda.Web/ .
RUN npx ng build --configuration production

# ──────────────────────────────────────────────────────────────
# Stage 2: serve with nginx
# ──────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runtime

# Remove default config and replace with our own
RUN rm /etc/nginx/conf.d/default.conf
COPY src/Veda.Web/nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /app/dist/veda-web/browser /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
