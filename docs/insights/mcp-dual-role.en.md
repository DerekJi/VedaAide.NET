# MCP Dual Role: VedaAide as Both Server and Client

**Phase**: Phase 4/5

> 中文版见 [mcp-dual-role.cn.md](mcp-dual-role.cn.md)

---

## Background

The original design described MCP (Model Context Protocol) as "wrapping external data sources as MCP Tools" — a vague direction. During development, two independent channels were clarified and neither conflicts with the other.

## The Fundamental Difference Between the Two Roles

| Role | Direction | Purpose | Implementation Status |
|---|---|---|---|
| MCP Server | Outward — exposes tools | External AI clients (VS Code Copilot, etc.) call VedaAide's knowledge base | ✅ Phase 4 Done |
| MCP Client | Inward — consumes tools | VedaAide actively reads external data sources (file system, Azure Blob) as ingestion sources | ✅ Phase 5 Done |

## VedaAide as MCP Server (Done)

Implementation location: `src/Veda.MCP/`

Exposed tools:
- `search_knowledge_base(query, topK)` — vector search, returns relevant chunks + similarity scores
- `list_documents()` — lists documents ingested into the knowledge base
- `ingest_document(content, documentName, documentType)` — ingests content into the knowledge base

Connection method (HTTP), configured in `.vscode/mcp.json`:
```json
{ "type": "http", "url": "http://localhost:5126/mcp" }
```

External clients (VS Code Copilot, Claude Desktop, other AI assistants) can call these tools via the standard MCP protocol to perform semantic search and content ingestion against VedaAide's knowledge base.

## VedaAide as MCP Client (Done)

Goal: VedaAide **actively consumes** external data sources and batch-ingests their content into the knowledge base.

```
External Data Source              VedaAide (MCP Client)
Local file system       →  FileSystemConnector   →  DocumentIngestService
Azure Blob Storage      →  BlobStorageConnector  →  DocumentIngestService
```

Key design:
- Abstract interface `IDataSourceConnector` unifies access to file system/Blob/database (OCP principle)
- Each Connector is only responsible for "fetching content"; `DocumentIngestService` handles "processing and storing" (SRP principle)
- Trigger modes: manual (`POST /api/datasources/sync`) or automatic (`DataSourceSyncBackgroundService` — periodic polling)
- Content-hash-based sync state (`ISyncStateStore`): each file's SHA-256 hash is persisted; unchanged files are skipped on subsequent syncs

## Why Off-the-Shelf Filesystem/Blob MCPs Are Not Enough

- `mcp-server-filesystem`: only supports filename/path search, no semantic retrieval capability
- Azure Blob MCP (community): only does CRUD, no content understanding
- Both lack the ability to "understand content semantics"

VedaAide consuming these tools as an MCP Client is not about replacing their retrieval capabilities — it's about **ingesting their content into VedaAide's vector store** so VedaAide can provide unified semantic search. The data flow is one-way: external → ingestion → knowledge base → retrieval.
