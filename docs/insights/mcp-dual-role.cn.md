# MCP 双重角色：VedaAide 既是 Server 也是 Client

## 背景

设计阶段将 MCP（Model Context Protocol）列为"将外部数据源封装为 MCP Tool"，方向模糊。开发过程中明确了两条独立的通道，两者不冲突。

## 两种角色的本质区别

| 角色 | 方向 | 用途 | 实现状态 |
|---|---|---|---|
| MCP Server | 向外暴露工具 | 外部 AI 客户端（VS Code Copilot 等）调用 VedaAide 的知识库 | ✅ Phase 4 已完成 |
| MCP Client | 向内消费工具 | VedaAide 主动读取外部数据源（文件系统、Azure Blob）作为 Ingest 数据源 | ⏳ Phase 4.5 |

## VedaAide 作为 MCP Server（已完成）

实现位置：`src/Veda.MCP/`

暴露的工具：
- `search_knowledge_base(query, topK)` — 向量检索，返回相关文档块及相似度
- `list_documents()` — 列出知识库中已摄入的文档
- `ingest_document(content, documentName, documentType)` — 将内容摄入知识库

接入方式（HTTP/SSE），配置在 `.vscode/mcp.json`：
```json
{ "type": "sse", "url": "http://localhost:5126/mcp/sse" }
```

外部客户端（VS Code Copilot、Claude Desktop、其他 AI 助手）可通过标准 MCP 协议按需调用以上工具，实现对 VedaAide 知识库的语义检索和内容写入。

## VedaAide 作为 MCP Client（Phase 4.5 待实现）

目标：让 VedaAide 能**主动消费**外部 MCP Server，将外部数据源的内容批量摄入知识库。

```
外部 MCP Server              VedaAide (MCP Client)
mcp-server-filesystem  →  FileSystemConnector  →  DocumentIngestService
Azure Blob MCP         →  BlobStorageConnector →  DocumentIngestService
```

关键设计：
- 抽象接口 `IDataSourceConnector`，统一文件系统/Blob/数据库的接入方式（OCP 原则）
- 各 Connector 只负责"获取内容"，`DocumentIngestService` 负责"处理入库"（SRP 原则）
- 触发方式：手动（UI 一键同步）或自动（Background Service 定时轮询）

## 为什么社区现成的文件系统/Blob MCP 不够用

- `mcp-server-filesystem`：只支持文件名/路径搜索，无语义检索能力
- Azure Blob MCP（社区版本）：只做 CRUD，不理解内容语义
- 两者都缺乏"理解内容含义"的能力

VedaAide 作为 MCP Client 消费这些工具的价值不在于替代它们的检索能力，而是将其内容**摄入到 VedaAide 的向量库**，由 VedaAide 统一提供语义检索。数据流是单向的：外部 → 摄取 → 知识库 → 检索。
