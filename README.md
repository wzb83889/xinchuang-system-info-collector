# 企业 IT 信创系统信息采集工具

用于企业设备资产盘点、硬件信息复核和信创环境初筛。V1.3.0 同时提供：

- `SystemInfoCollector-Windows.exe`：Windows 7/8/10/11，基于 .NET Framework 4.8 与 WMI。
- `systeminfo-linux.sh`：面向统信 UOS、银河麒麟、中标麒麟、openEuler、openKylin 等 Linux 发行版，不依赖 WMI、不要求 root。

> 信创判断是可解释的关键词线索评分，不是产品认证或合规结论。报告会保留命中项，并明确要求结合采购台账、产品认证和设备铭牌人工复核。

## V1.3.0 重点变化

- 新增 Linux 原生采集入口，读取标准 `/etc/os-release`、`/proc`、`/sys`，并按可用性调用 `lsblk`、`ip`、`lspci`、`aplay`、`lpstat`。
- 离线样例覆盖 UOS+兆芯、银河麒麟+飞腾、openEuler+鲲鹏、openKylin+龙芯。
- 扩充国产操作系统、整机品牌、CPU 和型号规则；去掉“普通联想/小米品牌即计入信创分值”的宽泛判断，降低误报。
- 将“置信度”改为“线索分值”，并在结果中强制显示人工复核提示。
- 移除源码中的内网 WebDAV 地址和默认账号，改用命令行或环境变量配置。

## Linux 使用

```sh
chmod +x systeminfo-linux.sh
./systeminfo-linux.sh
```

默认在当前目录生成 UTF-8 的 TXT、JSON、CSV 三种报告。

```sh
# 指定输出目录
./systeminfo-linux.sh --output "$HOME/设备报告"

# 只在终端显示，不写文件
./systeminfo-linux.sh --print

# 运行内置规则测试
./systeminfo-linux.sh --self-test
```

脚本不安装软件、不提升权限。部分字段依赖系统是否允许普通用户读取 DMI/sysfs；可选命令缺失时会降级为“系统未提供”，不会导致整份报告失败。

### Linux 采集覆盖

| 模块 | 首选来源 | 降级行为 |
| --- | --- | --- |
| 发行版 | `/etc/os-release` | 显示 Linux |
| 整机、序列号、主板、BIOS | `/sys/class/dmi/id`、device-tree | 字段标记未提供 |
| CPU、内存 | `/proc/cpuinfo`、`/proc/meminfo`、`lscpu` | 字段标记未提供 |
| 磁盘 | `lsblk` | 提示安装 `util-linux` |
| 网络 | `ip`、`/sys/class/net` | 至少保留接口与 MAC（若可读） |
| 显卡、音频 | `lspci`、`aplay` | 字段标记未提供 |
| 电池 | `/sys/class/power_supply` | 标记未检测到 |
| 打印机 | CUPS `lpstat` | 标记 CUPS/命令不可用 |

目前规则兼容名称包括：统信 UOS、银河麒麟、中标麒麟/NeoKylin、openKylin、麒麟信安/KylinSec、openEuler/EulerOS、Loongnix、Anolis OS、Asianux、中科方德、红旗 Linux、Deepin。未在真实硬件上验证的发行版属于“设计兼容”，发布前测试状态见下文。

## Windows 使用

直接运行 Release 中的 `SystemInfoCollector-Windows.exe`，按菜单选择基础检测、完整检测、信创判断、单模块检测或报告上传。

```powershell
# 规则自检
.\SystemInfoCollector-Windows.exe --self-test-xc

# 明确指定 WebDAV，不在源码保存地址、账号或密码
.\SystemInfoCollector-Windows.exe --collect "https://server/DeviceReports" user password

# 或仅通过环境变量提供非敏感配置；密码仍在运行时输入
$env:SYSTEMINFO_COLLECT_URL = "https://server/DeviceReports"
$env:SYSTEMINFO_COLLECT_USER = "user"
.\SystemInfoCollector-Windows.exe --collect
```

成功使用过的地址仍会保存在 `%LOCALAPPDATA%\SystemInfoCollector\last_collect_address.txt`；账号和密码不会持久化。

## 构建与测试

Windows 构建：

```powershell
dotnet msbuild .\SystemInfoCollector.Windows.csproj /t:Rebuild /p:Configuration=Release /v:minimal
.\bin\Release\SystemInfoCollector-Windows.exe --self-test-xc
```

Linux/WSL/Git Bash 测试：

```sh
sh -n ./systeminfo-linux.sh
sh ./tests/run-linux-tests.sh
```

## V1.3.0 验证状态

- Windows Release 构建：通过。
- Windows 信创规则 5 组自检：通过。
- Linux Shell 语法检查：通过。
- UOS、银河麒麟、openEuler、openKylin 离线样例采集与规则测试：通过。
- 真实信创设备现场测试：未执行（当前没有真实测试环境），仍是正式验收前的唯一硬件环境验证项。

官方产品信息表明银河麒麟桌面/服务器产品覆盖多种处理器架构，openKylin 也提供 ARM 与 LoongArch 镜像，因此 Linux 入口采用架构无关的 Shell 与内核接口，而不发布单一架构的二进制包：

- [银河麒麟产品线](https://www.kylinos.cn/productPc/)
- [银河麒麟桌面操作系统 V11 架构说明](https://www.kylinos.cn/productPc/desktop/desktopMainV11/)
- [openKylin 2.0 SP1 ARM/LoongArch 信息](https://www.openkylin.top/news/3607-en.html)

## 安全与隐私

- 采集在本地进行；Linux 版 V1.3.0 不上传报告。
- Windows WebDAV 上传只有在用户主动进入归集功能或使用 `--collect` 时才执行。
- 发布仓库不包含内网地址、默认账号、密码、真实设备报告或真实序列号。
- 公开反馈问题时请先删除报告中的序列号、UUID、MAC、IP、用户名和位置等敏感字段。

## 许可

当前仓库未授予开源再许可，默认保留全部权利。如需对外开源，请由项目所有者另行选择并加入许可证文件。
