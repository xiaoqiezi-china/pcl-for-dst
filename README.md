# DST Plain Craft Launcher（DPCL）
当前Readme存在部分失真落后，等待后续重写。
本仓库是 [Plain Craft Launcher (PCL)](https://github.com/Meloong-Git/PCL) 的**第三方修改版**，
基于 PCL `2.99.0.0` 源码继续开发，产品名 **DST Plain Craft Launcher**，简称 **DPCL**。

> 这是第三方作品，**不是** PCL 官方版本，也不代表 PCL 作者的任何立场或背书。

## 许可

本仓库继续沿用原项目的 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）。
使用、修改、分发前请先阅读该文件。其中对「重度使用」的要求包括：

- 明确标注这是第三方二次创作（本文件即为此目的）
- 名称以 Plain Craft Launcher (PCL) 开头，并带第三方修改的后缀
- 在关于页首位署名原作者并提供赞助链接
- 公开源代码

## 与原版的差异

### 界面

- 深色 / 浅色两套配色，主题全部开放
- 标题栏「默认」模式显示产品名 DPCL
- 首页服务器卡片支持右键隐藏延迟，省得挡住信息
- 「你知道吗」改为随机提示语池

### 版本管理

- 存档管理：level.dat 编辑、NBT 编辑、地图渲染、玩家数据
- 资源包与光影包管理
- **截图管理**：浏览当前实例的 `screenshots` 目录，支持缩略图、全屏预览、用画图编辑、批量删除
- 光影：解析 OptiFine / Iris 的 `shaders.properties` 与 `lang`，可就地编辑并写回
  `shaderpacks/<包名>.txt`，内置常见选项的中文对照

### 收藏夹

- 新增**游戏截图**分类，与 Mod / 整合包 / 资源包等并列
- 收藏截图时按策略处理文件，见「截图收藏策略」一节

### 联机

- 联机配置**编译期内置**，不依赖远端下发
- EasyTier 内核从官方 GitHub Release 下载（v2.6.4），走原生下载器，带断点续传与镜像回退
- 节点清单采用 **GitHub 同步 + 本地备份 + 内置兜底** 三层降级，见 `link-nodes.json`
- 启动前清理上次异常退出残留的 TUN 网卡，避免「对象已存在 (5010)」
- 连接前并发探测各节点 TCP 延迟，优先选最快的
- 邀请码会带上房主实际使用的节点地址（编码后写入），未知节点由用户确认后连接
- 联机需要管理员权限：EasyTier 要创建虚拟网卡并把自己加入防火墙白名单。
  权限不足时启动器会直接提示，右键「以管理员身份运行」即可

### 账号与 API

- 微软 ClientId、CurseForge Key 可在设置中填写，留空则回落到 `config.ini`，再回落到编译期内置值
- 遥测：本版本**不上传任何数据**

## 构建

需要 Visual Studio 生成工具（MSBuild）与 .NET Framework 4.8 开发包。

```bash
./build.sh
```

产物为 `dist/DST Plain Craft Launcher.exe`（单文件，依赖已嵌入）。

> 注意：`Resources.resx` 硬编码引用了 `PCLCS\bin` 下的 DLL，因此**不要覆盖 `OutDir`**，
> 否则 PCLCS 的产物不会落在它期望的位置，编译会失败。脚本已按默认输出处理。

### 其他构建脚本

| 脚本 | 产物 |
| --- | --- |
| `build.sh` | `dist/`，常规构建 |
| `build-preset.sh` | `fortrop/`，内置凭据的版本 |
| `build-release.sh` | `release/`，不含任何凭据的版本 |
| `build-all.sh` | 预设版与发布版的全部组合 |

## 凭据

仓库不含任何密钥。凭据按以下优先级解析，先命中先用：

1. 「设置 → 其他 → 账号与 API」里手填的值
2. 运行目录下的 `PCL/config.ini`
3. 编译期内置值

`config.ini` 只在首次启动且文件不存在时释放一份模板，之后**更新启动器不会覆盖它**，
因此升级后凭据仍在。发行版可以预先塞一份带初始数据的 `config.ini` 一起分发。

若要内置凭据，自建 `Modules/ModSecret.Local.vb`（已在 `.gitignore` 中，不会被提交）：

```vb
Friend Module ModSecretLocal
    Public Const ClientId As String = "你的 Azure 应用 ID"
    Public Const CurseForgeKey As String = "你的 CurseForge Key"
End Module
```

文件存在时工程会自动定义 `DST_LOCAL`，凭据随编译期注入。

## 截图收藏策略

设置 → 其他 → 截图收藏，三选一：

| 策略 | 行为 |
| --- | --- |
| 只记录路径 | 不复制，原图删了收藏就失效 |
| **复制到启动器目录**（默认） | 复制到 `PCL/ScreenshotFavorites/` |
| 复制到系统图片目录 | 复制到 `图片/DPCL/` |

取消收藏时会一并删掉当初复制出去的副本，避免残留。

## 远程数据源

| 文件 | 用途 | 地址 |
| --- | --- | --- |
| `yushe.json` | 云预设（服务地址、群号、展示名），默认空列表，自行部署服务后填入 | `raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/yushe.json` |
| `link-nodes.json` | 联机节点清单 | `raw.githubusercontent.com/xiaoqiezi-china/pcl-for-dst/refs/heads/main/link-nodes.json` |

两者都是「拉取 → 备份到本地 → 失败时读备份」的流程，备份保留最近 5 份。
节点清单再多一层内置兜底，确保 GitHub 不可达时仍能联机。

## 自动更新

启动器自身的更新只从 **GitHub Release** 取，不经过预设服务器：

- 版本号读 `releases/latest` 的 302 跳转（不吃匿名 API 配额），更新说明取 Release 正文
- 更新包取该 Release 里第一个 `.exe` 附件
- 下载按「官方地址 → `ghfast.top` → `gh-proxy.com`」依次尝试，任一成功即止
- 下载走 PCL 原生 `LoaderDownload`，进度显示在任务管理器
- 下载后按文件头校验，拿到的必须真的是启动器，镜像返回错误页会被丢弃

> 预设服务器只负责服务器列表与整合包下载，**不参与**启动器自身的更新决策。
> 那是使用者自选、可随时更换的地址，让它决定「装哪个启动器」风险过大。

发版流程：打 tag 并推送，CI 会构建并挂到该 tag 的 Release 上。

```bash
git tag v1.0.0 && git push origin v1.0.0
```

## 已知限制

部分整合包（例如 GTNH）依赖专属的主类与 JVM 参数，PCL 暂不支持这类组件。
安装时启动器会提示并引导改用 [Prism Launcher](https://prismlauncher.org/)。

联机节点清单目前只有两个公开公益节点（一个国内、一个海外），覆盖地区有限。
若要发挥延迟优选的作用，需要往 `link-nodes.json` 里补充更多地区的节点。

## 署名

- **龙腾猫跃** — Plain Craft Launcher 的开发者 — [赞助](https://meloong.com/afd/a/LTCat)
- **HerobrineXia** — Plain Craft Launcher 的协力开发者

EasyTier 联机内核来自 [EasyTier/EasyTier](https://github.com/EasyTier/EasyTier)（Apache-2.0）。
