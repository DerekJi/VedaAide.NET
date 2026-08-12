# Copilot Guide (VedaAide.NET)

## Repository Overview

This is a **production-grade RAG (Retrieval-Augmented Generation) platform** built in C# / .NET 10.
The goal is to demonstrate end-to-end AI systems engineering: hybrid retrieval, hallucination
defence, agent orchestration, MCP integration, enterprise auth, and a quantitative evaluation harness.

Main projects (layered, strict dependency direction `Core → Services → Storage → Entry Points`):
- `src/Veda.Core`: domain interfaces, options, shared models
- `src/Veda.Services`: ingest/query pipelines, embedding, LLM routing, retrieval, hallucination guard
- `src/Veda.Storage`: CosmosDB (DiskANN) + SQLite-VSS (EF Core) vector stores, caches, repositories
- `src/Veda.Agents`: Microsoft Agent Framework (MAF) orchestration (ReAct / IRCoT loop)
- `src/Veda.Evaluation`: LLM-as-a-judge scorers (faithfulness / relevancy / context recall)
- `src/Veda.MCP`: Model Context Protocol server (HTTP transport)
- `src/Veda.Api`: REST + GraphQL + SSE entry point
- `src/Veda.Web`, `src/Veda.Prompts`: supporting projects

**Main tech stack:**
- C# / .NET 10, ASP.NET Core
- Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`) + Microsoft.Agents.AI
- Ollama (local) / Azure OpenAI / DeepSeek (OpenAI-compatible)
- EF Core, CosmosDB, SQLite-VSS
- NUnit + FluentAssertions + Moq for testing

## Key Prerequisites

- Treat the requirements, plans, and acceptance criteria in `docs/` as the highest authority
  (especially `docs/rag-internals/` Architecture Decision Records and `docs/designs/`)
- Follow the existing implementation, directory structure, and test style of this repository
- Prefer reusing existing modules; avoid introducing unrelated tech stacks
- Respect the layered dependency direction; no upward references

## Build and Test

### Build the solution

```bash
dotnet build VedaAide.slnx
```

### Run tests

```bash
dotnet test VedaAide.slnx -q
```

### Code checks

```bash
dotnet build VedaAide.slnx
dotnet test VedaAide.slnx -q
```

## Project Structure

```text
/
├── docs/                    # requirements, plans, ADRs, bilingual (en / cn)
├── src/
│   ├── Veda.Core/           # domain interfaces, options
│   ├── Veda.Services/       # business services (ingest / query / retrieval)
│   ├── Veda.Storage/        # CosmosDB + SQLite vector stores
│   ├── Veda.Agents/         # MAF agent orchestration
│   ├── Veda.Evaluation/     # RAG evaluation harness
│   ├── Veda.MCP/            # MCP server
│   ├── Veda.Api/            # REST / GraphQL / SSE entry point
│   ├── Veda.Web/
│   └── Veda.Prompts/
├── tests/
│   ├── Veda.Core.Tests/
│   ├── Veda.Services.Tests/
│   └── Veda.Evaluation.Tests/
├── VedaAide.slnx
└── README.md
```

## Development Conventions

### C# code
- File/class names and public members use `PascalCase`; locals/parameters use `camelCase`
- `async`/`await` everywhere async is used; always propagate `CancellationToken`
- Use `ILogger<T>` for logging, never `Console.WriteLine` in library code
- Follow the DIP adapter pattern: domain interfaces in `Veda.Core`, implementations in `Veda.Services`
- Keep option classes in `src/Veda.Core/Options`, bound from `Veda:` config sections

### Tests
- Use NUnit + FluentAssertions + Moq
- Test files live under `tests/<Project>.Tests/` mirroring the source namespace
- Mock external dependencies (LLM clients, repositories, vector stores)
- For integration tests, use in-memory SQLite (`DataSource=:memory:`) and fake chat/embedding services;
  never require live Ollama / Azure OpenAI in CI

## Principles When Modifying Code

1. Read `docs/` first, then the existing implementation
2. Keep consistent with the boundaries of `src/Veda.Core`, `src/Veda.Services`, `src/Veda.Storage`
3. New behavior must come with tests
4. Prefer the smallest verifiable change
5. After changes, run `dotnet build VedaAide.slnx` and `dotnet test VedaAide.slnx -q`

## Reference Docs

- `docs/rag-internals/` — architecture, ADRs, query flow, module dependencies
- `docs/designs/` — phase plans and design docs
- `README.md` / `README.cn.md` — overview and bilingual docs
