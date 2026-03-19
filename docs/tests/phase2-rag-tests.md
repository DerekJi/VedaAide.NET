# 阶段二 RAG 质量增强测试方案

## 1. 测试范围

阶段二在阶段一基础上新增以下质量增强组件：

| 组件 | 测试类型 | 测试文件 |
|---|---|---|
| `DocumentIngestService`（相似度去重） | 单元（Mock） | `Veda.Services.Tests/DocumentIngestServiceTests.cs` |
| `QueryService`（Reranking + 防幻觉第一层） | 单元（Mock） | `Veda.Services.Tests/QueryServiceTests.cs` |
| `HallucinationGuardService`（防幻觉第二层） | 单元（Mock） | `Veda.Services.Tests/HallucinationGuardServiceTests.cs` |
| `SqliteVectorStore`（日期范围过滤） | 集成（临时 SQLite） | `Veda.Services.Tests/SqliteVectorStoreIntegrationTests.cs` |
| `POST /api/documents`（去重行为） | 冒烟 | `scripts/smoke-test.sh` |
| `POST /api/query`（防幻觉字段） | 冒烟 | `scripts/smoke-test.sh` |
| `POST /api/query`（日期过滤） | 冒烟 | `scripts/smoke-test.sh` |


## 2. 新增功能说明与测试目标

### 2.1 向量相似度去重（摄取阶段）

**功能位置：** `DocumentIngestService.IngestAsync`，在 Embedding 之后、`UpsertBatchAsync` 之前。

**核心逻辑：** 对每个新分块，先向向量库检索 TopK=1 的最近邻；若相似度 ≥ `RagOptions.SimilarityDedupThreshold`（默认 0.95），则跳过该块，不入库。

**测试目标：**

| 场景 | 预期行为 |
|---|---|
| 新内容，向量库为空或无近似块 | 全部分块入库，`ChunksStored` 等于分块总数 |
| 单块与已存储内容相似度 ≥ 阈值 | 跳过该块，`ChunksStored` 减少 1 |
| 所有分块均为近似重复 | `UpsertBatchAsync` 不被调用，`ChunksStored = 0` |
| 阈值设为 `1.1f`（永不触发） | 所有分块正常入库，行为退化为阶段一 |

**可配置性验证：** 将 `appsettings.json` 中 `Veda:Rag:SimilarityDedupThreshold` 改为 `0.0` 后重新摄取同一文档，应跳过所有分块（因为与自身完全相同的向量相似度 = 1.0）。


### 2.2 防幻觉第一层（回答 Embedding 校验）

**功能位置：** `QueryService.QueryAsync`，在 LLM 生成回答之后。

**核心逻辑：** 对回答文本做 Embedding，向量库检索 TopK=1；若最高相似度 < `RagOptions.HallucinationSimilarityThreshold`（默认 0.3），则设置 `RagQueryResponse.IsHallucination = true`。

**测试目标：**

| 场景 | 预期行为 |
|---|---|
| 回答向量与文档库相似度较高（≥ 0.3） | `IsHallucination = false` |
| 回答向量与文档库相似度极低（< 0.3） | `IsHallucination = true` |
| 文档库为空（无任何匹配） | 最高相似度 = 0，`IsHallucination = true` |


### 2.3 防幻觉第二层（LLM 自我校验）

**功能位置：** `HallucinationGuardService.VerifyAsync`，由 `QueryService` 在第一层通过后、且 `EnableSelfCheckGuard = true` 时调用。

**核心逻辑：** 向 LLM 发送严格事实核查提示词，要求返回 `true` 或 `false`；仅当返回值以 `true` 开头（忽略大小写）时认为回答有依据。

**测试目标：**

| 场景 | 预期行为 |
|---|---|
| LLM 返回 `"true"` | `VerifyAsync` 返回 `true`（回答有依据） |
| LLM 返回 `"false"` | `VerifyAsync` 返回 `false`（疑似幻觉） |
| LLM 返回 `" True\n"`（含空白） | 正确处理，返回 `true` |
| `EnableSelfCheckGuard = false` | 第二层完全跳过，`VerifyAsync` 不被调用 |
| 第一层已判定幻觉 | 第二层不被调用（短路逻辑） |


### 2.4 Reranking（检索结果重排序）

**功能位置：** `QueryService.QueryAsync` 中的私有方法 `Rerank`。

**核心逻辑：** 初始检索 `TopK × RerankCandidatesMultiplier`（默认 2×）个候选块，按 70% 向量相似度 + 30% 关键词覆盖率重新打分排序，取前 `TopK` 个。

