# Changelog

## V1.3.1 - 2026-11-10

### 新增

- Windows 简要、完整和批量采集共用物理网卡清单。可读取 MAC 的禁用或断开网卡继续归档，缺失时仅重试网络模块。
- 汇总 CSV 增加网卡数量、MAC 采集结论、各网卡及姓名校验列，支持旧报告按列名归并。
- 增加 Windows 采集回归套件，并接入 GitHub Actions。

### 修复

- 用 Unicode 控制台读取登记信息，拦截可识别的损坏姓名。
- WebDAV 区分连通与登录验证，已有归档目录通过 PROPFIND 核验后复用。
- 防护归档路径、重复文件名及 CSV 公式前缀。

公开版沿用运行时部署配置。参见 [开发调试复盘](docs/development-retrospective-fix2.md) 和 [发布前代码审查](docs/release-review-v1.3.1.md)。

## V1.3.0 - 2026-08-12

### Added

- Linux 原生设备信息采集脚本，适配常见信创 Linux 发行版与多 CPU 架构。
- UOS、银河麒麟、openEuler、openKylin 离线发行版样例测试。
- Windows 与 Linux 信创规则自检。
- GitHub Actions 双平台构建与测试。

### Changed

- 扩展国产操作系统、整机、CPU、型号关键词覆盖。
- 信创结果改为“线索分值 + 人工复核”，避免表达为正式认证。
- 收紧整机品牌规则，减少普通消费品牌造成的误报。
- WebDAV 地址与账号改为运行时参数或环境变量配置。

### Security

- 移除硬编码内网地址和默认账号。
- 发布包不包含真实设备报告、PDB、旧版本二进制或内部演示材料。

### Validation limitation

- 因当前没有真实 UOS/银河麒麟设备，本版本只完成 Windows 构建、自检、Shell 语法检查和离线发行版样例验证；真实硬件现场验收待补。
