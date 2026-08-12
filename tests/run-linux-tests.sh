#!/bin/sh
set -eu
repo_dir=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$repo_dir"
sh ./systeminfo-linux.sh --self-test
output=$(SYSTEMINFO_ROOT="$repo_dir/tests/fixtures/kylin" sh ./systeminfo-linux.sh --print)
printf '%s\n' "$output" | grep -F "银河麒麟高级服务器操作系统 V10" >/dev/null
printf '%s\n' "$output" | grep -F "Phytium D2000/8 E8C" >/dev/null
printf '%s\n' "$output" | grep -F "高度疑似信创设备" >/dev/null
printf '%s\n' "$output" | grep -F "线索分值: 85/100" >/dev/null
check_fixture() {
    fixture=$1; expected_os=$2; expected_cpu=$3
    fixture_output=$(SYSTEMINFO_ROOT="$repo_dir/tests/fixtures/$fixture" sh ./systeminfo-linux.sh --print)
    printf '%s\n' "$fixture_output" | grep -F "$expected_os" >/dev/null
    printf '%s\n' "$fixture_output" | grep -F "$expected_cpu" >/dev/null
    printf '%s\n' "$fixture_output" | grep -F "高度疑似信创设备" >/dev/null
}
check_fixture uos "UnionTech OS Desktop 20 Professional" "Zhaoxin KX-U6780A"
check_fixture openeuler "openEuler 24.03 (LTS-SP1)" "Kunpeng 920"
check_fixture openkylin "openKylin 2.0 SP1" "Loongson-3A6000"
echo "Linux 发行版样例与采集测试通过。"
