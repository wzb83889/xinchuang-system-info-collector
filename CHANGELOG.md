# Changelog

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
