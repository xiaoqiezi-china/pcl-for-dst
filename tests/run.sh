#!/usr/bin/env bash
# 跑纯逻辑模块的回归测试。
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

# 每个测试程序单独编译：它们的入口各自是一个 Module 的 Main，
# 混在一个 exe 里会撞入口点。加一个就跑一组，互不影响。
run_tests() {
    local Name="$1"
    shift
    if ! "$VBC" /nologo /target:exe /out:"$OUT/$Name.exe" \
        /r:System.dll /r:System.Core.dll \
        /imports:System,System.IO,System.IO.Compression,System.Text,System.Text.RegularExpressions,System.Collections.Generic \
        "$@" 2>&1 | grep -E "error|warning"; then
        :
    fi
    if [ "${PIPESTATUS[0]}" != "0" ]; then
        echo "测试代码编译失败：$Name" >&2
        exit 1
    fi
    "$OUT/$Name.exe" || return 1
}

FAILED=0
run_tests NbtTests \
    "$SRC/Modules/DstNbt.vb" \
    "$SRC/Modules/DstNbtPath.vb" \
    tests/NbtTests.vb || FAILED=1

run_tests InviteCodeTests \
    "$SRC/Modules/Config/ModInviteCode.vb" \
    tests/InviteCodeTests.vb || FAILED=1

exit $FAILED
