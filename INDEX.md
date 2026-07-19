# Local Media Manager — Knowledge System

> **Knowledge System v1.0**
>
> **状态：** 维护期（Maintenance）— 结构已冻结，只补内容，不再重构。
>
> **定案日期：** 2026-07-18
>
> **方法论：** Knowledge-Driven Development（KDD）

---

## 这是什么

Local Media Manager 采用 **Knowledge-Driven Development（KDD）**，不是传统的「先写代码、后补文档」。

```text
Code
  ↓
Decision（定案）
  ↓
Knowledge（PROJECT / DECISION / AGENTS）
  ↓
AI（下一程实现）
  ↓
Next Code
```

**v1.0 里程碑：** 文档体系**设计阶段结束**。知识体系进入维护期；产品开发恢复正常节奏。

---

## 固定结构（不再重构）

```text
LocalMediaManager/
│
├── INDEX.md              ← 本文件：入口、里程碑、维护规则
├── PROJECT.md            ← 项目百科：当前系统是什么
├── DECISION_LOG.md       ← 决策定案：为什么会这样
├── AGENTS.md             ← AI 手册：AI 应该怎么工作
└── docs/                 ← 证据层：Release、Audit、Matrix、Schema…
```

| 文件 | 职责 | 更新原则 |
|------|------|---------|
| **INDEX.md** | 入口与维护规则 | 极少；结构变更时 |
| **PROJECT.md** | **当前真相** — 系统是什么 | **仅当**当前真相改变 |
| **DECISION_LOG.md** | 已定案决策 + 状态 | 每个 Sprint 定案后 **新增 DEC-xxx** |
| **AGENTS.md** | AI 行为（Mission / Workflow / Rules） | **仅当** AI 规范改变；宜短，引用 PROJECT/DEC |
| **docs/** | 证据，不写原则 | Release / 验收 / 映射 / 审计 |

**原则层永远在 PROJECT。** docs 不放设计原则。

---

## 阅读顺序

### AI / 新会话

```text
1. INDEX.md（本文件，可选）
2. PROJECT.md
3. DECISION_LOG.md（Settings / MediaStorage / 写入 / Legacy 必读）
4. AGENTS.md
5. docs/…（按任务：ROADMAP · TODO · MATRIX · TEST_PLAN）
```

### Human / 维护者

```text
INDEX → ROADMAP/TODO → PROJECT（查当前设计）→ DECISION_LOG（查为什么）
```

---

## Decision 状态

每条 Decision 必须有 **Status**：

| Status | 含义 | AI 行为 |
|--------|------|---------|
| **Proposed** | 讨论中，未定案 | 不得当作规则引用 |
| **Accepted** | 已定案，当前有效 | 必须遵守 |
| **Superseded** | 已被新 Decision 取代 | 读新 DEC-xxx，勿用旧方案 |
| **Deprecated** | 明示废弃 | 禁止引用 |

**取代示例：**

```text
DEC-004  Status: Superseded  →  见 DEC-028
DEC-028  Status: Accepted
```

当前有效决策：**DEC-001 ～ DEC-010**（均为 Accepted）。见 `DECISION_LOG.md`。

---

## Sprint 完成模板（固定）

Sprint **Done** 须按序完成；缺项不算完成。

```text
① Code
② Build
③ Test
④ Smoke          ← Human
⑤ Human Acceptance
⑥ DEC-xxx        ← 新增或更新 Decision（Status: Accepted）
⑦ PROJECT        ← 仅当「当前真相」变化
⑧ AGENTS         ← 仅当 AI 规范变化
⑨ CHANGELOG + docs/releases
Done
```

**禁止：** 代码已合并、设计边界已变，却不写 Decision 就改 PROJECT。

**禁止：** 无 Decision 依据的整体重构 PROJECT 结构（v1.0 后结构冻结）。

详细门槛见 `PROJECT.md` §25 Definition of Done · `AGENTS.md` §02。

---

## PROJECT 维护规则

- PROJECT 描述 **当前版本真相**，不写产品版本号（如 0.5.0、0.4.3）。
- **版本范围** → `docs/ROADMAP.md`
- **已发布行为** → `docs/CHANGELOG.md`
- **历史演变** → `DECISION_LOG.md`，不在 PROJECT 重复踩坑故事。
- PROJECT 目标：**越来越完整**，不是越来越长。
- 缺章标 `[待补]`，按需增量补全；**不**为 Sprint 在 PROJECT 里堆版本段落。

---

## AGENTS 维护规则

- AGENTS 只保留 **AI 行为**：Mission · Workflow · Boundary · Report · Rules。
- 细节引用 PROJECT §xx 与 DEC-xxx，**不**复制百科正文。
- 随 Decision 增多，AGENTS Rules 表可增行，但**不宜**整章重复解释。
- 目标：**越来越短、越来越准**（通过引用而非堆字）。

---

## docs/ 证据层

docs **只存证据**，不存原则：

| 目录/文件 | 内容 |
|-----------|------|
| `docs/ROADMAP.md` | 版本范围权威 |
| `docs/TODO.md` | 未完成工作 |
| `docs/CHANGELOG.md` | 已发布记录 |
| `docs/migration/` | 矩阵、Legacy 规则、字段映射 |
| `docs/releases/` | Release 验证报告 |
| `docs/audits/` | 审计报告 |
| `docs/database/` | Schema 设计、Settings 策略 |
| `docs/TEST_PLAN.md` | 测试基线 |
| `docs/sprints/` | Sprint 正式文档（可选） |

---

##  backlog（不阻塞开发）

以下可在开发间隙**补内容**，不改变 v1.0 结构：

| 项 | 说明 |
|----|------|
| PROJECT 剩余 `[待补]` 章节 | §05、§07、§08、§15、§23 等 |
| DEC-011+ | Sprint 0.5.0-02～14 决策考古 |
| 可选 ADR 拆分 | 正文迁至 `docs/decisions/DEC-xxx.md`，DECISION_LOG 保留索引 |
| Commit Hook | 改 Bridge/Settings 时提示 Decision/PROJECT 是否需更新 |

---

## v1.0 交付清单

| 交付物 | 状态 |
|--------|------|
| `INDEX.md` | ✅ v1.0 |
| `PROJECT.md` | ✅ 核心章节 + 工作流（维护期增量补全） |
| `DECISION_LOG.md` | ✅ DEC-001～010（Accepted） |
| `AGENTS.md` | ✅ Mission / Workflow / Rules |
| `docs/` 证据层 | ✅ 既有，持续按 Release 追加 |
| 结构冻结 | ✅ 不再 Phase 1/2/3 式重构 |

---

## 快速引用

| 我想… | 读… |
|-------|-----|
| 了解整个知识体系 | 本文件 |
| 知道系统怎么设计 | `PROJECT.md` |
| 知道为什么这样设计 | `DECISION_LOG.md` |
| 让 AI 正确开发 | `AGENTS.md` |
| 查版本范围 | `docs/ROADMAP.md` |
| 查迁移是否完成 | `docs/migration/FEATURE_PARITY_MATRIX.md` |
| 查 Release 证据 | `docs/releases/` |

---

*Knowledge System v1.0 · Knowledge-Driven Development · 2026-07-18*
