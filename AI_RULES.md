# Local Media Manager Development Rules

> 本文件适用于所有 AI（Codex、ChatGPT、Claude、Gemini 等）以及任何参与本项目的开发者。
>
> 无论使用哪个 ChatGPT 账号、Codex 账号、API Provider 或模型，只要修改本项目，都必须遵守本文件。

本项目允许使用不同的 ChatGPT 账号、Codex 账号、API Provider（包括官方 API、第三方 API 或其他兼容接口）参与开发。

所有开发者和 AI 都必须遵循本仓库的文档规范、开发流程和版本管理规则。

不得因为模型、账号或 API 来源不同而改变项目规范、架构设计或开发标准。

---

## 一、最高原则

### 1. 不重新设计

本项目已经确定：

- 产品定位
- 技术架构
- UI 风格
- Design System
- 开发流程

未经用户确认，不得：

- 推翻已有设计
- 修改整体布局
- 恢复旧版 Jvedio UI
- 擅自新增交互
- 擅自删除已有功能

重大调整必须等待用户确认。

### 2. 一次只完成一个主题（One Theme Per Sprint）

一个 Sprint 只解决一个明确主题。

例如：

- 收藏系统
- 扫描导入
- 图片工作流
- NFO
- 文件整理

不要一次修改多个无关模块。一个主题可以包含完成其端到端工作流所必需的相关实现，但必须按依赖拆分为可独立验证和回滚的提交。

### 3. 功能优先（Feature First）

当前阶段：

`Feature Parity > UI > AI`

在 Feature Parity Matrix 未完成前：

- 不得为了视觉效果牺牲已有功能。
- 不得降低旧版能力。

### 4. 工作流优先（Workflow First）

任何功能必须按完整 Workflow 开发。

例如：

```text
扫描
↓
导入
↓
恢复评分
↓
恢复标签
↓
创建同步任务
↓
MetaTube
↓
图片
↓
NFO
↓
完成
```

不得只完成其中一个页面或按钮。

---

## 二、开发规范

修改任何功能前，必须阅读：

- `AI_RULES.md`
- `AGENTS.md`
- `PROJECT_CONTEXT.md`
- `docs/PRODUCT_VISION.md`
- `docs/ROADMAP.md`
- `docs/ARCHITECTURE.md`
- `docs/UI_DESIGN_SPEC.md`
- `docs/migration/FEATURE_PARITY_MATRIX.md`
- `docs/TEST_PLAN.md`

修改前必须搜索整个项目，检查：

- Bridge
- DTO
- Migration
- Database
- React
- Settings
- Tasks
- UI
- 自动化测试与安装版烟测

不得只修改一个文件或只修复截图中可见的页面。若确认某个主题只涉及一个文件，必须先完成全局检查并记录依据。

---

## 三、代码规范

优先复用已有组件。

避免：

- 重复代码
- 魔法数字
- 临时 Hack
- 无意义重构
- 为单一页面建立平行业务通道

复杂业务规则必须添加必要注释。注释应说明为什么这样设计，而不是重复代码本身。

---

## 四、UI 规范

所有页面必须遵守 `docs/UI_DESIGN_SPEC.md`。

统一：

- Material UI
- 间距
- 卡片
- 圆角
- 字体
- 配色
- 图标
- 动画
- 响应式
- 深色与浅色主题

不得出现多个设计风格，不得恢复 WPF/XAML 控件和页面结构。

---

## 五、数据安全

React 禁止直接访问 SQLite、文件系统或系统命令。

所有业务数据遵循：

```text
React
↓
Bridge DTO / Service
↓
SQLite
```

数据库结构升级必须经过 checksummed Migration。

任何数据库写入必须可验证、可恢复，并在高风险场景具备备份和回滚或补偿方案。

任何文件修改必须遵循：

```text
Preview
↓
Confirm
↓
Execute
↓
Audit / Recovery
```

禁止未经预览和确认直接修改用户文件。用户手工数据、锁定图片和用户维护的 NFO 始终优先。

---

## 六、Git 规范

每完成一个 Sprint，必须提供：

- Commit
- Release Tag
- Rollback Tag

不得累计大量无关修改后一次提交。一个 Sprint 内按主题创建意图明确、可独立验证和回滚的 Commit。

`main` 分支必须保持：

- 可编译
- 可运行
- 可发布

发布流程：

```text
Sprint Branch
↓
Self Test
↓
Smoke Test
↓
Freeze
↓
Merge
↓
Release
↓
Push
```

中间开发提交保留在本地 Sprint Branch。未通过完整发布门槛，不得推送 GitHub `main`，不得创建虚假 Release Tag 或验收记录。

---

## 七、测试规范

任何功能完成后，必须按适用范围执行：

```text
Debug Build
↓
Release Build
↓
Migration Test
↓
Automated Test
↓
Installed Smoke Test
↓
Test Plan
↓
Release
```

不得跳过必要测试，不得因为编译成功就认为功能完成，不得伪造或推测测试结果。

纯文档规划变更不要求无意义地构建产品，但必须完成文档一致性检查和 `git diff --check`。

---

## 八、禁止事项

禁止：

- 恢复旧 Jvedio UI
- 推翻现有架构
- 修改无关模块
- 删除已有能力
- 跳过 Bridge
- React 直接访问数据库或文件系统
- 未确认新增功能
- 为了编译通过删除代码或吞掉错误
- 伪造测试结果
- 将未验证功能标记为完成
- 将只读展示、模拟操作或单条协议预检标记为完整迁移
- 未经用户明确授权操作正式数据、发布或推送

---

## 九、沟通原则

如果需求存在会影响产品方向、数据安全、文件行为或交互方式的歧义：

1. 先提出方案。
2. 说明影响范围、风险和取舍。
3. 等待用户确认。

不得自行决定产品方向。完成后应按用户编号说明修改、验证和未完成内容。

---

## 十、项目目标

Local Media Manager 不是 Jvedio 的修改版。

目标是：

- 现代化
- 高性能
- 长期维护
- 可扩展
- 支持 AI
- 支持 NAS
- 支持插件
- 专业级本地媒体管理平台

所有开发必须围绕这一目标进行。

---

## 十一、Human Acceptance

默认情况下：

- AI 负责代码实现、编译、自动化测试、文档更新和本地 Commit。
- AI 不主动启动应用，不控制桌面，不执行人工 Smoke Test。
- UI 验收、功能体验和最终确认由开发者完成。

只有开发者明确要求时，AI 才执行应用启动或桌面自动化操作。
