# Cloudflare Tunnel 部署指南（方案 A）

本文档说明如何通过 **Cloudflare Tunnel（cloudflared）** 将 VedaAide 暴露到公网，
无需开放服务器防火墙端口或配置 NAT。

---

## 前置要求

| 工具 | 版本 | 说明 |
|------|------|------|
| Docker / Docker Compose | 24+ | 运行所有服务 |
| cloudflared CLI | 最新 | 仅首次创建 Tunnel 时使用 |
| Cloudflare 账号 | — | 免费计划即可 |
| 已托管在 Cloudflare 的域名 | — | 用于绑定公网地址 |

---

## 步骤 1：安装 cloudflared（本地一次性操作）

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

## 步骤 2：登录并创建 Tunnel

```bash
# 浏览器跳转授权
cloudflared tunnel login

# 创建 Tunnel（名称自定）
cloudflared tunnel create vedaaide

# 输出示例：
# Tunnel credentials written to ~/.cloudflared/<TUNNEL_ID>.json
# Created tunnel vedaaide with id <TUNNEL_ID>
```

---

## 步骤 3：配置文件

将生成的凭据文件复制到项目目录：

```bash
cp ~/.cloudflared/<TUNNEL_ID>.json cloudflare/credentials.json
```

编辑 `cloudflare/config.yml`，将占位符替换为真实值：

```yaml
tunnel: <TUNNEL_ID>          # ← 替换为第 2 步输出的 ID
credentials-file: /etc/cloudflared/credentials.json

ingress:
  - service: http://veda-web:80
  - service: http_status:404
```

---

## 步骤 4：绑定域名 DNS

```bash
cloudflared tunnel route dns vedaaide your-subdomain.example.com
```

此命令会在 Cloudflare DNS 中自动创建 CNAME 记录，指向 Tunnel 入口。

---

## 步骤 5：配置环境变量

在项目根目录创建 `.env` 文件（已加入 `.gitignore`）：

```env
CLOUDFLARE_TUNNEL_TOKEN=<从 cloudflare dashboard 获取的 token，可选>
```

> **注意**：`docker-compose.yml` 中 cloudflared 服务直接挂载 `config.yml` +
> `credentials.json`，`TUNNEL_TOKEN` 是可选的备用认证方式。

---

## 步骤 6：启动全部服务

```bash
# 首次启动（拉取/构建镜像 + 运行）
docker compose up -d --build

# 查看日志
docker compose logs -f cloudflared
```

成功后访问 `https://your-subdomain.example.com` 即可使用 VedaAide。

---

## 步骤 7：拉取 Ollama 模型（首次）

```bash
docker compose exec ollama ollama pull llama3.2
docker compose exec ollama ollama pull nomic-embed-text
```

---

## 安全注意事项

- `cloudflare/credentials.json` 包含私密凭据，**已加入 `.gitignore`，切勿提交**。
- 如需限制访问，可在 Cloudflare Access 面板配置 Zero Trust 策略。
- SQLite 数据库通过 Docker volume (`veda-db`) 持久化，备份 volume 即可保留数据。

---

## 服务架构

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
