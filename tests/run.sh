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

# MSYS 会把 /r:System.dll 这类参数当成路径改写掉，必须关掉自动转换。
# 放在最前面：下面的 vbc 探测也要用到正确的路径语义。
export MSYS_NO_PATHCONV=1
export MSYS2_ARG_CONV_EXCL='*'

# 把 Windows 风格路径（C:\a\b）转成 bash 能处理的 /c/a/b。
to_posix() {
    local P="$1"
    case "$P" in
        [A-Za-z]:\\*) P="/$(echo "${P:0:1}" | tr 'A-Z' 'a-z')/${P:3}" ;;
    esac
    echo "$P" | tr '\\' '/'
}

# 找 vbc.exe。
#
# 不同机器的安装位置差别很大：本地可能在 BuildTools 下，CI 的 windows-latest
# 则是 VS 的安装目录，而且 edition（Enterprise/Professional/BuildTools）与
# 年份都不固定。原来只写死一个路径，CI 上直接报“找不到 vbc.exe”。
# 这里按“显式指定 → PATH → MSBuild 同级 → vswhere → 遍历安装目录”的顺序找。
find_vbc() {
    if [ -n "${VBC:-}" ] && [ -f "${VBC}" ]; then
        echo "${VBC}"
        return 0
    fi

    # 1) 直接在 PATH 里（部分环境会把 Roslyn 加进 PATH）
    local FromPath
    FromPath="$(command -v vbc.exe 2>/dev/null || command -v vbc 2>/dev/null || true)"
    if [ -n "$FromPath" ] && [ -f "$FromPath" ]; then
        echo "$FromPath"
        return 0
    fi

    # 2) 从 PATH 里的 MSBuild 反推：vbc 就在 MSBuild.exe 同级的 Roslyn 目录下。
    #    CI 的 setup-msbuild 会把 MSBuild 加进 PATH，这条最可靠。
    local MsBuild FromMsBuild
    MsBuild="$(command -v msbuild.exe 2>/dev/null || command -v MSBuild.exe 2>/dev/null || command -v msbuild 2>/dev/null || true)"
    if [ -n "$MsBuild" ]; then
        MsBuild="$(to_posix "$MsBuild")"
        FromMsBuild="$(dirname "$MsBuild")/Roslyn/vbc.exe"
        if [ -f "$FromMsBuild" ]; then
            echo "$FromMsBuild"
            return 0
        fi
    fi

    # 3) 用 vswhere 问 VS 装在哪（vswhere 是 VS Installer 自带的，位置固定）
    local VsWhere Found
    for VsWhere in \
        "/c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe" \
        "C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe"; do
        if [ -f "$VsWhere" ]; then
            Found="$("$VsWhere" -latest -products '*' -find 'MSBuild\\**\\Bin\\Roslyn\\vbc.exe' 2>/dev/null | head -1 | tr -d '\r')"
            if [ -n "$Found" ] && [ -f "$Found" ]; then
                echo "$Found"
                return 0
            fi
            # 退一步：拿安装根目录再拼路径
            local Root
            Root="$("$VsWhere" -latest -products '*' -property installationPath 2>/dev/null | head -1 | tr -d '\r')"
            if [ -n "$Root" ]; then
                Found="$Root/MSBuild/Current/Bin/Roslyn/vbc.exe"
                if [ -f "$Found" ]; then
                    echo "$Found"
                    return 0
                fi
            fi
        fi
    done

    # 4) 兜底：遍历 VS 安装根目录，年份与 edition 都不写死
    local Base Sub
    for Base in "/c/Program Files/Microsoft Visual Studio" "/c/Program Files (x86)/Microsoft Visual Studio"; do
        [ -d "$Base" ] || continue
        while IFS= read -r Sub; do
            if [ -n "$Sub" ] && [ -f "$Sub" ]; then
                echo "$Sub"
                return 0
            fi
        done < <(find "$Base" -maxdepth 5 -name vbc.exe -path "*Roslyn*" 2>/dev/null | head -3)
    done

    return 1
}

VBC="$(find_vbc || true)"
if [ -z "$VBC" ]; then
    echo "找不到 vbc.exe。请安装 Visual Studio 生成工具，或用 VBC 环境变量指定路径。" >&2
    echo "调试信息：" >&2
    echo "  PATH 里的 msbuild：$(command -v msbuild.exe 2>/dev/null || echo '(无)')" >&2
    echo "  VS 安装目录：" >&2
    ls -d "/c/Program Files/Microsoft Visual Studio"/*/* 2>/dev/null | head -5 >&2
    exit 1
fi
echo "使用 vbc：$VBC"

mkdir -p "$OUT"

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