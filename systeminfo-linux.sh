#!/bin/sh
set -u

APP_NAME="企业IT信创系统信息采集工具 Linux 版"
VERSION="1.3.0"
OUTPUT_DIR="."
PRINT_ONLY=0
SYSTEM_ROOT=${SYSTEMINFO_ROOT:-/}

usage() {
    cat <<'EOF'
用法: ./systeminfo-linux.sh [--output 目录] [--print] [--self-test] [--help]

  --output 目录   保存 TXT/JSON/CSV 报告，默认当前目录
  --print         只输出到终端，不写文件
  --self-test     运行信创规则内置样例测试

脚本仅在本机读取 /etc、/proc、/sys 及常见只读系统命令，不需要 root。
EOF
}

root_path() {
    if [ "$SYSTEM_ROOT" = "/" ]; then
        printf '%s' "$1"
    else
        printf '%s%s' "${SYSTEM_ROOT%/}" "$1"
    fi
}

read_one_line() {
    file=$(root_path "$1")
    if [ -r "$file" ]; then
        IFS= read -r value < "$file" || true
        printf '%s' "${value:-}"
    fi
}

os_release_value() {
    file=$(root_path /etc/os-release)
    key=$1
    if [ -r "$file" ]; then
        awk -F= -v wanted="$key" '
            $1 == wanted {
                value=substr($0, index($0, "=") + 1)
                gsub(/^"|"$/, "", value)
                gsub(/^\047|\047$/, "", value)
                print value
                exit
            }
        ' "$file"
    fi
}

have() { command -v "$1" >/dev/null 2>&1; }

safe_command() {
    if [ "$SYSTEM_ROOT" = "/" ]; then
        "$@" 2>/dev/null || true
    fi
}

first_nonempty() {
    for value in "$@"; do
        if [ -n "${value:-}" ]; then
            printf '%s' "$value"
            return
        fi
    done
}

json_escape() {
    printf '%s' "$1" | awk 'BEGIN { ORS="" } {
        gsub(/\\/, "\\\\"); gsub(/\"/, "\\\""); gsub(/\r/, "\\r"); gsub(/\t/, "\\t");
        if (NR > 1) printf "\\n"; printf "%s", $0
    }'
}

keyword_hit() {
    text=$1
    pattern=$2
    printf '%s\n' "$text" | grep -Eio "$pattern" | head -n 1 || true
}

assess_xinchuang() {
    ASSESS_BRAND=$1
    ASSESS_MODEL=$2
    ASSESS_CPU=$3
    ASSESS_OS=$4
    BRAND_HIT=$(keyword_hit "$ASSESS_BRAND" '华为|Huawei|中国长城|Great Wall|浪潮|Inspur|中科曙光|Sugon|紫光|UNIS|新华三|H3C|清华同方|Tongfang|方正|Founder|神州|Hasee|联想开天|Lenovo Kaitian|宝德|PowerLeader|同方超翔')
    CPU_HIT=$(keyword_hit "$ASSESS_CPU" '兆芯|Zhaoxin|KX-|KH-|海光|Hygon|C86|飞腾|Phytium|FT-2000|D2000|S2500|龙芯|Loongson|LoongArch|3A5000|3A6000|3C5000|鲲鹏|Kunpeng|HiSilicon|申威|Sunway|SW64|海思|Kirin|瑞芯微|Rockchip|RK3588')
    OS_HIT=$(keyword_hit "$ASSESS_OS" '统信|UnionTech|Uniontech OS|UOS|银河麒麟|中标麒麟|NeoKylin|KylinOS|Kylin Linux|openKylin|开放麒麟|麒麟信安|KylinSec|openEuler|欧拉|EulerOS|Loongnix|龙芯操作系统|Anolis OS|龙蜥|Asianux|中科方德|NFSChina|红旗 Linux|Red Flag Linux|Deepin|深度操作系统')
    MODEL_HIT=$(keyword_hit "$ASSESS_MODEL" '信创|国产化|Kunpeng|鲲鹏|Phytium|飞腾|Hygon|海光|Loongson|龙芯|LoongArch|Zhaoxin|兆芯|Sunway|申威|Kaitian|开天|擎云|超翔')
    SCORE=0
    [ -n "$BRAND_HIT" ] && SCORE=$((SCORE + 30))
    [ -n "$CPU_HIT" ] && SCORE=$((SCORE + 35))
    [ -n "$OS_HIT" ] && SCORE=$((SCORE + 20))
    [ -n "$MODEL_HIT" ] && SCORE=$((SCORE + 15))
    if [ "$SCORE" -ge 75 ]; then
        ASSESS_RESULT="高度疑似信创设备（需人工复核）"
    elif [ "$SCORE" -ge 45 ]; then
        ASSESS_RESULT="疑似信创设备（需人工复核）"
    else
        ASSESS_RESULT="信创线索不足（不等于非信创）"
    fi
}

