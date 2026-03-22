# 阶段四设计方案：MCP + Agentic Workflow

> 前提：阶段三已完成（GraphQL + SSE 流式 + Angular 前端 + Docker 部署）。

---

## 1. 目标与交付物

| 交付物 | 说明 |
|--------|------|
| `Veda.MCP` | MCP Server 类库，暴露知识库工具，整合进 `Veda.Api` |
| `Veda.Agents` | Semantic Kernel Agent 编排层，多 Agent 协同 |
| `Veda.Prompts`（补全） | Prompt 模板库 + 上下文窗口构建器 |
| `.vscode/mcp.json` | VSCode Copilot Chat 集成配置 |

---

## 2. Veda.MCP — MCP Server

### 2.1 技术选型

| 项目 | 选择 | 理由 |
|------|------|------|
| MCP SDK | `ModelContextProtocol.AspNetCore`（Microsoft 官方） | 原生.NET，与 SK 同源 |
| 传输协议（开发） | HTTP/SSE（`/mcp/sse`） | Veda.Api 复用同一端口，无需独立进程 |
| 传输协议（备选） | stdio | VSCode 本地开发时直接调用 |

### 2.2 暴露的 MCP Tools

| Tool 名 | 入参 | 出参 | 用途 |
|---------|------|------|------|
| `search_knowledge_base` | `query: string`, `topK: int = 5` | JSON 数组（chunk + 相似度） | 向量检索 |
| `ingest_document` | `content: string`, `documentName: string`, `documentType: string` | `{ documentId, chunksAdded }` | 摄取文本 |
| `list_documents` | 无 | JSON 数组（文档名 + 块数） | 查看知识库内容 |

### 2.3 项目结构

```
src/Veda.MCP/
  Veda.MCP.csproj
  GlobalUsings.cs
  McpServiceExtensions.cs          # AddVedaMcp() 扩展方法
  Tools/
    KnowledgeBaseTools.cs          # search + list
    IngestTools.cs                 # ingest
```

### 2.4 关键代码模式

```csharp
// McpServiceExtensions.cs
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<KnowledgeBaseTools>()
    .WithTools<IngestTools>();

// Program.cs
app.MapMcp("/mcp");

// Tool 定义
[McpServerToolType]
public sealed class KnowledgeBaseTools(IEmbeddingService embedding, IVectorStore vectorStore)
{
    [McpServerTool, Description("Search the VedaAide knowledge base")]
    public async Task<string> SearchKnowledgeBase(
        [Description("Search query")] string query,
        [Description("Max results")] int topK = 5)
    {
        var embedding = await _embedding.GenerateEmbeddingAsync(query);
        var results = await _vectorStore.SearchAsync(embedding, topK);
        return JsonSerializer.Serialize(results.Select(r => new { r.Chunk.Content, r.Chunk.DocumentName, r.Similarity }));
    }
}
```

---

## 3. Veda.Agents — Semantic Kernel Agent 编排

### 3.1 技术选型

使用 **Semantic Kernel Agents**（`Microsoft.SemanticKernel.Agents.Core`，SK 1.73 内置），无需引入额外的 Agent Framework。

### 3.2 定义的 Agent 角色

> ⚠️ **实际实现说明**：三个 Agent 角色未创建独立的类文件，而是直接内嵌在编排服务中：
- `OrchestrationService`（确定性调用链）和 `LlmOrchestrationService`（LLM 驱动）均承担 DocumentAgent / QueryAgent / EvalAgent 三个角色的职责。

| Agent 角色 | 职责 | 实现位置 |
|------------|------|----------|
| DocumentAgent | 文档类型推断 + 摄取 | `OrchestrationService.RunIngestFlowAsync()` / `LlmOrchestrationService.RunIngestFlowAsync()` |
| QueryAgent | RAG 检索 + LLM 生成 | `OrchestrationService.RunQueryFlowAsync()` / `LlmOrchestrationService`（Agent Loop） |
| EvalAgent | 上下文一致性校验 | 两个 OrchestrationService 末尾的 `HallucinationGuardService.VerifyAsync()` 调用 |

### 3.3 编排模式

```
用户请求 → OrchestrationService.RunAsync()
    ├─ DocumentAgent（文档处理场景）
    └─ QueryAgent → EvalAgent（问答 + 自动评估场景）
```

使用 `AgentGroupChat`（顺序模式）或手动调用链（更可控）。阶段四以手动调用链为优先，确保可测试性。

### 3.4 项目结构（实际）

```
src/Veda.Agents/
  Veda.Agents.csproj
  GlobalUsings.cs
  AgentServiceExtensions.cs         # AddVedaAgents()，注册 LlmOrchestrationService
  VedaKernelPlugin.cs               # search_knowledge_base KernelFunction（供 ChatCompletionAgent 调用）
  LlmOrchestrationService.cs        # LLM 驱动 Agent Loop（ChatCompletionAgent + IRCoT）— 默认实现
  Orchestration/
    IOrchestrationService.cs        # 接口定义
    OrchestrationService.cs         # 确定性调用链实现（备用，便于测试）
```

