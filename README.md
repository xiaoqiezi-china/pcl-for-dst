# DST Plain Craft Launcher（DPCL）
本仓库是 [Plain Craft Launcher (PCL)](https://github.com/Meloong-Git/PCL) 的**第三方修改版**，
基于 PCL `2.99.0.0` 源码继续开发，软件名 **DST Plain Craft Launcher**，简称 **DPCL**。

> 这是第三方作品，**不是** PCL 官方版本，也不代表 PCL 作者的任何立场或背书。

## 许可

本仓库继续沿用原项目的 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）。
使用、修改、分发前请先阅读该文件。
本软件遵守此协议，以开源全部源码，并保留了原作者署名及其赞助通道。

## 我们做了什么

### 管理
DPCL为PCL拓展了非常丰富强大的管理页，以及针对模组等第三方资源的收藏夹。

#### 存档管理
除了常见的删减存档，DPCL使用户对存档拥有更强的修改与预览能力。右键存档管理页中的具体存档即可进入存档的详细管理页。
在这里，你可以查看所有进入过此存档的玩家信息并编辑他们的信息。在存档外修改血量，经验等基础数据，以及直接操作物品栏与末影箱。
还可以通过内置的轻量引擎直接读取MCA世界文件并预渲染世界地图

#### 光影管理
除了基础功能外，允许直接修改光影设置项，并提供常见设置的中文对照

#### 资源包与数据包管理
提供了基础的管理功能

### 联机
是的，我们重新启用了PCL的联机功能。

原版联机依赖官方下发的配置与节点列表，那套服务已经下线，DPCL 把配置改成编译期内置，
节点清单改成「GitHub 同步 → 本地备份 → 内置兜底」三层降级。房主实际使用的节点会编码进邀请码，
成员加入时自动按它连接，不再需要双方手动填写同一个节点。

需要注意的几点：

- 联机依赖 EasyTier，创建虚拟网卡、写防火墙白名单都需要管理员权限，请**右键以管理员身份运行**。
  没有提权会在进入联机页时收到提示，创建/加入时则会直接失败并说明原因
- 房主用的节点如果不在公开清单里，成员加入时会弹窗确认是否信任该节点，由使用者自己决定
- 默认的公益节点覆盖地区有限，不一定能提供满意的网络效果，可以换成自建节点（见「自定义节点」）

### PCL Indra
在保留了PCL Classic美术风格的同时，提供了一套自由切换的PCL Indra布局，采用了经典的侧边栏布局并以深色为主题。当然，你也可以为PCL经典皮肤切换深色背景

### Music Time
百宝箱新增了基于网易云的音乐功能，这也许可以为性能较弱的设备避免开启另一个音乐软件

### 云功能
由于 DPCL 不发布也不盈利，仅作学习使用，**发布版**不内置 clientID 与 CurseForge Key；
需要内置凭据的定制分发见下面「凭据」一节。同时也为了满足开发时考虑的服务器定制启动器需要，DPCL内置了一套与其同生态的DST-QQbot的交互功能。当然，基于源码设计一套服务器也是可以的。并且由于DST-QQbot尚在开发中，并未开源，当前阶段这也是唯一的办法。您可以联系开发者直接接入此Bot.
云功能默认不连接。在设置-机器人中选择一个预设（或填写服务器地址与令牌）即可开启：
选定后会立刻连接一次，并把服务端下发的 ClientId 与 CurseForge Key 写进本地配置。

「设置-个性化 → 常规模式」只影响首页是否展示云端内容，**不会**阻止连接与凭据同步，
机器人设置页照常显示服务状态。

云功能为DPCL提供了收藏夹云同步，查看QQ群消息，查看QQ群公告，查看当前QQ群绑定的服务器信息等功能。

### 其他
如果您需要登入正版，以及查询 CurseForge 资源，可以在「设置 → 其他 → 账号与 API」里自行填写；
也可以在「设置 → 机器人」里选一个预设，选中后会立即同步一次，把服务端下发的 ClientId 与
CurseForge Key 写进本地配置，之后就能正常登入正版。

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
| `build-all.sh` | 预设版与发布版的全部组合，版本号固定为 1.0.0 / 0.9.9，只用于更新链路测试 |

`build-preset.sh` 与 `build-release.sh` 的版本号从 `Modules/Base/ModBase.vb` 读取，
不再写死，发版时只需改源码里的版本号。

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

文件存在时工程会自动定义 `DST_LOCAL`，凭据随编译期注入；`build-preset.sh` 产出的
`fortrop/` 版就是这样来的。

预设版除了上面两项，还需要在同一个文件里补上服务地址、令牌、预设 id 与群号
（常量名见 `Modules/ModSecret.vb` 里对 `ModSecretLocal` 的引用）。都填了之后，
启动器才会在「设置 → 机器人」里多出「本地预设」这一项，不必再联网拉预设列表。

反之 `build-release.sh` 会把该文件移走再编译，产物不含任何内置凭据。

## 截图收藏策略

设置 → 其他 → 截图收藏，三选一：

| 策略 | 行为 |
| --- | --- |
| 只记录路径 | 不复制，原图删了收藏就失效 |
| **复制到启动器目录**（默认） | 复制到 `PCL/ScreenshotFavorites/` |
| 复制到系统图片目录 | 复制到 `图片/DPCL/` |

取消收藏时会一并删掉当初复制出去的副本，避免残留。

## 远程数据源

| 文件 | 用途 |
| --- | --- |
| `yushe.json` | 云预设（服务地址、群号、展示名） |
| `link-nodes.json` | 联机节点清单 |

国内直连 `raw.githubusercontent.com` 基本拿不到，所以两者都按镜像优先的顺序尝试，
任一源成功即止：

| 文件 | 尝试顺序 |
| --- | --- |
| `yushe.json` | `gh-proxy.com` → `ghfast.top` → 官方 |
| `link-nodes.json` | `ghfast.top` → `gh-proxy.com` → `cdn.jsdelivr.net` → 官方 |

流程都是「拉取 → 备份到本地 → 失败时读备份」，备份保留最近 5 份；
节点清单再多一层内置兜底，确保一个源都拿不到时仍能联机。
手动点「刷新」会带时间戳参数绕开 CDN 缓存。

> 镜像由第三方转发，理论上可以替换内容。预设文件里带着服务地址与令牌，
> 节点清单决定联机走哪台机器，所以镜像只是「能用」的折中，不是可信来源。
> 有条件的话可以自建一份放到自己的服务器上。

## 自动更新

启动器自身的更新只从 **GitHub Release** 取，不经过预设服务器：

- **不做缓存**：每次检查都实时请求。缓存过就会「发了新版还提示已是最新」，这个坑踩过
- 版本信息首选 Release 列表接口，失败时退回 `releases/latest` 的 302 跳转
  （这条不吃匿名 API 配额，是最稳的兜底）
- 版本接口同样加加速：官方 `api.github.com` 拿不到就走 `gh-proxy.com` 转发。
  `ghfast.top` 对 API 地址返回 403，只能用于下载，两者不能混用
- 更新包取该 Release 里第一个 `.exe` 附件，下载按「官方 → `ghfast.top` → `gh-proxy.com`」依次尝试
- 下载走 PCL 原生 `LoaderDownload`，进度显示在任务管理器
- 下载后按文件头确认拿到的是启动器，镜像返回的错误页会被丢弃；
  Release 提供 SHA-256 摘要时会再校验一次内容

> 预设服务器只负责服务器列表与整合包下载，**不参与**启动器自身的更新决策。
> 那是使用者自选、可随时更换的地址，让它决定「装哪个启动器」风险过大。

发版流程：改 `ModBase.vb` 里的版本号 → 打 tag 并推送，CI 会构建并挂到该 tag 的 Release 上。
Release 附件名固定为 `DST Plain Craft Launcher.exe`。

```bash
git tag v1.0.4 && git push origin v1.0.4
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
