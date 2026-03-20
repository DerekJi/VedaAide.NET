# 本地 Ollama Embedding：为什么不用云端 API

**项目**：VedaAide.NET  
**涉及文件**：`src/Veda.Services/EmbeddingService.cs`、`src/Veda.Services/ServiceCollectionExtensions.cs`  
**阶段**：Phase 1

---

## 决策

Embedding 使用本地 Ollama（`nomic-embed-text` / `bge-m3`），而不是 OpenAI `text-embedding-ada-002` 或 Azure OpenAI。

---

## 为什么不用云端 Embedding API

| 维度 | 云端 API | 本地 Ollama |
|---|---|---|
| 成本 | 按 token 计费，大批量摄取贵 | 零边际成本 |
| 延迟 | 受网络 RTT 影响，~50–200ms/次 | 本地推理，~5–30ms/次 |
| 隐私 | 文档内容出站，不适合私密数据 | 数据不离开本机 |
| 离线 | 依赖网络 | 完全离线可用 |
| 模型锁定 | 供应商模型，无法自选 | 可随时切换任意 GGUF 模型 |

对于私有知识库场景，**隐私和成本是决定性因素**，本地 Embedding 是自然选择。

---

## 架构隔离：不直接依赖 Ollama SDK

`EmbeddingService` 依赖 `Microsoft.Extensions.AI` 的标准接口 `IEmbeddingGenerator<string, Embedding<float>>`，而不是 Ollama SDK 的具体类型：

```csharp
// EmbeddingService.cs — 领域层只依赖标准接口，不感知底层实现
public sealed class EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> inner) : IEmbeddingService
```

Ollama 的注册在 `ServiceCollectionExtensions.cs` 的 DI 配置里，通过 `.AddOllamaEmbeddingGenerator()` 绑定。

**好处**：将来切换到 Azure OpenAI Embedding 或 `bge-m3`，只需改一行 DI 注册，`EmbeddingService` 和上层代码零修改。这是 **DIP（依赖倒置原则）** 在 AI 服务层的具体应用。

---

## 模型选型的权衡

| 模型 | 特点 | 适用场景 |
|---|---|---|
| `nomic-embed-text` | 轻量（~270MB），英文优先 | 英文文档，资源受限环境 |
| `bge-m3` | 较大（~570MB），中英日多语 | 中文内容，生产环境推荐 |
| `mxbai-embed-large` | 英文高精度 | 英文专业文档 |

初始选用 `nomic-embed-text` 是为了快速验证流程；中文内容场景应换 `bge-m3`（MTEB 中文榜前列），切换只需改 `appsettings.json` 中的 `EmbeddingModel` 并重建向量库。

---

## 踩坑记录

- **向量维度绑定**：`nomic-embed-text` 输出 768 维，`bge-m3` 输出 1024 维。SQLite 建表时维度硬编码，切换模型后**旧向量必须清空重建**，不能混用。
- **中文召回率低**：`nomic-embed-text` 对中文的语义理解较弱，中文同义句的余弦相似度明显低于英文，导致检索召回率下降。这是当前系统的已知局限。

---

## 面试延伸点

- AI 工程里的 DIP：接口隔离 LLM / Embedding 供应商，是企业级 AI 应用的标配设计
- 本地 vs. 云端 Embedding 的成本分析：大批量摄取时本地节省的不只是钱，还有延迟叠加效应
- 向量维度一致性问题：为什么切换 Embedding 模型必须重建向量库（内积/余弦相似度在不同维度空间无意义）
