# Self-Hosted Software Directory Standard

## Scope

This repository is the only formal Local Media Manager Git worktree. All current and future self-hosted software follows the same directory convention under `D:\自用软件`.

```text
D:\自用软件\源代码目录\<software>
D:\自用软件\部署安装目录\<software>
D:\自用软件\测试目录\<software>
D:\自用软件\临时目录\<software>
D:\自用软件\备份目录\<software>
```

For Local Media Manager, the five roots are:

```text
D:\自用软件\源代码目录\本地媒体管理器
D:\自用软件\部署安装目录\本地媒体管理器
D:\自用软件\测试目录\本地媒体管理器
D:\自用软件\临时目录\本地媒体管理器
D:\自用软件\备份目录\本地媒体管理器
```

## Rules

- Source code, Git, builds, and debugging only occur in the source root.
- The deployment root contains only the runnable application and formal user data; it is never a Git worktree or a development location.
- Smoke data, isolated databases, media samples, logs, and validation artifacts belong in the test root.
- Disposable exports, generated files, SQL, logs, scripts, and intermediate files belong in the temporary root.
- New release backups use versioned names such as `v0.7.9`. The deployment script retains the newest three versioned backups only.
- Existing migration-era backup directories are not managed by automatic retention. They require an inventory and separate deletion approval.
- Do not create `copy`, `clean`, `backup`, or `new` source replicas, and do not create formal project roots directly under `D:\`.

## Migration Safety

Existing projects are migrated only after directory analysis, an approved move/copy/delete plan, and a rollback plan. Do not delete an old source, deployment, data, test, or backup directory until migration validation is reported and deletion is explicitly approved.
