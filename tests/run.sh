#!/usr/bin/env bash
# 跑 NBT / 路径工具的回归测试。
#
# 这些模块不依赖 WPF，所以直接用 vbc 编成控制台程序即可，
# 不需要引入测试框架，也不需要启动启动器。
#
# 用法（在仓库根目录）：bash tests/run.sh
set -u
cd "$(dirname "$0")/.." || exit 1

# vbc 随 MSBuild 一起安装；CI 里由 setup-msbuild 提供。
VBC="${VBC:-C:/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe}"
SRC="Plain Craft Launcher 2"
OUT="tests/bin"

if [ ! -f "$VBC" ]; then
    VBC="$(command -v vbc.exe || command -v vbc || true)"
fi
if [ -z "$VBC" ] || [ ! -f "$VBC" ]; then
    echo "找不到 vbc.exe。请安装 Visual Studio 生成工具，或用 VBC 环境变量指定路径。" >&2
    exit 1
fi

mkdir -p "$OUT"

# MSYS 会把 /r:System.dll 这类参数当成路径改写掉，必须关掉自动转换
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

"$VBC" /nologo /target:exe /out:"$OUT/NbtTests.exe" \
    /r:System.dll /r:System.Core.dll \
    /imports:System,System.IO,System.IO.Compression,System.Text,System.Collections.Generic \
    "$SRC/Modules/DstNbt.vb" \
    "$SRC/Modules/DstNbtPath.vb" \
    tests/NbtTests.vb 2>&1 | grep -E "error|warning"
RC=${PIPESTATUS[0]}
if [ "$RC" != "0" ]; then
    echo "测试代码编译失败" >&2
    exit 1
fi

"$OUT/NbtTests.exe"
