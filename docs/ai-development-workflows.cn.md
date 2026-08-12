# AI 开发工作流（GitHub Actions）

本仓库内置三个 GitHub Actions 工作流，让 AI 代理
（[Reasonix](https://www.npmjs.com/package/reasonix) + DeepSeek）自动完成开发、Review 与修复。
实现参考自 `diffusion-rag` 项目中的同名工作流，已按本仓库的 .NET 技术栈适配。

## 工作流一览

| 工作流 | 触发方式 | 作用 |
|--------|----------|------|
| `reasonix_develop.yml` | Issue 被标记 `ai-dev` 标签 | Reasonix 按 Issue 需求自主开发，随后创建关闭该 Issue 的 PR（`reasonix-dev/issue-<n>`） |
| `reasonix_pr_feedback.yml` | 在 PR 上评论包含 `/fix` | 检出 PR 分支，Reasonix 按评论反馈修复代码，验证通过后 push |
| `reasonix_pr_pipeline.yml` | PR 打开 / 更新 / 提交 Review | 依次执行静态检查（`dotnet build` / `test`）→ AI 自动修复 → AI 代码 Review → 按 Review 结果自动修复 → 全部通过后 push |

三个工作流都会跳过 bot 自身的提交以防止死循环；流水线工作流按 PR 号做并发控制。

## 需要的 Secrets

请在 **Settings → Secrets and variables → Actions** 中配置：

| Secret | 用途 |
|--------|------|
| `DEEPSEEK_API_KEY` | Reasonix 模型（DeepSeek）的 API Key |
| `GH_PAT` | 具有 `repo` 权限的 Personal Access Token，用于 `create-pull-request` 与 push（必须用 PAT 而非 `GITHUB_TOKEN`，这样创建 PR 才能触发其他工作流） |

## 任务配置

打上 `ai-dev` 标签的 Issue（以及自动创建的 PR 正文）支持以下 yaml 风格字段：

```text
model: deepseek-flash    # deepseek-flash（默认）| deepseek-pro
level: medium            # low | medium（默认）| high
type: code               # code（默认）| docs
```

- `model`：选择 Reasonix 使用的模型（复杂任务可用 `deepseek-pro`）。
- `level`：传给代理的努力程度。
- `type: docs`：任务提示切换到文档编写标准而非代码标准。

## PR 上的 Review 反馈

如需让代理按 Review 意见修复，在 PR 上回复：

```text
/fix <你希望修改的内容>
```

代理会修改 PR 分支，运行 `dotnet build` / `dotnet test`，
验证通过后 push（检查失败时会额外自修复一轮）。

## 说明

- 所有生成的 prompt 文件都放在 `$RUNNER_TEMP`，不会污染代码差异。
- 注入给 AI 的工程规范位于 `.reasonix/commands/`（`develop.md`、`code-review.md`、
  `fix-bug.md`、`feature.md`、`e2e-test.md`、`github-issues.md`）以及
  `.reasonix/copilot-instructions.md` —— 修改它们即可改变 AI 对本仓库的理解。
- `reasonix.toml` 用于配置本地 Reasonix CLI（模型、权限、沙箱）。
