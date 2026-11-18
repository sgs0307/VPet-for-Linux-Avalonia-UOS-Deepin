# 发布到 GitHub

本项目在 GitHub 上的发布分三层：**源码入库** → **推送 tag** → **发 Release（带安装包）**。
下面按顺序来，全部是一次性配置，之后每个版本只有第 4 节要重复。

---

## 0. 当前仓库状态

本地 `VPet-Avalonia-UOS/` 已经准备好了：

| 项 | 状态 |
|---|---|
| `git init` | ✅ 已完成，主分支 `main` |
| 首次提交 | ✅ 123 个文件，`.git` 仅 1.9 MB（`publish/`、`dist/`、`bin/`、`obj/` 已被 `.gitignore` 排除） |
| 版本 tag | ✅ `v1.3.1`（annotated tag） |
| 提交作者 | ⚠️ 占位 `VPet UOS Port <vpet-uos@users.noreply.github.com>`，建议换成你自己的 |
| 远端 | ❌ 还没配 |

改提交作者（在推送之前做，只影响这一条提交）：

```bash
cd VPet-Avalonia-UOS
git config user.name  "你的名字"
git config user.email "你的GitHub邮箱"
git commit --amend --reset-author --no-edit
git tag -f -a v1.3.1 -m "v1.3.1 — 工作/学习面板 + 进度条配色跟随 MOD"
```

> `git config user.email` 要和你 GitHub 账号的邮箱一致（或用它提供的 `xxx@users.noreply.github.com`），
> 否则提交在 GitHub 上不会关联到你的头像和主页。

---

## 1. 在网页上创建仓库

1. 打开 <https://github.com/new>
2. **Repository name**：`VPet-Linux-Avalonia`（或你喜欢的名字）
3. **Description**：`VPet 虚拟桌宠的 Linux(Avalonia) 移植版 — 统信 UOS / Deepin`
4. 选 **Public**
5. ⚠️ **不要勾选** `Add a README file`、`Add .gitignore`、`Choose a license` —— 本地已经有了，勾了会产生冲突
6. 点 **Create repository**

创建后页面会显示一段「…or push an existing repository from the command line」，复制其中两行备用。

---

## 2. 配置认证（选一种）

### 方式 A：Personal Access Token（推荐，最简单）

1. 打开 <https://github.com/settings/tokens> → **Generate new token (classic)**
2. Note 随便写（如 `vpet-push`），Expiration 选 90 天
3. 勾选 **`repo`** 权限（够用）
4. 生成后**立刻复制** token（只显示一次，形如 `ghp_xxxxxxxx`）

推送时把 token 当密码用：

```bash
git remote add origin https://github.com/<你的用户名>/VPet-Linux-Avalonia.git
git push -u origin main
# Username: <你的用户名>
# Password: <粘贴 token，不是账号密码>
```

想免输入（token 会明文存在 `~/.git-credentials`，注意这台机器是不是共享的）：

```bash
git config --global credential.helper store
```

### 方式 B：SSH key

```bash
ssh-keygen -t ed25519 -C "你的邮箱"      # 一路回车
cat ~/.ssh/id_ed25519.pub               # 复制输出的整行
```

打开 <https://github.com/settings/keys> → **New SSH key** → 粘贴 → 保存。

```bash
git remote add origin git@github.com:<你的用户名>/VPet-Linux-Avalonia.git
git push -u origin main
```

---

## 3. 推送源码与 tag

```bash
cd VPet-Avalonia-UOS

# 源码（1.9 MB，很快）
git push -u origin main

# 版本 tag
git push origin v1.3.1
```

推送后仓库首页应该有 README 渲染、三张截图、`CHANGELOG.md`。

> **国内网络提示**：`github.com` 直连经常超时，但 `api.github.com` 通常可用。
> 如果 `git push` 卡住或报 `Failed to connect to github.com port 443`，给 git 挂代理：
> ```bash
> git config --global http.proxy  http://127.0.0.1:7890
> git config --global https.proxy http://127.0.0.1:7890
> ```
> （端口换成你自己代理软件的；本机环境里 http 代理是 `127.0.0.1:39241`）

---

## 4. 发 Release（带安装包）

### 4.1 先打安装包

```bash
# arm64（本机架构，已在真机验证）
./scripts/package-release.sh --src dist/_arm64 --arch arm64 --mod-from publish/mod

# x64（统信 UOS 主流是 amd64，交叉编译产物）
./scripts/package-release.sh --src dist/_x64   --arch x64   --mod-from publish/mod
```

产物在 `dist/`：

| 文件 | 大小 |
|---|---|
| `VPet-UOS-v1.3.1-linux-x64-full.tar.gz` | 990 MB（含 6349 张宠物帧图） |
| `VPet-UOS-v1.3.1-linux-x64-nomod.tar.gz` | 9.3 MB |
| `VPet-UOS-v1.3.1-linux-arm64-full.tar.gz` | 990 MB |
| `VPet-UOS-v1.3.1-linux-arm64-nomod.tar.gz` | 8.7 MB |
| `VPet-UOS-v1.3.1-linux-{x64,arm64}-sha256.txt` | 校验和 |