self_test() {
    failed=0
    run_case() {
        name=$1; brand=$2; model=$3; cpu=$4; os=$5; minimum=$6
        assess_xinchuang "$brand" "$model" "$cpu" "$os"
        if { [ "$minimum" -eq 0 ] && [ "$SCORE" -lt 45 ]; } || { [ "$minimum" -gt 0 ] && [ "$SCORE" -ge "$minimum" ]; }; then
            printf '[PASS] %s: %s/100\n' "$name" "$SCORE"
        else
            printf '[FAIL] %s: %s/100\n' "$name" "$SCORE"
            failed=1
        fi
    }
    run_case "银河麒麟飞腾整机" "中国长城" "飞腾 D2000" "Phytium D2000" "银河麒麟桌面操作系统 V10" 75
    run_case "统信兆芯终端" "清华同方" "超翔 Z800" "Zhaoxin KX-U6780A" "UnionTech OS Desktop 20" 75
    run_case "openEuler鲲鹏服务器" "Huawei" "Kunpeng Server" "Kunpeng 920" "openEuler 24.03" 75
    run_case "openKylin龙芯设备" "中科曙光" "LoongArch Workstation" "Loongson-3A6000" "openKylin 2.0" 75
    run_case "普通Linux电脑" "Lenovo" "ThinkPad T14" "Intel Core Ultra 7" "Ubuntu 24.04" 0
    [ "$failed" -eq 0 ]
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --output)
            [ "$#" -ge 2 ] || { echo "--output 缺少目录" >&2; exit 2; }
            OUTPUT_DIR=$2; shift 2 ;;
        --print) PRINT_ONLY=1; shift ;;
        --self-test) self_test; exit $? ;;
        --help|-h) usage; exit 0 ;;
        *) echo "未知参数: $1" >&2; usage >&2; exit 2 ;;
    esac
done

OS_NAME=$(first_nonempty "$(os_release_value PRETTY_NAME)" "$(os_release_value NAME)" "Linux")
OS_ID=$(os_release_value ID)
OS_ID_LIKE=$(os_release_value ID_LIKE)
OS_VERSION=$(first_nonempty "$(os_release_value VERSION_ID)" "$(os_release_value VERSION)")
KERNEL=$(safe_command uname -sr)
ARCH=$(safe_command uname -m)
[ -n "$ARCH" ] || ARCH=$(read_one_line /proc/sys/kernel/arch)
HOSTNAME_VALUE=$(first_nonempty "$(read_one_line /etc/hostname)" "$(safe_command hostname)")

MANUFACTURER=$(first_nonempty "$(read_one_line /sys/class/dmi/id/sys_vendor)" "$(read_one_line /proc/device-tree/model)")
MODEL=$(first_nonempty "$(read_one_line /sys/class/dmi/id/product_name)" "$(read_one_line /sys/firmware/devicetree/base/model)")
SERIAL=$(first_nonempty "$(read_one_line /sys/class/dmi/id/product_serial)" "$(read_one_line /sys/firmware/devicetree/base/serial-number)")
UUID=$(read_one_line /sys/class/dmi/id/product_uuid)
BOARD=$(first_nonempty "$(read_one_line /sys/class/dmi/id/board_vendor) $(read_one_line /sys/class/dmi/id/board_name)" "$(read_one_line /sys/class/dmi/id/board_name)")
BIOS=$(first_nonempty "$(read_one_line /sys/class/dmi/id/bios_vendor) $(read_one_line /sys/class/dmi/id/bios_version)" "$(read_one_line /sys/class/dmi/id/bios_version)")
BIOS_DATE=$(read_one_line /sys/class/dmi/id/bios_date)

CPU_INFO_FILE=$(root_path /proc/cpuinfo)
CPU_NAME=""
if [ -r "$CPU_INFO_FILE" ]; then
    CPU_NAME=$(awk -F: '/^(model name|Hardware|Processor|cpu model)[[:space:]]*:/ { sub(/^[[:space:]]+/, "", $2); print $2; exit }' "$CPU_INFO_FILE")
