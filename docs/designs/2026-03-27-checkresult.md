## 一、一二三期实现完整性

**编译无错误，整体完成度较高。** 对照文档逐条核对：

### 已实现 ✅
| 模块 | 状态 |
|---|---|
| 一期：RAG底座、防幻觉、Agent、MCP Server、评估体系、GraphQL | ✅ |
| 二期：CosmosDB切换、LLM路由(simple/advanced)、语义缓存、API Key认证、CORS、Rate Limiting、Admin工具 | ✅ |
| 三期Sprint1：HybridRetriever、KnowledgeScope过滤、SearchByKeywordsAsync | ✅ |
| 三期Sprint2：DocumentIntelligence提取、VisionModel提取、文件上传端点 | ✅ |
| 三期Sprint3：StructuredFinding、StructuredOutputParser、DocumentDiffService、版本化字段、SemanticEnhancer | ✅ |
| 三期Sprint4：UserBehaviorEvent、UserMemoryStore、FeedbackBoostService、GovernanceController、隐私隔离 | ✅ |

### 尚未实现 ⚠️（与设计文档明确标注的遗留项一致）
| 项目 | 说明 |
|---|---|
| `AdminController.Stats` 无缓存命中率统计 | Sprint3遗留 |
| `DocumentIngestService` ingest后不触发cache失效 | Sprint3遗留 |
| `/mcp` 端点未受API Key保护 | Sprint2遗留安全风险 |
| 摄取完整性评估指标（`Veda.Evaluation`集成） | Sprint2遗留 |

---

## 二、代码逻辑一致性问题与Bug

### 🐛 Critical Bug：`MarkDocumentSupersededAsync` 会把新chunk也标记为已取代

**位置**：`DocumentIngestService.IngestAsync`，SQLite和CosmosDB两个实现均受影响。

**原因**：调用顺序是：
1. `UpsertBatchAsync(deduped)` → 新chunk写入，`SupersededAtTicks == 0`
2. `MarkDocumentSupersededAsync(documentName, newDocumentId)` → WHERE条件是 `DocumentName == name AND SupersededAtTicks == 0`

步骤2的WHERE会同时命中旧chunk和刚插入的新chunk，导致新chunk立刻被标记为已取代（被自身取代），之后所有查询（`WHERE SupersededAtTicks == 0`）都找不到新文档内容。

**还有额外一个CosmosDB的bug**：`MarkDocumentSupersededAsync`的Patch操作使用了`PartitionKey.None`，而容器的PartitionKey是`/documentId`，Patch需要精确的PartitionKey，跨分区操作在CosmosDB中对写/更新是不被支持的，会抛出异常。

### 🐛 Bug：`QueryStreamAsync` 与 `QueryAsync` 行为不一致
流式查询存在三处遗漏（而非流式路径都有正确实现）：

| 功能 | QueryAsync | QueryStreamAsync |
|---|---|---|
| HybridRetriever（双通道检索） | ✅ 根据配置使用 | ❌ 始终直接调用`vectorStore.SearchAsync` |
| KnowledgeScope过滤 | ✅ 传入`scope: request.Scope` | ❌ 缺少`scope`参数，完全忽略 |
| FeedbackBoost个性化排序 | ✅ 按userId应用 | ❌ 没有调用`feedbackBoostService` |

### ⚠️ 潜在并发Bug：HybridRetriever并发操作同一DbContext

```csharp
// HybridRetriever.cs
var vectorTask = vectorStore.SearchAsync(...);    // 启动第一个查询
var keywordTask = vectorStore.SearchByKeywordsAsync(...)  // 立即启动第二个查询
await Task.WhenAll(vectorTask, keywordTask);      // 并发等待
```

两个Task都使用同一个`SqliteVectorStore`实例（Scoped），进而使用同一个`VedaDbContext`。EF Core的`DbContext`不支持并发操作，可能抛出 `InvalidOperationException: A second operation was started on this context instance before a previous asynchronous operation completed`。SQLite情况下可能偶现，CosmosDB路径无此问题（CosmosDB实现不依赖DbContext）。

### ⚠️ 安全隐患：`IsDocumentVisibleToUserAsync` 使用字符串Contains匹配userId

```csharp
var groups = await db.SharingGroups
    .Where(g => g.MembersJson.Contains(userId))  // 潜在子串误匹配
```

若userId=`user1`，MembersJson=`["user12","user13"]`，Contains会错误命中。应改用正确的JSON解析或增加引号边界匹配（`Contains($"\"{userId}\"")`）。

---

## 三、切换数据库时的Migration处理

**已有自动Migration机制，但仅覆盖SQLite部分。**

### 现有机制

`Program.cs`启动时执行：
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VedaDbContext>();
    await db.Database.MigrateAsync();  // 自动应用所有pending migrations
}
```

如果切到CosmosDB则额外异步初始化：
```csharp
var cosmosInitializer = app.Services.GetService<CosmosDbInitializer>();
if (cosmosInitializer is not null)
    await cosmosInitializer.EnsureReadyAsync(initCts.Token);  // 创建Database/Container/Vector Index
```

### 切换场景分析

| 场景 | 行为 |
|---|---|
| **SQLite→SQLite**（首次启动或新schema字段） | ✅ `MigrateAsync()`自动应用，安全 |
| **CosmosDB→CosmosDB**（首次启动） | ✅ `CosmosDbInitializer`自动创建容器和向量索引（幂等） |
| **SQLite切换到CosmosDB**（修改`Veda:StorageProvider`） | ⚠️ 旧SQLite数据**不会迁移**到CosmosDB，知识库需重新ingest；`MigrateAsync()`仍会跑（SQLite元数据库独立存在），向量数据从零开始 |
| **CosmosDB切换到SQLite** | ⚠️ 同上，CosmosDB中的向量数据不会同步到SQLite，需重新摄取文档 |
| **Embedding模型变更（维度变化）** | ❌ **没有自动检测**，旧embedding维度与新模型不兼容，需手动`DELETE /api/admin/data`清库后重新ingest |

**结论**：切换StorageProvider不会报错、不会崩溃（`MigrateAsync`只操作SQLite元数据库，向量存储各走各的路），但**向量知识库数据不会自动迁移**，切换后需要重新触发数据源同步。设计文档中已明确说明这个行为（"修改配置 + 清库 + 重新ingest"）。

---

## 建议修复优先级

| 优先级 | 问题 |
|---|---|
| P0（数据损坏） | `MarkDocumentSupersededAsync` 误标新chunk：应在`UpsertBatch`前先标记旧chunk，或在WHERE中增加`DocumentId != newDocumentId` |
| P0（CosmosDB） | `PatchItemAsync`使用`PartitionKey.None`，需改为从查询结果中获取`documentId`作为PartitionKey |
| P1（行为不一致） | `QueryStreamAsync`补充：HybridRetriever、KnowledgeScope、FeedbackBoost |
| P1（并发安全） | `HybridRetriever`改为顺序await，或使用独立DbContext作用域 |
| P2（安全） | `SharingGroups.MembersJson.Contains(userId)` 改为精确JSON匹配 |
| P2（遗留缺口） | cache失效、stats缓存统计、`/mcp`认证 | 

