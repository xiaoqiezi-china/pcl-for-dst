# Plain Craft Launcher（DST 修改版）

本仓库是 [Plain Craft Launcher (PCL)](https://github.com/Meloong-Git/PCL) 的**第三方修改版**，
基于 PCL `2.99.0.0` 的源码继续开发。

> 这是第三方作品，**不是** PCL 官方版本，也不代表 PCL 作者的任何立场或背书。

## 许可

本仓库继续沿用原项目的 [LICENCE](LICENCE)（《PCL 分发有限许可》与《PCL 存储库合理使用指南》）。
使用、修改、分发前请先阅读该文件。其中对"重度使用"的要求包括：

- 明确标注这是第三方二次创作（本文件即为此目的）
- 名称以 Plain Craft Launcher (PCL) 开头，并带第三方修改的后缀
- 在关于页首位署名原作者并提供赞助链接
- 公开源代码

## 与原版的差异

| 模块 | 内容 |
| --- | --- |
| 界面 | 深色 / 浅色两套配色，主题全部开放；QQ 形态登录页 |
| 版本管理 | 存档管理（level.dat 编辑、NBT 编辑、地图渲染、玩家数据）、资源包与光影包管理 |
| 光影 | 解析 OptiFine / Iris 的 `shaders.properties` 与 `lang`，可就地编辑并写回 `shaderpacks/<包名>.txt`，内置常见选项的中文对照 |
| 账号与 API | 微软 ClientId、CurseForge Key 可在设置中填写，留空则回落到编译期内置值 |
| 遥测 | 本版本**不上传任何数据** |

## 构建

需要 Visual Studio 生成工具（MSBuild）与 .NET Framework 4.8 开发包。

```bash
./build.sh
```

产物为 `dist/DST Craft Launcher.exe`（单文件，依赖已内嵌）。

> 注意：`Resources.resx` 硬编码引用了 `PCLCS\bin` 下的 DLL，因此**不要覆盖 `OutDir`**，
> 否则 PCLCS 的产物不会落在它期望的位置，编译会失败。`build.sh` 已按默认输出处理。

## 凭据

仓库不含任何密钥。需要正版登录或 CurseForge 搜索时：

- 方式一：在「设置 → 其他 → 账号与 API」中填写
- 方式二：自建 `Modules/ModSecret.Local.vb`（已在 `.gitignore` 中，不会被提交）

```vb
Friend Module ModSecretLocal
    Public Const ClientId As String = "你的 Azure 应用 ID"
    Public Const CurseForgeKey As String = "你的 CurseForge Key"
    Public Const PresetApi As String = ""
    Public Const PresetToken As String = ""
End Module
```

文件存在时工程会自动定义 `DST_LOCAL` 编译符号。

## 已知限制

部分整合包（例如 GTNH）依赖专属的主类与 JVM 参数，PCL 暂不支持这类组件。
安装时启动器会提示并引导改用 [Prism Launcher](https://prismlauncher.org/)。

详见 [docs/脱敏标准.md](docs/脱敏标准.md)，其中也包含发布前的检查清单。

## 署名

- **龙腾猫跃** — Plain Craft Launcher 的开发者 — [赞助](https://meloong.com/afd/a/LTCat)
- **HerobrineXia** — Plain Craft Launcher 的协力开发者
