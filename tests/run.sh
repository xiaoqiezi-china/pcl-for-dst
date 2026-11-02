#!/usr/bin/env bash
# 跑纯逻辑模块的回归测试。
#
# 这些模块不依赖 WPF，所以直接用 vbc 编成控制台程序即可，
# 不需要引入测试框架，也不需要启动启动器。
#
# 用法（在仓库根目录）：bash tests/run.sh
set -u
cd "$(dirname "$0")/.." || exit 1

SRC="Plain Craft Launcher 2"
OUT="tests/bin"

# 找 vbc.exe。
#
# 不同机器的安装位置差别很大：本地可能在 BuildTools 下，CI 的 windows-latest
# 则是 VS 的 MSBuild 目录。原来只写死一个路径，CI 上直接报“找不到 vbc.exe”。
# 这里按“显式指定 → PATH → 常见安装位置”的顺序找。
find_vbc() {
    if [ -n "${VBC:-}" ] && [ -f "${VBC}" ]; then
        echo "${VBC}"
        return 0
    fi
    local FromPath
    FromPath="$(command -v vbc.exe 2>/dev/null || command -v vbc 2>/dev/null || true)"
    if [ -n "$FromPath" ] && [ -f "$FromPath" ]; then
        echo "$FromPath"
        return 0
    fi
    local Candidate
    for Candidate in \
        "C:/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "/c/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files/Microsoft Visual Studio/2019/Enterprise/MSBuild/Current/Bin/Roslyn/vbc.exe" \
        "C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/MSBuild/Current/Bin/Roslyn/vbc.exe"; do
        if [ -f "$Candidate" ]; then
            echo "$Candidate"
            return 0
        fi
    done
    return 1
}

VBC="$(find_vbc || true)"
if [ -z "$VBC" ]; then
    echo "找不到 vbc.exe。请安装 Visual Studio 生成工具，或用 VBC 环境变量指定路径。" >&2
    exit 1
fi
echo "使用 vbc：$VBC"

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

run_tests LinkDiagnosticsTests \
    "$SRC/Modules/Config/ModLinkDiagnostics.vb" \
    tests/LinkDiagnosticsTests.vb || FAILED=1

exit $FAILED
