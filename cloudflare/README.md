# Cloudflare Tunnel Deployment Guide (Option A)

This document explains how to expose VedaAide to the public internet via **Cloudflare Tunnel (cloudflared)** — no need to open server firewall ports or configure NAT.

---

## Prerequisites

| Tool | Version | Notes |
|------|------|------|
| Docker / Docker Compose | 24+ | Runs all services |
| cloudflared CLI | Latest | Only needed the first time you create a Tunnel |
| Cloudflare account | — | The free plan is enough |
| A domain hosted on Cloudflare | — | Used to bind a public address |

---

## Step 1: Install cloudflared (one-time, local)

```bash
# macOS
brew install cloudflare/cloudflare/cloudflared

# Windows (Scoop)
scoop bucket add cloudflare https://github.com/cloudflare/cloudflare-tunnel-installer
scoop install cloudflared

# Linux
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 \
  -o /usr/local/bin/cloudflared && chmod +x /usr/local/bin/cloudflared
```

---

## Step 2: Log in and create the Tunnel

```bash
# Opens a browser for authorization
cloudflared tunnel login

# Create the tunnel (name is up to you)
cloudflared tunnel create vedaaide

# Example output:
# Tunnel credentials written to ~/.cloudflared/<TUNNEL_ID>.json
# Created tunnel vedaaide with id <TUNNEL_ID>
```

---

## Step 3: Configuration file

Copy the generated credentials file into the project directory:

```bash
cp ~/.cloudflared/<TUNNEL_ID>.json cloudflare/credentials.json
```

Edit `cloudflare/config.yml`, replacing the placeholders with real values:

```yaml
tunnel: <TUNNEL_ID>          # ← replace with the ID from step 2
credentials-file: /etc/cloudflared/credentials.json

ingress:
  - service: http://veda-web:80
  - service: http_status:404
```

---

## Step 4: Bind the domain DNS

```bash
cloudflared tunnel route dns vedaaide your-subdomain.example.com
```

This command automatically creates a CNAME record in Cloudflare DNS pointing to the Tunnel ingress.

---

## Step 5: Configure environment variables

Create a `.env` file in the project root (already in `.gitignore`):

```env
CLOUDFLARE_TUNNEL_TOKEN=<token from the Cloudflare dashboard, optional>
```

> **Note**: in `docker-compose.yml` the cloudflared service mounts `config.yml` +
> `credentials.json` directly; `TUNNEL_TOKEN` is an optional alternative auth method.

---

## Step 6: Start all services

```bash
# First start (pull/build images + run)
docker compose up -d --build

# View logs
docker compose logs -f cloudflared
```

On success, visit `https://your-subdomain.example.com` to use VedaAide.

---

## Step 7: Pull Ollama models (first time)

```bash
docker compose exec ollama ollama pull llama3.2
docker compose exec ollama ollama pull nomic-embed-text
```

---

## Security Notes

- `cloudflare/credentials.json` contains private credentials; it is **already in `.gitignore` — never commit it**.
- To restrict access, configure a Zero Trust policy in the Cloudflare Access dashboard.
- The SQLite database is persisted via a Docker volume (`veda-db`); back up the volume to keep the data.

---

## Service Architecture

```
Internet → Cloudflare Edge → cloudflared Tunnel
                                     ↓
                              veda-web (nginx:80)
                              ├── /api/*  →  veda-api:8080
                              ├── /graphql →  veda-api:8080
                              └── /*      →  Angular SPA
                                                 ↓
                                          veda-api (ASP.NET Core)
                                          ├── SQLite (volume)
                                          └── ollama:11434
```
