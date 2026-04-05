# FSM Design for QueryAsync Refactoring

## English Version

### Overview
Finite State Machines (FSM) are a powerful tool for managing complex workflows with distinct states and transitions. In the context of the `QueryAsync` method, FSM can help simplify the logic by breaking it into discrete states, each responsible for a specific part of the process.

### Proposed FSM States
1. **Initial**: Validate the input request.
2. **QueryExpansion**: Expand the user query using the `semanticEnhancer`.
3. **CacheCheck**: Check the semantic cache for a precomputed answer.
4. **Retrieval**: Retrieve candidates using hybrid or vector-based retrieval.
5. **Reranking**: Rerank the retrieved candidates.
6. **ContextBuilding**: Build the context for the LLM.
7. **AnswerGeneration**: Generate the answer using the LLM.
8. **HallucinationDetection**: Detect hallucinations in the generated answer.
9. **CacheUpdate**: Update the semantic cache with the new answer.
10. **ResponseBuilding**: Construct the final response object.

### FSM Implementation
Each state will be implemented as a method, and transitions will be managed by a central state machine controller. For example:

```csharp
public enum QueryState
{
    Initial,
    QueryExpansion,
    CacheCheck,
    Retrieval,
    Reranking,
    ContextBuilding,
    AnswerGeneration,
    HallucinationDetection,
    CacheUpdate,
    ResponseBuilding,
    Completed
}

public class QueryStateMachine
{
    private QueryState _state = QueryState.Initial;

    public async Task<RagQueryResponse> ExecuteAsync(RagQueryRequest request, CancellationToken ct)
    {
        while (_state != QueryState.Completed)
        {
            switch (_state)
            {
                case QueryState.Initial:
                    ValidateRequest(request);
                    _state = QueryState.QueryExpansion;
                    break;

                case QueryState.QueryExpansion:
                    var expandedQuestion = await ExpandQueryAsync(request, ct);
                    _state = QueryState.CacheCheck;
                    break;

                case QueryState.CacheCheck:
                    var cachedResponse = await CheckSemanticCacheAsync(expandedQuestion, request, ct);
                    if (cachedResponse != null)
                        return cachedResponse;
                    _state = QueryState.Retrieval;
                    break;

                // Additional states...

                case QueryState.ResponseBuilding:
                    return BuildResponse(answer, rerankedResults, isHallucination);
            }
        }

        throw new InvalidOperationException("Query state machine reached an invalid state.");
    }
}
```

### Advantages of FSM
- **Clarity**: Each state has a single responsibility, making the logic easier to understand.
- **Extensibility**: Adding new states or transitions is straightforward.
- **Debuggability**: Issues can be isolated to specific states.

---

## 中文版本

### 概述
有限状态机（FSM）是一种强大的工具，用于管理具有明确状态和转换的复杂工作流。在 `QueryAsync` 方法的上下文中，FSM 可以通过将逻辑分解为离散状态来简化流程，每个状态负责特定的处理阶段。

### 提议的 FSM 状态
1. **Initial**：验证输入请求。
2. **QueryExpansion**：使用 `semanticEnhancer` 扩展用户查询。
3. **CacheCheck**：检查语义缓存中是否存在预计算答案。
4. **Retrieval**：使用混合检索或向量检索获取候选文档块。
5. **Reranking**：对检索到的候选文档块进行重排序。
6. **ContextBuilding**：为 LLM 构建上下文。
7. **AnswerGeneration**：使用 LLM 生成答案。
8. **HallucinationDetection**：检测生成答案中的幻觉。
9. **CacheUpdate**：将新答案更新到语义缓存。
10. **ResponseBuilding**：构建最终的响应对象。

### FSM 实现
每个状态将实现为一个方法，状态之间的转换由中央状态机控制器管理。例如：

```csharp
public enum QueryState
{
    Initial,
    QueryExpansion,
    CacheCheck,
    Retrieval,
    Reranking,
    ContextBuilding,
    AnswerGeneration,
    HallucinationDetection,
    CacheUpdate,
    ResponseBuilding,
    Completed
}

public class QueryStateMachine
{
    private QueryState _state = QueryState.Initial;

    public async Task<RagQueryResponse> ExecuteAsync(RagQueryRequest request, CancellationToken ct)
    {
        while (_state != QueryState.Completed)
        {
            switch (_state)
            {
                case QueryState.Initial:
                    ValidateRequest(request);
                    _state = QueryState.QueryExpansion;
                    break;

                case QueryState.QueryExpansion:
                    var expandedQuestion = await ExpandQueryAsync(request, ct);
                    _state = QueryState.CacheCheck;
                    break;

                case QueryState.CacheCheck:
                    var cachedResponse = await CheckSemanticCacheAsync(expandedQuestion, request, ct);
                    if (cachedResponse != null)
                        return cachedResponse;
                    _state = QueryState.Retrieval;
                    break;

                // 其他状态...

                case QueryState.ResponseBuilding:
                    return BuildResponse(answer, rerankedResults, isHallucination);
            }
        }

        throw new InvalidOperationException("Query state machine reached an invalid state.");
    }
}
```

### FSM 的优势
- **清晰性**：每个状态具有单一职责，使逻辑更易于理解。
- **可扩展性**：添加新状态或转换非常简单。
- **可调试性**：问题可以被隔离到特定状态。