**测试目标：**

| 场景 | 预期行为 |
|---|---|
| 有多个候选块，其中部分包含问题关键词 | 包含更多关键词的块排名靠前 |
| `TopK=3` 但候选块多于 3 个 | 最终 `Sources` 数量 = 3 |
| 所有候选均无问题关键词 | 退化为纯向量相似度排序 |


### 2.5 日期范围过滤（向量检索阶段）

**功能位置：** `SqliteVectorStore.SearchAsync`（`WHERE CreatedAtTicks >= dateFrom` / `<= dateTo`）；  
通过 `RagQueryRequest.DateFrom` / `DateTo` 字段传入，API 层通过 `QueryRequest.DateFrom` / `DateTo` 接收。

**测试目标：**

| 场景 | 预期行为 |
|---|---|
| `DateFrom` 设为某时间点，库中有早于和晚于该时间点的块 | 只返回晚于（或等于）`DateFrom` 的块 |
| `DateTo` 设为某时间点 | 只返回早于（或等于）`DateTo` 的块 |
| 同时设置 `DateFrom` 和 `DateTo` | 只返回时间窗口范围内的块 |
| 不设置 `DateFrom` / `DateTo`（null） | 不过滤，返回所有符合其他条件的块 |


## 3. 冒烟测试方案

在阶段一冒烟测试基础上，追加以下 Phase 2 专项冒烟验证：

| 测试点 | 操作 | 验证内容 |
|---|---|---|
| 去重行为 | 连续两次 `POST /api/documents` 提交相同内容 | 第二次返回 `chunksStored = 0` |
| 防幻觉字段 | `POST /api/query` 正常查询 | 响应 JSON 中包含 `isHallucination` 字段（`true` 或 `false`） |
| 日期过滤 | `POST /api/query`，`dateFrom` 设为远未来时间（如 `"2099-01-01T00:00:00Z"`） | `sources` 列表为空，`answer` 返回"没有足够信息" |
| 日期过滤 | `POST /api/query`，不传 `dateFrom`/`dateTo` | 行为与阶段一相同，正常返回结果 |

### 冒烟测试请求示例

#### 去重验证：摄取相同内容两次

```bash
CONTENT="RAG quality test content."

curl -s -X POST http://localhost:5126/api/documents \
  -H "Content-Type: application/json" \
  -d "{\"content\": \"$CONTENT\", \"documentName\": \"dedup-test.txt\"}"
# 预期：chunksStored > 0

curl -s -X POST http://localhost:5126/api/documents \
  -H "Content-Type: application/json" \
  -d "{\"content\": \"$CONTENT\", \"documentName\": \"dedup-test-2.txt\"}"
# 预期：chunksStored = 0（向量相似度 = 1.0，触发去重）
```

#### 日期过滤验证

```bash
curl -s -X POST http://localhost:5126/api/query \
  -H "Content-Type: application/json" \
  -d '{
    "question": "What is this document about?",
    "dateFrom": "2099-01-01T00:00:00Z"
  }'
# 预期：sources = []，answer 包含 "don't have enough information"
```

#### 防幻觉字段验证

```bash
curl -s -X POST http://localhost:5126/api/query \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the answer to everything?"}' \
  | grep -o '"isHallucination":[a-z]*'
# 预期：输出 "isHallucination":true 或 "isHallucination":false
```


## 4. 配置说明

阶段二各阈值均可通过 `appsettings.json`（`Veda:Rag` 节）覆盖，无需重新编译：

```json
"Veda": {
  "Rag": {
    "SimilarityDedupThreshold": 0.95,
    "HallucinationSimilarityThreshold": 0.3,
    "EnableSelfCheckGuard": false
  }
}
```

| 参数 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `SimilarityDedupThreshold` | `float` | `0.95` | 摄取去重阈值，调低会去重更多内容 |
| `HallucinationSimilarityThreshold` | `float` | `0.3` | 幻觉判定阈值，调高会使更多回答被标记为幻觉 |
| `EnableSelfCheckGuard` | `bool` | `false` | 开启后每次查询额外消耗一次 LLM 调用 |


## 5. 测试执行命令

```bash
# 运行所有单元 + 集成测试（含阶段二新增用例）
dotnet test

# 仅运行 Services 层测试（含去重、防幻觉、Reranking 相关测试）
dotnet test tests/Veda.Services.Tests/

# 运行冒烟测试（确保 API 已启动在 5126 端口）
./scripts/smoke-test.sh

# 或自动启停 API
./scripts/smoke-test.sh --start-api
```
