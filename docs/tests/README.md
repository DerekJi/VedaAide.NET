# VedaAide.NET 测试体系文档

本目录包含测试方案设计、测试方法说明及各阶段的测试指引。

## 目录结构

```
docs/tests/
├── README.md              ← 本文件，测试体系总览
├── phase1-rag-tests.md    ← 阶段一 RAG 引擎核心测试方案
├── phase2-rag-tests.md    ← 阶段二 RAG 质量增强测试方案（去重、防幻觉、Reranking、日期过滤）
└── test-conventions.md   ← 测试命名规范与编写约定
```

## 测试分层

| 层级 | 用途 | 工具 | 运行方式 |
|---|---|---|---|
| **单元测试** | 验证单个类/方法的业务逻辑，不依赖外部服务 | NUnit + Moq | `dotnet test` |
| **集成测试** | 验证模块间协作（EF Core + SQLite），使用内存/临时 DB | NUnit | `dotnet test` |
| **冒烟测试** | 验证 API 端到端主要流程是否可用 | Bash (`scripts/smoke-test.sh`) | `./scripts/smoke-test.sh` |
| **AI 评估测试** | 验证 RAG 输出质量（忠实度、相关性等） | `Veda.Evaluation`（阶段五） | `dotnet test` |

## 快速运行

```bash
# 单元 + 集成测试
dotnet test

# 冒烟测试（手动启动 API，再运行）
dotnet run --project src/Veda.Api &
sleep 8
./scripts/smoke-test.sh

# 冒烟测试（自动启停 API，一行完成）
./scripts/smoke-test.sh --start-api

# 指定自定义 API 地址
./scripts/smoke-test.sh http://your-server:5126
```

## 启停 API（开发用）

```bash
# 启动（等待就绪后返回）
./scripts/start-api.sh

# 停止（只停 Veda.Api，不影响其他 dotnet 项目）
./scripts/stop-api.sh
```

> **注意：** 绝对不要用 `taskkill //F //IM dotnet.exe` 或 `pkill dotnet` 来解除文件锁，
> 这会杀掉所有 dotnet 进程。始终用 `pkill -f "Veda.Api"` 精确定位。

## 覆盖率要求

- `Veda.Core`：≥ 80%（纯领域逻辑，应高覆盖）
- `Veda.Services`：≥ 80%（Mock LLM + Mock VectorStore）
- `Veda.Storage`：≥ 60%（集成测试覆盖）
- `Veda.Api`：冒烟测试覆盖主要端点即可