fi
if [ -z "$CPU_NAME" ] && have lscpu; then
    CPU_NAME=$(safe_command lscpu | awk -F: '/Model name|型号名称|Model:/ { sub(/^[[:space:]]+/, "", $2); print $2; exit }')
fi
CPU_LOGICAL=$(safe_command getconf _NPROCESSORS_ONLN)
if [ -z "$CPU_LOGICAL" ] && [ -r "$CPU_INFO_FILE" ]; then
    CPU_LOGICAL=$(awk '/^processor[[:space:]]*:/ {n++} END {print n+0}' "$CPU_INFO_FILE")
fi

MEMINFO=$(root_path /proc/meminfo)
MEMORY_TOTAL=""
if [ -r "$MEMINFO" ]; then
    MEMORY_TOTAL=$(awk '/^MemTotal:/ {printf "%.2f GiB", $2/1024/1024}' "$MEMINFO")
fi

DISKS=""
if have lsblk; then
    DISKS=$(safe_command lsblk -dn -o NAME,MODEL,SIZE,TYPE,TRAN,SERIAL | awk '$4=="disk" {print}' | sed '/^[[:space:]]*$/d')
fi
[ -n "$DISKS" ] || DISKS="系统未提供（可安装 util-linux 后重试）"

NETWORK=""
if have ip; then
    NETWORK=$(safe_command ip -o addr show up | awk '$3=="inet" || $3=="inet6" {print $2 " " $3 " " $4}' | sed '/ lo /d')