> 交叉编译 x64 需要联网下载 runtime pack。本机 NuGet 默认只走离线缓存，
> 编译时要加 `--source https://api.nuget.org/v3/index.json`：
> ```bash
> DOTNET_ROOT=$HOME/dotnet dotnet publish src/VPet-Avalonia.App -c Release -r linux-x64 \
>   --self-contained false -o dist/_x64 -m:1 -p:CopyModResources=false \
>   --source https://api.nuget.org/v3/index.json
> ```

### 4.2 网页上传

1. 仓库页 → 右侧 **Releases** → **Draft a new release**
2. **Choose a tag** → 选 `v1.3.1`（已推送过，会出现在列表里）
3. **Release title**：`v1.3.1 — 工作/学习面板 + 进度条配色跟随 MOD`
4. **Describe this release**：把 `docs/RELEASE_NOTES-v1.3.1.md` 的内容整段粘进去
   （GitHub 支持 Markdown，表格和代码块都会正常渲染）
5. **Attach binaries**：把 `dist/` 里 4 个 `.tar.gz` + 2 个 `sha256.txt` 拖进虚线框
6. 点 **Publish release**

> **990 MB 的包上传会很慢**（国内上行 10 Mbps 大概要 15 分钟，中途断了要整个重传）。
> 建议策略：
> 1. 先只传两个 `-nomod`（合计 18 MB）+ `sha256.txt`，发布一个「精简版」Release；
> 2. 页面右上 **Edit** 再进编辑，慢慢补两个 full 包（编辑时上传是增量的，已上传的附件不会重传）；
> 3. 或者拆成「程序包 + 资源包」两个 Release 附件分开发。
>
> 如果本机有可用代理，用 `gh` CLI 断点续传更省心（见 4.3）。

### 4.3 用 gh CLI 发（可选）

```bash
# 安装：https://github.com/cli/cli#installation
gh auth login                       # 选 GitHub.com → HTTPS → 浏览器登录

gh release create v1.3.1 \
  --title "v1.3.1 — 工作/学习面板 + 进度条配色跟随 MOD" \
  --notes-file docs/RELEASE_NOTES-v1.3.1.md \
  dist/VPet-UOS-v1.3.1-linux-x64-full.tar.gz \
  dist/VPet-UOS-v1.3.1-linux-arm64-full.tar.gz \
  dist/VPet-UOS-v1.3.1-linux-x64-nomod.tar.gz \
  dist/VPet-UOS-v1.3.1-linux-arm64-nomod.tar.gz \
  dist/VPet-UOS-v1.3.1-linux-x64-sha256.txt \
  dist/VPet-UOS-v1.3.1-linux-arm64-sha256.txt
```

`gh` 走 HTTP 上传，断了重跑会跳过已完成的资产。

---

## 5. 以后每个版本怎么发

```bash
# 1. 改版本号（VERSION + CHANGELOG + README 徽章）
echo 1.3.2 > VERSION

# 2. 构建 + 打包
./scripts/build.sh
./scripts/package-release.sh --src publish --arch arm64

# 3. 提交 + 打 tag + 推送
git add -A
git commit -m "v1.3.2: <一句话>"
git tag -a v1.3.2 -m "v1.3.2"
git push origin main && git push origin v1.3.2

# 4. 到网页发 Release（同 4.2）
```

---

## 6. 几个坑

| 现象 | 原因 / 处理 |
|---|---|
| 推上去发现 `publish/` 也在仓库里 | `.gitignore` 已排除；如果之前误 add 过，`git rm -r --cached publish` 后再提交 |
| 提交里附件超过 100 MB | GitHub 硬限制单文件 100 MB（Release 附件是 2 GB）。本项目已经用 `.gitignore` + Release 分离，源码仓库只有 1.9 MB |
| README 里的截图不显示 | 图片路径是相对路径，注意大小写和中文文件名编码；`assets/screenshots/` 下的三张图必须一起提交 |
| 提交在 GitHub 上不显示作者头像 | `user.email` 与账号邮箱不一致，按第 0 节改 |
| 上传大附件报 `Payload Too Large` | 单附件不能超 2 GB；本项目 full 包 990 MB 在限内 |
| Release 页面中文乱码 | 用 UTF-8 保存 `RELEASE_NOTES-*.md`（本仓库文件已是 UTF-8） |

---

## 7. 顺便建议（可选）

- 仓库 **About** 区填 Description 和 Topics：`uos` `deepin` `linux` `desktop-pet` `avalonia` `dotnet`，能被人搜到
- **Pin** 一下仓库，访客第一眼看得到
- 加一个 `.github/ISSUE_TEMPLATE/bug_report.md`，收集「发行版 + 架构 + `uname -m` + 日志」三样信息，
  否则 UOS 各版本差异很难排查
- 上游是 Apache-2.0，**保留 `LICENSE` 和 README 里的上游链接**；宠物美术资源不要放进 git，
  Release 里随包分发属于合理使用，但最好在 README 里像现在这样写清「版权归原作者」