> `AgentServiceExtensions.AddVedaAgents()` 注册 `LlmOrchestrationService` 为 `IOrchestrationService` 的默认实现。

---

## 4. Veda.Prompts — Prompt 工程层（补全）

### 4.1 实现内容

| 类 | 职责 |
|----|------|
| `PromptTemplate` | 模板实体（Name, Version, Content, DocumentType） |
| `PromptTemplateRepository` | 从 EF Core 加载/保存版本化模板 |
| `ContextWindowBuilder` | 按 Token 预算从候选块中选择最优上下文 |
| `ServiceCollectionExtensions` | `AddVedaPrompts()` DI 注册 |

### 4.2 与现有代码集成

- `QueryService` 通过 `IContextWindowBuilder` 接口替换当前硬编码的 `Take(topK)` 逻辑。
- `Veda.Core.Interfaces` 新增 `IContextWindowBuilder`。

---

## 5. VSCode Copilot Chat 集成

### 5.1 `.vscode/mcp.json` 配置

**方式一：HTTP（推荐，Veda.Api 已运行时）**

```json
{
  "servers": {
    "vedaaide": {
      "type": "http",
      "url": "http://localhost:5126/mcp"
    }
  }
}
```

> 当前 `ModelContextProtocol.AspNetCore` 使用 Streamable HTTP 协议，挂载路径为 `/mcp`。

**方式二：stdio（独立进程，无需 API 先启动）**  
需要将 `Veda.MCP` 编译为独立可执行程序（`OutputType=Exe`）。当前未实现此模式。

### 5.2 验证步骤

1. 启动 `Veda.Api`（`dotnet run`）。
2. 打开 VSCode Copilot Chat，切换到 **Agent 模式**（`@`）。
3. 输入：`@vedaaide 帮我搜索关于XX的文档`。
4. 观察 Tool 调用日志，确认 `search_knowledge_base` 被触发。

---

## 6. 数据库变更（EF Core 迁移）

阶段四新增 `PromptTemplate` 表：

```csharp
public class PromptTemplateEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Content { get; set; }
    public string? DocumentType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

迁移命令：
```bash
dotnet ef migrations add Phase4_PromptTemplates --project src/Veda.Storage --startup-project src/Veda.Api
```

> ✅ 迁移已生成：`src/Veda.Storage/Migrations/`，`Program.cs` 中的 `MigrateAsync()` 在启动时自动应用。

---

## 7. 测试策略

| 测试类型 | 位置 | 覆盖范围 | 状态 |
|---------|------|---------|------|
| MCP Tool 单元测试 | `tests/Veda.Services.Tests/` | Mock `IVectorStore`，验证 `KnowledgeBaseTools` / `IngestTools` 入参/出参格式 | ✅ 已完成 |
| Agent 编排单元测试 | `tests/Veda.Services.Tests/` | Mock 所有依赖，验证 `OrchestrationService` 调用链 | ✅ 已完成 |
| Prompt 模块单元测试 | `tests/Veda.Core.Tests/` | `ContextWindowBuilder` Token 预算逻辑、`ChainOfThoughtStrategy` 输出格式 | ✅ 已完成 |
| 外部数据源单元测试 | `tests/Veda.Services.Tests/` | `FileSystemConnector` 正常/目录不存在/disabled 场景 | ✅ 已完成 |
| MCP 集成测试 | `tests/Veda.Services.Tests/` | 启动内存 WebApp，调用 `/mcp` 验证工具响应 | ⏳ 待实现 |

---

## 8. 开发顺序

```
Step 1: Veda.Prompts 补全代码（无外部依赖，最简单）
Step 2: Veda.MCP 项目 + Tools 定义
Step 3: 挂载 MCP 到 Veda.Api（Program.cs + csproj）
Step 4: .vscode/mcp.json + 手动 VSCode 验证
Step 5: Veda.Agents 项目 + Agent 定义
Step 6: 更新 VedaAide.slnx
Step 7: 单元测试
```

---

## 9. 风险与应对

| 风险 | 应对 |
|------|------|
| MCP SDK API 变动（仍在活跃迭代） | 锁定 NuGet 包版本，升级前先跑测试 |
| SK Agents API 不稳定 | 阶段四用手动调用链替代 `AgentGroupChat`，降低依赖 |
| MCP HTTP/SSE CORS 问题 | 在 `Veda.Api` 中为 `/mcp/*` 路由配置 AllowAll CORS 策略 |
| `PromptTemplate` 表未迁移导致启动失败 | `Program.cs` 已有 `MigrateAsync()`，自动建表 |