fi
if [ -z "$NETWORK" ]; then
    NET_ROOT=$(root_path /sys/class/net)
    if [ -d "$NET_ROOT" ]; then
        for iface_path in "$NET_ROOT"/*; do
            [ -e "$iface_path" ] || continue
            iface=$(basename "$iface_path")
            [ "$iface" = "lo" ] && continue
            mac=""; [ -r "$iface_path/address" ] && IFS= read -r mac < "$iface_path/address"
            NETWORK="${NETWORK}${iface} MAC:${mac}\n"
        done
    fi
fi
[ -n "$NETWORK" ] || NETWORK="系统未提供"

GPU=""
if have lspci; then GPU=$(safe_command lspci | grep -Ei 'VGA|3D|Display' || true); fi
[ -n "$GPU" ] || GPU="系统未提供（可安装 pciutils 后重试）"

AUDIO=""
if have aplay; then AUDIO=$(safe_command aplay -l); fi
if [ -z "$AUDIO" ] && have lspci; then AUDIO=$(safe_command lspci | grep -Ei 'audio|multimedia' || true); fi
[ -n "$AUDIO" ] || AUDIO="系统未提供"

BATTERY=""
POWER_ROOT=$(root_path /sys/class/power_supply)
if [ -d "$POWER_ROOT" ]; then
    for battery_path in "$POWER_ROOT"/BAT*; do
        [ -d "$battery_path" ] || continue
        capacity=""; status=""
        [ -r "$battery_path/capacity" ] && IFS= read -r capacity < "$battery_path/capacity"
        [ -r "$battery_path/status" ] && IFS= read -r status < "$battery_path/status"
        BATTERY="${BATTERY}$(basename "$battery_path") ${capacity}% ${status}\n"
    done
fi
[ -n "$BATTERY" ] || BATTERY="未检测到电池或系统未提供"

PRINTERS=""
if have lpstat; then PRINTERS=$(safe_command lpstat -p); fi
[ -n "$PRINTERS" ] || PRINTERS="未检测到 CUPS 打印机或 lpstat 不可用"

ASSESS_TEXT="$OS_NAME $OS_ID $OS_ID_LIKE"
assess_xinchuang "$MANUFACTURER" "$MODEL" "$CPU_NAME" "$ASSESS_TEXT"
GENERATED_AT=$(date '+%Y-%m-%d %H:%M:%S %z')

REPORT=$(cat <<EOF
$APP_NAME V$VERSION
生成时间: $GENERATED_AT

========== 操作系统 ==========
主机名: ${HOSTNAME_VALUE:-系统未提供}
发行版: $OS_NAME
发行版 ID: ${OS_ID:-系统未提供}
兼容族: ${OS_ID_LIKE:-系统未提供}
版本: ${OS_VERSION:-系统未提供}
内核: ${KERNEL:-系统未提供}
架构: ${ARCH:-系统未提供}

========== 整机与主板 ==========
制造商: ${MANUFACTURER:-系统未提供}
型号: ${MODEL:-系统未提供}
序列号: ${SERIAL:-系统未提供}
UUID: ${UUID:-系统未提供}
主板: ${BOARD:-系统未提供}
BIOS/固件: ${BIOS:-系统未提供}
BIOS 日期: ${BIOS_DATE:-系统未提供}

========== 处理器与内存 ==========
CPU: ${CPU_NAME:-系统未提供}
逻辑处理器: ${CPU_LOGICAL:-系统未提供}
总内存: ${MEMORY_TOTAL:-系统未提供}

========== 磁盘 ==========
$DISKS

========== 网络 ==========
$(printf '%b' "$NETWORK")

========== 显卡 ==========
$GPU

========== 音频 ==========
$AUDIO

========== 电池 ==========
$(printf '%b' "$BATTERY")

========== 打印机 ==========
$PRINTERS

========== 信创线索判断 ==========
判断结果: $ASSESS_RESULT
线索分值: $SCORE/100
品牌命中: ${BRAND_HIT:-未命中}
型号命中: ${MODEL_HIT:-未命中}
CPU 命中: ${CPU_HIT:-未命中}
系统命中: ${OS_HIT:-未命中}
说明: 本结果仅用于设备初筛，不代表认证或合规结论；请结合采购台账、产品认证和设备铭牌人工复核。
EOF
)

printf '%s\n' "$REPORT"

if [ "$PRINT_ONLY" -eq 0 ]; then
    mkdir -p "$OUTPUT_DIR" || { echo "无法创建输出目录: $OUTPUT_DIR" >&2; exit 1; }
    timestamp=$(date '+%Y%m%d_%H%M%S')
    safe_host=$(printf '%s' "${HOSTNAME_VALUE:-unknown}" | tr -c 'A-Za-z0-9._-' '_')
    base="$OUTPUT_DIR/系统信息检测报告_${safe_host}_${timestamp}"
    printf '%s\n' "$REPORT" > "${base}.txt"
    cat > "${base}.json" <<EOF
{"app":"$(json_escape "$APP_NAME")","version":"$VERSION","generatedAt":"$(json_escape "$GENERATED_AT")","hostname":"$(json_escape "${HOSTNAME_VALUE:-}")","os":{"name":"$(json_escape "$OS_NAME")","id":"$(json_escape "$OS_ID")","idLike":"$(json_escape "$OS_ID_LIKE")","version":"$(json_escape "$OS_VERSION")","kernel":"$(json_escape "$KERNEL")","architecture":"$(json_escape "$ARCH")"},"device":{"manufacturer":"$(json_escape "$MANUFACTURER")","model":"$(json_escape "$MODEL")","serial":"$(json_escape "$SERIAL")","uuid":"$(json_escape "$UUID")","board":"$(json_escape "$BOARD")","bios":"$(json_escape "$BIOS")","biosDate":"$(json_escape "$BIOS_DATE")"},"cpu":"$(json_escape "$CPU_NAME")","logicalProcessors":"$(json_escape "$CPU_LOGICAL")","memoryTotal":"$(json_escape "$MEMORY_TOTAL")","xinchuang":{"result":"$(json_escape "$ASSESS_RESULT")","score":$SCORE,"brandHit":"$(json_escape "$BRAND_HIT")","modelHit":"$(json_escape "$MODEL_HIT")","cpuHit":"$(json_escape "$CPU_HIT")","osHit":"$(json_escape "$OS_HIT")","requiresManualReview":true}}
EOF
    printf '\357\273\277"主机名","操作系统","架构","制造商","型号","序列号","CPU","内存","信创判断","线索分值"\n' > "${base}.csv"
    printf '"%s","%s","%s","%s","%s","%s","%s","%s","%s","%s/100"\n' \
        "$HOSTNAME_VALUE" "$OS_NAME" "$ARCH" "$MANUFACTURER" "$MODEL" "$SERIAL" "$CPU_NAME" "$MEMORY_TOTAL" "$ASSESS_RESULT" "$SCORE" >> "${base}.csv"
    printf '\n报告已保存:\n  %s.txt\n  %s.json\n  %s.csv\n' "$base" "$base" "$base"
fi
