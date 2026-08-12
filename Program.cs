using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace SystemInfoTest
{
    internal class Program
    {
        private const string AppName = "Windows 企业IT信创系统信息采集工具（V1.3.0）";
        private const string Version = "1.3.0";
        private const string CollectUrlEnvironmentVariable = "SYSTEMINFO_COLLECT_URL";
        private const string CollectUserEnvironmentVariable = "SYSTEMINFO_COLLECT_USER";
        private const int StdOutputHandle = -11;
        private const int EnableVirtualTerminalProcessing = 0x0004;
        private const string AnsiReset = "\x1b[0m";
        private const string AnsiDim = "\x1b[90m";
        private const string AnsiRed = "\x1b[91m";
        private const string AnsiGreen = "\x1b[92m";
        private const string AnsiYellow = "\x1b[93m";
        private const string AnsiBlue = "\x1b[94m";
        private const string AnsiMagenta = "\x1b[95m";
        private const string AnsiCyan = "\x1b[96m";
        private const string AnsiWhite = "\x1b[97m";
        private const string AnsiBold = "\x1b[1m";
        private const string AnsiHideCursor = "\x1b[?25l";
        private const string AnsiShowCursor = "\x1b[?25h";

        private static readonly List<ReportSection> LastReport = new List<ReportSection>();
        private static string _lastReportTitle = "尚未检测";
        private static bool _lastReportIncludeSource = false;
        private static string _currentCollectDepartment = "";
        private static string _currentCollectUserName = "";
        private static string _currentCollectLocation = "";
        private static int _connectivityProgressLength = 0;
        private static bool _supportsAnsi = false;
        private static bool _cursorHidden = false;
        private static int _homeClockLeft = -1;
        private static int _homeClockTop = -1;

        private static readonly string[] DepartmentOptions = new[]
        {
            "综合办",
            "财务部",
            "项管部",
            "商务部",
            "市场部",
            "安全部",
            "人资部",
            "科质环部",
            "数智部",
            "物资部",
            "领导班子"
        };

        private static readonly string[] XinChuangBrandKeywords = new[]
        {
            "华为", "Huawei", "长城", "Great Wall", "中国长城", "浪潮", "Inspur", "中科曙光", "Sugon",
            "紫光", "UNIS", "新华三", "H3C", "清华同方", "Tongfang", "方正", "Founder", "神州", "Hasee",
            "联想开天", "Lenovo Kaitian", "宝德", "PowerLeader", "超越", "百信", "黄河", "同方超翔"
        };

        private static readonly string[] XinChuangCpuKeywords = new[]
        {
            "兆芯", "Zhaoxin", "KX-", "KH-", "海光", "Hygon", "C86", "飞腾", "Phytium", "FT-2000", "D2000", "S2500",
            "龙芯", "Loongson", "LoongArch", "3A5000", "3A6000", "3C5000", "鲲鹏", "Kunpeng", "HiSilicon", "申威", "Sunway", "SW64",
            "海思", "Kirin", "麒麟芯片", "瑞芯微", "Rockchip", "RK3588"
        };

        private static readonly string[] XinChuangOsKeywords = new[]
        {
            "统信", "UnionTech", "Uniontech OS", "UOS", "银河麒麟", "中标麒麟", "NeoKylin", "KylinOS", "Kylin Linux",
            "openKylin", "开放麒麟", "麒麟信安", "KylinSec", "openEuler", "欧拉", "EulerOS", "Loongnix", "龙芯操作系统",
            "Anolis OS", "龙蜥", "Asianux", "中科方德", "NFSChina", "红旗 Linux", "Red Flag Linux", "Deepin", "深度操作系统"
        };

        private static readonly string[] XinChuangModelKeywords = new[]
        {
            "信创", "国产化", "Kunpeng", "鲲鹏", "Phytium", "飞腾", "Hygon", "海光", "Loongson", "龙芯", "LoongArch",
            "Zhaoxin", "兆芯", "Sunway", "申威", "Kaitian", "开天", "擎云", "超翔"
        };

        private static readonly List<DetectionModule> Modules = new List<DetectionModule>
        {
            new DetectionModule("1", "操作系统", "Windows 版本、架构、安装时间、启动时间", CollectOperatingSystem),
            new DetectionModule("2", "品牌型号", "制造商、型号、主机序列号、UUID、设备类型", CollectComputerSystem),
            new DetectionModule("3", "处理器", "CPU 名称、核心数、线程数、主频", CollectProcessor),
            new DetectionModule("4", "内存", "容量、插槽、DDR 代数、频率、部件号", CollectMemory),
            new DetectionModule("5", "硬盘驱动器", "物理硬盘驱动器和逻辑磁盘汇总", CollectDisk),
            new DetectionModule("6", "显卡", "显卡名称、显存、驱动版本", CollectVideo),
            new DetectionModule("7", "主板和 BIOS", "主板信息、BIOS 信息、多源序列号兜底", CollectBoardAndBios),
            new DetectionModule("8", "网络", "启用网卡、MAC、IP、网关", CollectNetwork),
            new DetectionModule("9", "音频设备", "声卡名称和制造商", CollectAudioDevices),
            new DetectionModule("10", "电池信息", "电池状态和预计剩余时间", CollectBatteryDevices),
            new DetectionModule("11", "显示器信息", "显示器名称和分辨率", CollectMonitorDevices),
            new DetectionModule("12", "外围设备", "已安装打印机、驱动、端口、IP、连接方式", CollectPeripheralDevices),
            new DetectionModule("13", "信创判定", "品牌、CPU、系统、型号的规则评分", CollectXinChuang)
        };

        public static string AppName1 => AppName;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

        private static void Main(string[] args)
        {
            try
            {
                RunApplication(args);
            }
            catch (Exception ex)
            {
                WriteFatalError(ex);
            }
        }

        private static void RunApplication(string[] args)
        {
            ConfigureConsole();
            if (TryHandleCommandLine(args))
            {
                return;
            }

            while (true)
            {
                ShowHome();
                string choice = ReadHomeChoiceWithLiveClock();
                if (choice == null)
                {
                    return;
                }

                choice = choice.Trim();
                if (choice == "0" || choice == "6")
                {
                    return;
                }

                if (choice == "1")
                {
                    RunBasicHardwareInfo();
                }
                else if (choice == "2")
                {
                    RunModules("硬件完整信息检测", Modules.Select(m => m.Key), false);
                }
                else if (choice == "3")
                {
                    RunXinChuangJudgement();
                }
                else if (choice == "4")
                {
                    ShowModuleMenu();
                }
                else if (choice == "5")
                {
                    ShowExportMenu();
                }
                else
                {
                    WriteWarning("无效选项，请重新输入。");
                    Pause();
                }
            }
        }

        private static void WriteFatalError(Exception ex)
        {
            try
            {
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("程序发生异常，已安全拦截。");
                Console.WriteLine("异常类型: {0}", ex == null ? "未知" : ex.GetType().FullName);
                Console.WriteLine("异常消息: {0}", ex == null ? "未知" : ex.Message);
                Console.WriteLine("请截图此信息反馈。按任意键退出...");
                ReadKeySafe();
            }
            catch
            {
            }
        }

        private static void ConfigureConsole()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.OutputEncoding = Encoding.UTF8;
                }

                if (!Console.IsInputRedirected)
                {
                    Console.InputEncoding = Encoding.UTF8;
                }

                _supportsAnsi = TryEnableAnsiOutput() || IsAnsiLikeTerminal();
                Console.Title = AppName1 + " " + Version;
            }
            catch
            {
            }
        }

        internal static bool SupportsAnsiOutput
        {
            get { return _supportsAnsi; }
        }

        internal static bool IsInteractiveTerminal
        {
            get { return !Console.IsOutputRedirected || IsAnsiLikeTerminal(); }
        }

        internal static string AnsiCode(ConsoleColor color)
        {
            switch (color)
            {
                case ConsoleColor.Red:
                case ConsoleColor.DarkRed:
                    return AnsiRed;
                case ConsoleColor.Green:
                case ConsoleColor.DarkGreen:
                    return AnsiGreen;
                case ConsoleColor.Yellow:
                case ConsoleColor.DarkYellow:
                    return AnsiYellow;
                case ConsoleColor.Blue:
                case ConsoleColor.DarkBlue:
                    return AnsiBlue;
                case ConsoleColor.Magenta:
                case ConsoleColor.DarkMagenta:
                    return AnsiMagenta;
                case ConsoleColor.Cyan:
                case ConsoleColor.DarkCyan:
                    return AnsiCyan;
                case ConsoleColor.DarkGray:
                case ConsoleColor.Gray:
                    return AnsiDim;
                case ConsoleColor.White:
                    return AnsiWhite;
                default:
                    return "";
            }
        }

        internal static string Colorize(string text, ConsoleColor color)
        {
            if (!_supportsAnsi)
            {
                return text ?? "";
            }

            return AnsiCode(color) + (text ?? "") + AnsiReset;
        }

        internal static void HideCursorForProgress()
        {
            if (!IsInteractiveTerminal)
            {
                return;
            }

            try
            {
                if (_supportsAnsi)
                {
                    Console.Write(AnsiHideCursor);
                }
                else
                {
                    Console.CursorVisible = false;
                }

                _cursorHidden = true;
            }
            catch
            {
            }
        }

        internal static void ShowCursorAfterProgress()
        {
            if (!_cursorHidden)
            {
                return;
            }

            try
            {
                if (_supportsAnsi)
                {
                    Console.Write(AnsiShowCursor);
                }
                else if (!Console.IsOutputRedirected)
                {
                    Console.CursorVisible = true;
                }
            }
            catch
            {
            }
            finally
            {
                _cursorHidden = false;
            }
        }

        private static bool TryEnableAnsiOutput()
        {
            try
            {
                IntPtr handle = GetStdHandle(StdOutputHandle);
                int mode;
                if (handle == IntPtr.Zero || !GetConsoleMode(handle, out mode))
                {
                    return false;
                }

                if ((mode & EnableVirtualTerminalProcessing) == EnableVirtualTerminalProcessing)
                {
                    return true;
                }

                return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAnsiLikeTerminal()
        {
            string term = Environment.GetEnvironmentVariable("TERM") ?? "";
            string msystem = Environment.GetEnvironmentVariable("MSYSTEM") ?? "";
            string wt = Environment.GetEnvironmentVariable("WT_SESSION") ?? "";
            string conEmu = Environment.GetEnvironmentVariable("ConEmuANSI") ?? "";

            if (!string.IsNullOrWhiteSpace(msystem) || !string.IsNullOrWhiteSpace(wt) || conEmu.Equals("ON", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (term.Equals("dumb", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ContainsAnyText(term, "xterm", "ansi", "cygwin", "mingw", "screen", "tmux", "vt100", "vt220");
        }

        private static bool TryHandleCommandLine(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }

            string command = args[0] == null ? "" : args[0].Trim();
            if (command.Equals("--collect", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("/collect", StringComparison.OrdinalIgnoreCase))
            {
                string targetDirectory = args.Length > 1 ? args[1] : GetPreferredCollectAddress();
                string userName = args.Length > 2 ? args[2] : GetConfiguredCollectUserName();
                string password = args.Length > 3 ? args[3] : "";
                RunBatchCollection(targetDirectory, !Console.IsInputRedirected && !Console.IsOutputRedirected, userName, password);
                return true;
            }

            if (command.Equals("--self-test-xc", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RunXinChuangRuleSelfTests() ? 0 : 1;
                return true;
            }

            if (command.Equals("--merge", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("/merge", StringComparison.OrdinalIgnoreCase))
            {
                string sourceDirectory = args.Length > 1 ? args[1] : "";
                if (string.IsNullOrWhiteSpace(sourceDirectory))
                {
                    WriteError("缺少归集目录参数。示例: SystemInfoCollector-Windows.exe --merge \"\\\\server\\设备信息采集\"");
                    return true;
                }

                MergeCollectedReports(sourceDirectory);
                return true;
            }

            if (command.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                command.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("批量采集: SystemInfoCollector-Windows.exe --collect");
                Console.WriteLine("指定 WebDAV: SystemInfoCollector-Windows.exe --collect \"https://server/DeviceReports\" user password");
                Console.WriteLine("汇总统计: SystemInfoCollector-Windows.exe --merge \"\\\\server\\设备信息采集\"");
                Console.WriteLine("信创规则自检: SystemInfoCollector-Windows.exe --self-test-xc");
                Console.WriteLine("也可设置 SYSTEMINFO_COLLECT_URL 与 SYSTEMINFO_COLLECT_USER 环境变量。用户名和密码不会写入源码。");
                return true;
            }

            return false;
        }

        private static void ShowHome()
        {
            ClearConsole();
            WriteHeader(AppName1);
            WriteFeatureIntro();
            WriteHomeStatus();
            Console.WriteLine();
            WriteMenuItem("1", "基础硬件信息检测", "计算机名、品牌、序列号、预计出厂日期、系统、CPU、网卡");
            WriteMenuItem("2", "硬件完整信息检测", "输出所有模块，按设备数量分组展示");
            WriteMenuItem("3", "信创产品判断", "展示品牌、型号、CPU、系统的综合判断过程");
            WriteMenuItem("4", "单模块信息检测", "按功能单独查看和调试");
            WriteMenuItem("5", "系统报告上传", "完整导出、批量采集上传和共享目录汇总");
            WriteMenuItem("6", "退出程序", "关闭程序");
            Console.WriteLine();
            WritePrompt("请输入选项: ");
        }

        private static void WriteHomeStatus()
        {
            BeginColor(ConsoleColor.DarkGray);
            Console.Write("  🖥 当前电脑: ");
            ResetTextColor();
            BeginColor(ConsoleColor.White);
            Console.WriteLine(Environment.MachineName);
            ResetTextColor();

            BeginColor(ConsoleColor.DarkGray);
            Console.Write("  ⏱ 检测时间: ");
            ResetTextColor();

            try
            {
                _homeClockLeft = Console.CursorLeft;
                _homeClockTop = Console.CursorTop;
            }
            catch
            {
                _homeClockLeft = -1;
                _homeClockTop = -1;
            }

            WriteHomeClockValue();
            Console.WriteLine();
        }

        private static void WriteHomeClockValue()
        {
            BeginColor(ConsoleColor.Green, true);
            Console.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            ResetTextColor();
        }

        private static string ReadHomeChoiceWithLiveClock()
        {
            if (!IsInteractiveTerminal || Console.IsInputRedirected)
            {
                return Console.ReadLine();
            }

            StringBuilder input = new StringBuilder();
            DateTime lastRefresh = DateTime.MinValue;
            while (true)
            {
                if ((DateTime.Now - lastRefresh).TotalMilliseconds >= 250)
                {
                    RefreshHomeClock();
                    lastRefresh = DateTime.Now;
                }

                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(60);
                    continue;
                }

                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return input.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (input.Length > 0)
                    {
                        input.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return "6";
                }

                if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }

        private static void RefreshHomeClock()
        {
            if (_homeClockLeft < 0 || _homeClockTop < 0)
            {
                return;
            }

            try
            {
                int left = Console.CursorLeft;
                int top = Console.CursorTop;
                Console.SetCursorPosition(_homeClockLeft, _homeClockTop);
                WriteHomeClockValue();
                Console.Write(" ");
                Console.SetCursorPosition(left, top);
            }
            catch
            {
            }
        }

        private static void ShowModuleMenu()
        {
            while (true)
            {
                ClearConsole();
                WriteHeader("单模块检测");
                foreach (DetectionModule module in Modules)
                {
                    WriteMenuItem(module.Key, module.Name, module.Description);
                }
                WriteMenuItem("0", "返回", "回到主菜单");
                Console.WriteLine();
                WritePrompt("请选择模块: ");

                string choice = Console.ReadLine();
                if (choice == null || choice.Trim() == "0")
                {
                    return;
                }

                DetectionModule selected = Modules.FirstOrDefault(m => m.Key == choice.Trim());
                if (selected == null)
                {
                    WriteWarning("无效模块编号。");
                    Pause();
                    continue;
                }

                RunModules(selected.Name, new[] { selected.Key });
            }
        }

        private static void ShowExportMenu()
        {
            ClearConsole();
            WriteHeader("系统报告上传");
            Console.WriteLine("最近报告: {0}", _lastReportTitle);
            Console.WriteLine("模块数量: {0}", LastReport.Count);
            Console.WriteLine();
            WriteMenuItem("1", "完整检测并导出", "先运行所有模块再保存报告");
            WriteMenuItem("2", "批量采集上传提交", "员工使用，完整检测后提交 txt/json/csv");
            WriteMenuItem("3", "汇总分析共享目录表", "信息化管理员专用，扫描归集目录生成统计表 CSV");
            WriteMenuItem("0", "返回", "回到主菜单");
            Console.WriteLine();
            WritePrompt("请输入选项: ");

            string choice = Console.ReadLine();
            if (choice == "1")
            {
                RunModules("硬件完整信息检测", Modules.Select(m => m.Key), false, true);
            }
            else if (choice == "2")
            {
                string targetDirectory = ResolveCollectDirectoryFromUser();
                RunBatchCollection(targetDirectory, true, GetConfiguredCollectUserName(), "");
                Pause();
            }
            else if (choice == "3")
            {
                Console.Write("请输入归集目录路径: ");
                string sourceDirectory = Console.ReadLine();
                MergeCollectedReports(sourceDirectory);
                Pause();
            }
        }

        private static void RunBatchCollection(string targetDirectory, bool interactive, string webDavUserName, string webDavPassword)
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                targetDirectory = GetPreferredCollectAddress();
            }

            try
            {
                string title = "批量设备信息归集";
                if (interactive)
                {
                    ClearConsole();
                    WriteHeader(title);
                }

                CollectTarget collectTarget = ResolveAccessibleCollectTarget(targetDirectory, interactive, webDavUserName, webDavPassword);
                if (collectTarget == null)
                {
                    return;
                }

                string department = interactive ? PromptDepartment() : "";
                string userName = interactive ? PromptRequiredText("请输入该计算机设备使用人员姓名: ") : "";
                string location = interactive ? PromptRequiredText("请输入当前设备使用位置（门牌号）: ") : "";
                _currentCollectDepartment = department;
                _currentCollectUserName = userName;
                _currentCollectLocation = location;

                List<ReportSection> sections = CollectModulesForExport(Modules.Select(m => m.Key), title, interactive);
                InsertCollectionRegistrationSection(sections, department, userName, location);
                LastReport.Clear();
                LastReport.AddRange(sections);
                _lastReportTitle = title + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _lastReportIncludeSource = false;

                string departmentDirectoryName = SanitizeFileName(SafeFilePart(department));
                string locationDirectoryName = SanitizeFileName(SafeFilePart(location));
                string fileIdentity = BuildCollectionFileIdentity(department, userName);
                ReportSaveResult result;
                if (collectTarget.IsWebDav)
                {
                    string remoteDirectory = CombineWebDavPath(departmentDirectoryName, locationDirectoryName);
                    result = SaveReportBundleToWebDav(sections, _lastReportTitle, false, collectTarget, remoteDirectory, fileIdentity);
                }
                else
                {
                    string locationDirectory = Path.Combine(collectTarget.Address, departmentDirectoryName, locationDirectoryName);
                    result = SaveReportBundle(sections, _lastReportTitle, false, locationDirectory, fileIdentity);
                }
                WriteSuccess("批量归集完成，共生成 {0} 个文件。", result.Paths.Count);
                foreach (string path in result.Paths)
                {
                    Console.WriteLine("  " + path);
                }
            }
            catch (Exception ex)
            {
                WriteError("批量归集失败: {0}", ex.Message);
            }
        }

        private static string ResolveCollectDirectoryFromUser()
        {
            string preferred = GetPreferredCollectAddress();
            string configured = GetConfiguredCollectAddress();
            if (!string.IsNullOrWhiteSpace(preferred) && !preferred.Equals(configured, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("上次成功 WebDAV 地址: {0}", preferred);
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    Console.WriteLine("环境变量配置的 WebDAV 地址: {0}", configured);
                }
            }
            else if (!string.IsNullOrWhiteSpace(preferred))
            {
                Console.WriteLine("默认 WebDAV 地址: {0}", preferred);
            }
            else
            {
                Console.WriteLine("尚未配置 WebDAV 地址。可输入地址，或设置 {0}。", CollectUrlEnvironmentVariable);
            }

            Console.Write("是否更换 WebDAV 地址? (Y/N，默认 N): ");
            string answer = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(answer) && answer.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("请输入新的 WebDAV 地址: ");
                string custom = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    return custom.Trim();
                }

                WriteWarning("未输入新地址。若没有已保存或环境变量配置的地址，本次归集将取消。");
            }

            return preferred;
        }

        private static CollectTarget ResolveAccessibleCollectTarget(string targetDirectory, bool interactive, string webDavUserName, string webDavPassword)
        {
            targetDirectory = string.IsNullOrWhiteSpace(targetDirectory) ? GetPreferredCollectAddress() : targetDirectory.Trim();
            CollectTarget target = BuildCollectTarget(targetDirectory, webDavUserName, webDavPassword, interactive);
            while (true)
            {
                Exception testError;
                if (target == null)
                {
                    return null;
                }

                if (TestCollectTarget(target, interactive, out testError))
                {
                    SaveSuccessfulCollectAddress(target.Address);
                    return target;
                }

                string message = GetCollectShareAccessFailureMessage(testError);
                WriteError("批量归集失败: {0}请核实更换 WebDAV 地址、账号密码，或联系信息化管理员。", message);

                if (!interactive)
                {
                    return null;
                }

                Console.Write("请输入新的 WebDAV 地址（直接回车取消批量归集）: ");
                string custom = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(custom))
                {
                    WriteWarning("已取消批量归集。");
                    return null;
                }

                target = BuildCollectTarget(custom.Trim(), GetConfiguredCollectUserName(), "", interactive);
            }
        }

        private static string GetPreferredCollectAddress()
        {
            string remembered = LoadSuccessfulCollectAddress();
            return string.IsNullOrWhiteSpace(remembered) ? GetConfiguredCollectAddress() : remembered;
        }

        private static string GetConfiguredCollectAddress()
        {
            return (Environment.GetEnvironmentVariable(CollectUrlEnvironmentVariable) ?? "").Trim();
        }

        private static string GetConfiguredCollectUserName()
        {
            return (Environment.GetEnvironmentVariable(CollectUserEnvironmentVariable) ?? "").Trim();
        }

        private static string LoadSuccessfulCollectAddress()
        {
            try
            {
                string path = GetCollectConfigPath();
                if (File.Exists(path))
                {
                    string value = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
            catch
            {
            }

            return "";
        }

        private static void SaveSuccessfulCollectAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            try
            {
                string path = GetCollectConfigPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, address.Trim(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static string GetCollectConfigPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = AppDomain.CurrentDomain.BaseDirectory;
            }

            return Path.Combine(root, "SystemInfoCollector", "last_collect_address.txt");
        }

        private static CollectTarget BuildCollectTarget(string address, string userName, string password, bool interactive)
        {
            address = string.IsNullOrWhiteSpace(address) ? GetConfiguredCollectAddress() : address.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                WriteError("批量归集失败: 地址不能为空。请通过 --collect 参数或 {0} 环境变量提供。", CollectUrlEnvironmentVariable);
                return null;
            }
            CollectTarget target = new CollectTarget();
            target.Address = address.TrimEnd('\\', '/');
            target.IsWebDav = IsWebDavAddress(target.Address);
            if (!target.IsWebDav)
            {
                return target;
            }

            target.UserName = string.IsNullOrWhiteSpace(userName) ? GetConfiguredCollectUserName() : userName.Trim();
            target.Password = password ?? "";
            if (interactive)
            {
                Console.WriteLine("当前 WebDAV 账号: {0}", string.IsNullOrWhiteSpace(target.UserName) ? "未配置" : target.UserName);
                Console.Write("是否更换 WebDAV 账号? (Y/N，默认 N): ");
                string answer = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(answer) && answer.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    string customUser = PromptRequiredText("请输入 WebDAV 账号: ");
                    target.UserName = customUser;
                }

                target.Password = PromptPassword("请输入 WebDAV 密码: ");
            }
            else if (string.IsNullOrEmpty(target.Password))
            {
                WriteError("批量归集失败: WebDAV 密码不能为空。请使用参数: --collect \"{0}\" {1} password", target.Address, target.UserName);
                return null;
            }

            return target;
        }

        private static bool TestCollectTarget(CollectTarget target, bool interactive, out Exception error)
        {
            error = null;
            if (target == null || string.IsNullOrWhiteSpace(target.Address))
            {
                error = new DirectoryNotFoundException("目标地址不能为空。");
                return false;
            }

            try
            {
                if (target.IsWebDav)
                {
                    TestWebDavTarget(target, interactive);
                }
                else
                {
                    TestLocalCollectDirectory(target.Address, interactive);
                }

                WriteSuccess("归集地址访问测试成功: {0}", target.Address);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        private static void TestLocalCollectDirectory(string targetDirectory, bool interactive)
        {
            if (interactive)
            {
                WriteConnectivityProgress("检查本地/SMB路径", 20);
            }
            Directory.CreateDirectory(targetDirectory);
            if (interactive)
            {
                WriteConnectivityProgress("写入测试文件", 70);
            }
            string testFile = Path.Combine(
                targetDirectory,
                ".systeminfo_access_test_" + Environment.MachineName + "_" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(testFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Encoding.UTF8);
            File.Delete(testFile);
            if (interactive)
            {
                WriteConnectivityProgress("访问测试完成", 100);
                Console.WriteLine();
            }
        }

        private static void TestWebDavTarget(CollectTarget target, bool interactive)
        {
            string testDirectoryName = ".systeminfo_access_test_" + Environment.MachineName + "_" + Guid.NewGuid().ToString("N");
            string testFileName = "probe.txt";

            Console.WriteLine("正在测试 WebDAV 归集地址:");
            Console.WriteLine("  目标地址: {0}", target.Address);
            Console.WriteLine("  认证账号: {0}", target.UserName);

            WriteConnectivityProgress("OPTIONS 探测服务", 15);
            WebDavRequest(target, "OPTIONS", "", null, false);
            Console.WriteLine();
            Console.WriteLine("  来自 {0} 的响应: OPTIONS 成功", GetHostText(target.Address));

            WriteConnectivityProgress("创建测试目录", 40);
            TryCreateWebDavDirectory(target, testDirectoryName);
            Console.WriteLine();
            Console.WriteLine("  来自 {0} 的响应: MKCOL 成功", GetHostText(target.Address));

            WriteConnectivityProgress("上传测试文件", 70);
            WebDavRequest(target, "PUT", CombineWebDavPath(testDirectoryName, testFileName), Encoding.UTF8.GetBytes("SystemInfo WebDAV access test"), true);
            Console.WriteLine();
            Console.WriteLine("  来自 {0} 的响应: PUT 成功", GetHostText(target.Address));

            WriteConnectivityProgress("清理测试文件", 95);
            WebDavRequest(target, "DELETE", CombineWebDavPath(testDirectoryName, testFileName), null, false);
            TryDeleteWebDav(target, testDirectoryName);
            WriteConnectivityProgress("访问测试完成", 100);
            Console.WriteLine();
            Console.WriteLine("  来自 {0} 的响应: WebDAV 访问测试成功", GetHostText(target.Address));
        }

        private static string GetCollectShareAccessFailureMessage(Exception error)
        {
            if (error is UnauthorizedAccessException)
            {
                return "归集地址无法访问、账号密码错误或没有写入权限。";
            }

            if (error is System.Net.WebException)
            {
                System.Net.WebException webError = (System.Net.WebException)error;
                System.Net.HttpWebResponse response = webError.Response as System.Net.HttpWebResponse;
                if (response != null && (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden))
                {
                    return "WebDAV 账号密码错误或没有写入权限。";
                }
            }

            return "找不到网络路径或 WebDAV 服务不可达。";
        }

        private static bool IsWebDavAddress(string address)
        {
            return !string.IsNullOrWhiteSpace(address) &&
                (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 address.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static string PromptPassword(string prompt)
        {
            Console.Write(prompt);
            if (Console.IsInputRedirected)
            {
                return Console.ReadLine() ?? "";
            }

            StringBuilder sb = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return sb.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write("*");
                }
            }
        }

        private static void WriteConnectivityProgress(string step, double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "  当前步骤: {0} {1}",
                step,
                BuildConnectivityProgressBar(percent, 42));

            if (!IsInteractiveTerminal)
            {
                Console.WriteLine(line);
                return;
            }

            if (percent < 100)
            {
                HideCursorForProgress();
            }

            int width = 100;
            try
            {
                width = Math.Max(60, Console.WindowWidth - 1);
            }
            catch
            {
            }

            if (line.Length > width)
            {
                line = line.Substring(0, Math.Max(0, width - 3)) + "...";
            }

            int clear = Math.Max(0, _connectivityProgressLength - line.Length);
            string outputLine = _supportsAnsi ? AnsiBlue + line + AnsiReset : line;
            Console.Write("\r" + outputLine + new string(' ', clear));
            _connectivityProgressLength = line.Length;

            if (percent >= 100)
            {
                ShowCursorAfterProgress();
            }
        }

        private static string BuildConnectivityProgressBar(double percent, int width)
        {
            percent = Math.Max(0.0, Math.Min(100.0, percent));
            width = Math.Max(24, Math.Min(width, 54));
            string value = percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            int innerWidth = Math.Max(10, width - value.Length - 3);
            char[] chars = Enumerable.Repeat('░', innerWidth).ToArray();
            int filled = (int)Math.Round(percent / 100.0 * innerWidth);

            for (int i = 0; i < filled && i < chars.Length; i++)
            {
                chars[i] = '█';
            }

            return value.PadLeft(6) + " [" + new string(chars) + "]";
        }

        private static string CombineWebDavPath(params string[] parts)
        {
            List<string> clean = new List<string>();
            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    clean.Add(part.Trim('/').Replace("\\", "/"));
                }
            }

            return string.Join("/", clean.ToArray());
        }

        private static string BuildWebDavDisplayPath(CollectTarget target, string remoteDirectory, string fileName)
        {
            return target.Address.TrimEnd('/') + "/" + CombineWebDavPath(remoteDirectory, fileName);
        }

        private static string EncodeWebDavRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return "";
            }

            string[] parts = relativePath.Replace("\\", "/").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("/", parts.Select(part => Uri.EscapeDataString(part)).ToArray());
        }

        private static string GetHostText(string address)
        {
            try
            {
                Uri uri = new Uri(address);
                return uri.Host;
            }
            catch
            {
                return address;
            }
        }

        private static void UploadTextToWebDav(CollectTarget target, string relativePath, string content)
        {
            WebDavRequest(target, "PUT", relativePath, new UTF8Encoding(true).GetBytes(content ?? ""), true);
        }

        private static void EnsureWebDavDirectoryPath(CollectTarget target, string remoteDirectory)
        {
            if (string.IsNullOrWhiteSpace(remoteDirectory))
            {
                return;
            }

            string current = "";
            foreach (string part in remoteDirectory.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                current = CombineWebDavPath(current, part);
                TryCreateWebDavDirectory(target, current);
            }
        }

        private static void TryCreateWebDavDirectory(CollectTarget target, string relativePath)
        {
            try
            {
                WebDavRequest(target, "MKCOL", relativePath, null, false);
            }
            catch (System.Net.WebException ex)
            {
                System.Net.HttpWebResponse response = ex.Response as System.Net.HttpWebResponse;
                if (response != null && ((int)response.StatusCode == 405 || (int)response.StatusCode == 409))
                {
                    return;
                }

                throw;
            }
        }

        private static void TryDeleteWebDav(CollectTarget target, string relativePath)
        {
            try
            {
                WebDavRequest(target, "DELETE", relativePath, null, false);
            }
            catch
            {
            }
        }

        private static void WebDavRequest(CollectTarget target, string method, string relativePath, byte[] payload, bool hasBody)
        {
            string url = target.Address.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                url += "/" + EncodeWebDavRelativePath(CombineWebDavPath(relativePath));
            }

            System.Net.HttpWebRequest request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.PreAuthenticate = true;
            request.Credentials = new System.Net.NetworkCredential(target.UserName, target.Password);
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(target.UserName + ":" + target.Password));
            request.Headers[System.Net.HttpRequestHeader.Authorization] = "Basic " + token;

            if (hasBody)
            {
                byte[] actualPayload = payload ?? new byte[0];
                request.ContentLength = actualPayload.Length;
                request.ContentType = "application/octet-stream";
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(actualPayload, 0, actualPayload.Length);
                }
            }

            using (System.Net.HttpWebResponse response = (System.Net.HttpWebResponse)request.GetResponse())
            {
            }
        }

        private static string PromptDepartment()
        {
            Console.WriteLine();
            Console.WriteLine("请选择您所在的部门:");
            for (int i = 0; i < DepartmentOptions.Length; i++)
            {
                Console.WriteLine("  {0}. {1}", i + 1, DepartmentOptions[i]);
            }

            while (true)
            {
                Console.Write("请输入部门序号，或直接输入其他部门名称: ");
                string input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input))
                {
                    WriteWarning("部门不能为空，请重新输入。");
                    continue;
                }

                input = input.Trim();
                int index;
                if (int.TryParse(input, out index) && index >= 1 && index <= DepartmentOptions.Length)
                {
                    return DepartmentOptions[index - 1];
                }

                if (int.TryParse(input, out index))
                {
                    WriteWarning("部门序号无效，请重新选择，或直接输入其他部门名称。");
                    continue;
                }

                return input;
            }
        }

        private static string PromptRequiredText(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string value = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }

                WriteWarning("该项不能为空，请重新输入。");
            }
        }

        private static void InsertCollectionRegistrationSection(List<ReportSection> sections, string department, string userName, string location)
        {
            ReportSection section = new ReportSection("批量归集登记信息");
            section.Add("部门", SafeText(department), "用户输入", IsMissing(department) ? ItemStatus.Warning : ItemStatus.Ok);
            section.Add("使用人员姓名", SafeText(userName), "用户输入", IsMissing(userName) ? ItemStatus.Warning : ItemStatus.Ok);
            section.Add("使用位置", SafeText(location), "用户输入", IsMissing(location) ? ItemStatus.Warning : ItemStatus.Ok);
            section.Add("报告日期", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), "系统时间");
            sections.Insert(0, section);
        }

        private static string BuildCollectionFileIdentity(string department, string userName)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-{2}-{3}",
                SafeFilePart(department),
                SafeFilePart(userName),
                SafeFilePart(Environment.MachineName),
                DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
        }

        private static string SafeFilePart(string value)
        {
            return IsMissing(value) ? "未填写" : value.Trim();
        }

        private static List<ReportSection> CollectModulesForExport(IEnumerable<string> moduleKeys, string title, bool showProgress)
        {
            List<ReportSection> sections = new List<ReportSection>();
            List<DetectionModule> selectedModules = Modules.Where(m => moduleKeys.Contains(m.Key)).ToList();
            int current = 0;

            foreach (DetectionModule module in selectedModules)
            {
                current++;
                ReportSection section;
                using (ProgressDisplay progress = new ProgressDisplay(selectedModules.Count, current - 1))
                {
                    if (showProgress)
                    {
                        progress.Start();
                        progress.BeginStep(current, module.Name);
                    }

                    try
                    {
                        section = module.Collect();
                    }
                    catch (Exception ex)
                    {
                        section = new ReportSection(module.Name);
                        section.Add("模块状态", "检测失败: " + ex.Message, "程序异常", ItemStatus.Error);
                    }

                    sections.Add(section);

                    if (showProgress)
                    {
                        progress.CompleteStep(current);
                        progress.Stop();
                    }
                }
            }

            return sections;
        }

        private static ReportSection CollectBasicHardwareSummary()
        {
            ReportSection section = new ReportSection("基础硬件信息");
            string manufacturer = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Manufacturer");
            string model = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Model");
            string osName = FirstWmiValue("SELECT * FROM Win32_OperatingSystem", "Caption");
            string cpuName = FirstWmiValue("SELECT * FROM Win32_Processor", "Name");
            string systemType = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "PCSystemType");
            SerialResult serial = ResolveBestSerialNumber();

            section.Add("计算机名", Environment.MachineName, "Environment.MachineName");
            section.Add("品牌", FormatBrand(manufacturer), "Win32_ComputerSystem.Manufacturer");
            section.Add("型号", SafeText(model), "Win32_ComputerSystem.Model");
            section.Add("设备类型判定", ClassifyDeviceType(manufacturer, model, systemType), "本地设备类型规则");
            section.Add("序列号", serial.Value, serial.Source, serial.Status);
            section.Add("预计出厂日期", GetEstimatedManufactureDate(), "Win32_BIOS.ReleaseDate", ItemStatus.Warning);
            section.Add("操作系统", SafeText(osName), "Win32_OperatingSystem.Caption");
            section.Add("CPU", SafeText(cpuName), "Win32_Processor.Name");

            int nicIndex = 0;
            WmiForEach("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True", delegate(ManagementObject nic)
            {
                nicIndex++;
                string name = SafeText(GetValue(nic, "Description"));
                string mac = SafeText(GetValue(nic, "MACAddress"));
                string ips = JoinArray(nic["IPAddress"]);
                section.Add("网卡" + nicIndex, name + " | MAC: " + mac + " | IP: " + ips, "Win32_NetworkAdapterConfiguration");
            });

            if (nicIndex == 0)
            {
                section.Add("网卡", "未检测到已启用网卡。", "Win32_NetworkAdapterConfiguration", ItemStatus.Warning);
            }

            return section;
        }

        private static void RunBasicHardwareInfo()
        {
            ClearConsole();
            WriteHeader("基础硬件信息检测");

            List<ReportSection> sections = new List<ReportSection>();
            using (ProgressDisplay progress = new ProgressDisplay(1, 0))
            {
                progress.Start();
                progress.BeginStep(1, "基础硬件信息");
                sections.Add(CollectBasicHardwareSummary());
                progress.CompleteStep(1);
                progress.Stop();
            }

            foreach (ReportSection section in sections)
            {
                WriteSection(section, false);
                Console.WriteLine();
            }

            LastReport.Clear();
            LastReport.AddRange(sections);
            _lastReportTitle = "基础硬件信息检测 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _lastReportIncludeSource = false;

            Console.WriteLine("检测完成。");
            Console.WriteLine("按 E 导出本次报告，按 Enter 返回。");
            ConsoleKeyInfo key = ReadKeySafe();
            if (key.Key == ConsoleKey.E)
            {
                ExportReport(LastReport, _lastReportTitle, _lastReportIncludeSource);
                Pause();
            }
        }

        private static void RunModules(string title, IEnumerable<string> moduleKeys, bool showSource = false, bool exportAfterRun = false)
        {
            ClearConsole();
            WriteHeader(title);

            List<ReportSection> sections = new List<ReportSection>();
            List<DetectionModule> selectedModules = Modules.Where(m => moduleKeys.Contains(m.Key)).ToList();
            int current = 0;

            foreach (DetectionModule module in selectedModules)
            {
                current++;
                ReportSection section;
                using (ProgressDisplay progress = new ProgressDisplay(selectedModules.Count, current - 1))
                {
                    progress.Start();
                    progress.BeginStep(current, module.Name);
                    try
                    {
                        section = module.Collect();
                    }
                    catch (Exception ex)
                    {
                        section = new ReportSection(module.Name);
                        section.Add("模块状态", "检测失败: " + ex.Message, "程序异常", ItemStatus.Error);
                    }

                    sections.Add(section);
                    progress.CompleteStep(current);
                    progress.Stop();
                }

                WriteSection(section, showSource);
                Console.WriteLine();
            }

            LastReport.Clear();
            LastReport.AddRange(sections);
            _lastReportTitle = title + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _lastReportIncludeSource = showSource;

            if (exportAfterRun)
            {
                ExportReport(LastReport, _lastReportTitle, _lastReportIncludeSource);
                Pause();
                return;
            }

            Console.WriteLine("检测完成。");
            Console.WriteLine("按 E 导出本次报告，按 Enter 返回。");
            ConsoleKeyInfo key = ReadKeySafe();
            if (key.Key == ConsoleKey.E)
            {
                ExportReport(LastReport, _lastReportTitle, _lastReportIncludeSource);
                Pause();
            }
        }

        private static ReportSection CollectOperatingSystem()
        {
            ReportSection section = new ReportSection("操作系统信息");
            WmiForEach("SELECT * FROM Win32_OperatingSystem", delegate(ManagementObject os)
            {
                string caption = GetValue(os, "Caption");
                string version = GetValue(os, "Version");
                section.Add("操作系统名称", caption, "Win32_OperatingSystem.Caption");
                section.Add("系统版本", GetWindowsVersionDisplay(caption, version), "Win32_OperatingSystem.Version + Registry");
                section.Add("系统架构", GetValue(os, "OSArchitecture"), "Win32_OperatingSystem.OSArchitecture");
                section.Add("产品激活状态", GetWindowsActivationStatus(), "SoftwareLicensingProduct");
                section.Add("安装日期", FormatWmiDate(GetValue(os, "InstallDate")), "Win32_OperatingSystem.InstallDate");
                section.Add("最后启动时间", FormatWmiDate(GetValue(os, "LastBootUpTime")), "Win32_OperatingSystem.LastBootUpTime");
                section.Add("系统目录", GetValue(os, "WindowsDirectory"), "Win32_OperatingSystem.WindowsDirectory");
            });

            section.EnsureNotEmpty("未检测到操作系统信息。");
            return section;
        }

        private static ReportSection CollectComputerSystem()
        {
            ReportSection section = new ReportSection("计算机品牌与型号");
            string manufacturer = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Manufacturer");
            string model = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Model");
            string systemType = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "PCSystemType");
            string serial = FirstWmiValue("SELECT * FROM Win32_ComputerSystemProduct", "IdentifyingNumber");
            string uuid = FirstWmiValue("SELECT * FROM Win32_ComputerSystemProduct", "UUID");

            section.Add("制造商", SafeText(manufacturer), "Win32_ComputerSystem.Manufacturer");
            section.Add("品牌中文名", GetChineseBrandName(manufacturer), "本地品牌规则");
            section.Add("型号", SafeText(model), "Win32_ComputerSystem.Model");
            section.Add("设备类型", GetPcSystemType(systemType), "Win32_ComputerSystem.PCSystemType");
            section.Add("主机序列号", SafeText(serial), "Win32_ComputerSystemProduct.IdentifyingNumber", GetSerialStatus(serial));
            section.Add("UUID", SafeText(uuid), "Win32_ComputerSystemProduct.UUID", GetSerialStatus(uuid));
            AddManufactureDateItems(section, manufacturer);
            return section;
        }

        private static ReportSection CollectProcessor()
        {
            ReportSection section = new ReportSection("处理器信息");
            int index = 0;
            WmiForEach("SELECT * FROM Win32_Processor", delegate(ManagementObject cpu)
            {
                index++;
                string prefix = "CPU" + index + " ";
                section.Add(prefix + "名称", GetValue(cpu, "Name"), "Win32_Processor.Name");
                section.Add(prefix + "核心数", GetValue(cpu, "NumberOfCores"), "Win32_Processor.NumberOfCores");
                section.Add(prefix + "逻辑处理器", GetValue(cpu, "NumberOfLogicalProcessors"), "Win32_Processor.NumberOfLogicalProcessors");
                section.Add(prefix + "最大频率", AppendUnit(GetValue(cpu, "MaxClockSpeed"), "MHz"), "Win32_Processor.MaxClockSpeed");
                section.Add(prefix + "厂商", GetValue(cpu, "Manufacturer"), "Win32_Processor.Manufacturer");
            });

            section.EnsureNotEmpty("未检测到处理器信息。");
            return section;
        }

        private static ReportSection CollectMemory()
        {
            ReportSection section = new ReportSection("内存信息");
            int index = 0;
            double totalGb = 0;

            WmiForEach("SELECT * FROM Win32_PhysicalMemory", delegate(ManagementObject mem)
            {
                index++;
                section.AddDivider("内存插槽" + index);
                double capacityGb = ToDouble(GetValue(mem, "Capacity")) / 1024 / 1024 / 1024;
                totalGb += capacityGb;

                DdrResult ddr = DetectDdrType(mem);
                string speed = FirstNonEmpty(GetValue(mem, "ConfiguredClockSpeed"), GetValue(mem, "Speed"));
                string slot = FirstNonEmpty(GetValue(mem, "BankLabel"), GetValue(mem, "DeviceLocator"), "插槽" + index);
                string formFactor = GetMemoryFormFactor(GetValue(mem, "FormFactor"));
                string value = string.Format(CultureInfo.InvariantCulture, "{0:F2} GB {1} {2}", capacityGb, ddr.Type, AppendUnit(speed, "MHz")).Trim();

                section.Add("插槽位置", slot, "Win32_PhysicalMemory.BankLabel/DeviceLocator");
                section.Add("外形规格", formFactor, "Win32_PhysicalMemory.FormFactor", IsMissing(formFactor) ? ItemStatus.Warning : ItemStatus.Ok);
                section.Add("容量和类型", value, ddr.Source, ddr.Status);
                section.Add("厂商", GetValue(mem, "Manufacturer"), "Win32_PhysicalMemory.Manufacturer");
                section.Add("部件号", GetValue(mem, "PartNumber"), "Win32_PhysicalMemory.PartNumber");
            });

            if (index == 0)
            {
                section.Add("内存状态", "未检测到物理内存条信息。", "Win32_PhysicalMemory", ItemStatus.Warning);
            }
            else
            {
                int totalSlots = GetMemorySlotCount(index);
                section.Insert(0, "内存插槽占用", index.ToString(CultureInfo.InvariantCulture) + "/" + totalSlots.ToString(CultureInfo.InvariantCulture), "Win32_PhysicalMemoryArray.MemoryDevices");
                section.Insert(1, "检测到内存条数量", index.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_PhysicalMemory");
                section.Insert(2, "总内存", string.Format(CultureInfo.InvariantCulture, "{0:F2} GB", totalGb), "Win32_PhysicalMemory.Capacity");
            }

            return section;
        }

        private static ReportSection CollectDisk()
        {
            ReportSection section = new ReportSection("硬盘驱动器信息");
            int physicalCount = 0;
            List<string> physicalSummary = new List<string>();
            WmiForEach("SELECT * FROM Win32_DiskDrive", delegate(ManagementObject disk)
            {
                physicalCount++;

                string model = GetValue(disk, "Model");
                string interfaceType = GetDiskInterfaceType(disk, model, physicalCount);
                double sizeGb = ToDouble(GetValue(disk, "Size")) / 1024 / 1024 / 1024;
                string summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} | {1:F2} GB | {2} | {3}",
                    SafeText(model),
                    sizeGb,
                    GetDiskBrand(model),
                    interfaceType);

                physicalSummary.Add(summary);
            });

            int logicalCount = 0;
            List<string> logicalSummary = new List<string>();
            WmiForEach("SELECT * FROM Win32_LogicalDisk WHERE DriveType = 3", delegate(ManagementObject ld)
            {
                logicalCount++;
                double total = ToDouble(GetValue(ld, "Size")) / 1024 / 1024 / 1024;
                double free = ToDouble(GetValue(ld, "FreeSpace")) / 1024 / 1024 / 1024;
                double used = total - free;
                string deviceId = GetValue(ld, "DeviceID");
                string fs = SafeText(GetValue(ld, "FileSystem"));
                string value = string.Format(CultureInfo.InvariantCulture, "{0} | {1} | 总计 {2:F2} GB | 已用 {3:F2} GB | 可用 {4:F2} GB", SafeText(deviceId), fs, total, used, free);
                logicalSummary.Add(value);
            });

            section.AddDivider("物理硬盘驱动器");
            section.Add("物理硬盘驱动器数量", physicalCount.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_DiskDrive", physicalCount > 0 ? ItemStatus.Ok : ItemStatus.Warning);
            if (physicalSummary.Count == 0)
            {
                section.Add("检测结果", "未检测到物理硬盘驱动器。", "Win32_DiskDrive", ItemStatus.Warning);
            }
            else
            {
                for (int i = 0; i < physicalSummary.Count; i++)
                {
                    section.AddNumberedLine((i + 1).ToString(CultureInfo.InvariantCulture) + ".", physicalSummary[i]);
                }
            }
            section.AddBlankLine();
            section.AddDivider("逻辑磁盘");
            section.Add("逻辑磁盘数量", logicalCount.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_LogicalDisk", logicalCount > 0 ? ItemStatus.Ok : ItemStatus.Warning);
            if (logicalSummary.Count == 0)
            {
                section.Add("检测结果", "未检测到逻辑磁盘。", "Win32_LogicalDisk", ItemStatus.Warning);
            }
            else
            {
                for (int i = 0; i < logicalSummary.Count; i++)
                {
                    section.AddNumberedLine((i + 1).ToString(CultureInfo.InvariantCulture) + ".", logicalSummary[i]);
                }
            }

            if (physicalCount == 0 && logicalCount == 0)
            {
                section.EnsureNotEmpty("未检测到硬盘驱动器信息。");
            }

            return section;
        }

        private static ReportSection CollectVideo()
        {
            ReportSection section = new ReportSection("显卡信息");
            int index = 0;
            WmiForEach("SELECT * FROM Win32_VideoController", delegate(ManagementObject vc)
            {
                index++;
                string ram = "系统未提供";
                double ramBytes = ToDouble(GetValue(vc, "AdapterRAM"));
                if (ramBytes > 0)
                {
                    ram = string.Format(CultureInfo.InvariantCulture, "{0:F0} MB", ramBytes / 1024 / 1024);
                }

                section.Add("显卡" + index, GetValue(vc, "Name"), "Win32_VideoController.Name");
                section.Add("显卡" + index + " 显存", ram, "Win32_VideoController.AdapterRAM", ramBytes > 0 ? ItemStatus.Ok : ItemStatus.Warning);
                section.Add("显卡" + index + " 驱动版本", GetValue(vc, "DriverVersion"), "Win32_VideoController.DriverVersion");
                section.Add("显卡" + index + " 驱动日期", FormatWmiDate(GetValue(vc, "DriverDate")), "Win32_VideoController.DriverDate");
                section.AddBlankLine();
            });

            if (index > 0)
            {
                section.Insert(0, "检测到显卡数量", index.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_VideoController");
            }

            section.EnsureNotEmpty("未检测到显卡信息。");
            return section;
        }

        private static ReportSection CollectBoardAndBios()
        {
            ReportSection section = new ReportSection("主板和 BIOS 信息");
            WmiForEach("SELECT * FROM Win32_BaseBoard", delegate(ManagementObject mb)
            {
                section.Add("主板制造商", GetValue(mb, "Manufacturer"), "Win32_BaseBoard.Manufacturer");
                section.Add("主板名称", BuildBoardName(GetValue(mb, "Manufacturer"), GetValue(mb, "Product")), "Win32_BaseBoard.Manufacturer/Product");
                section.Add("主板产品", GetValue(mb, "Product"), "Win32_BaseBoard.Product");
                section.Add("主板芯片组", DetectChipsetName(), "Win32_PnPEntity");
                section.Add("主板版本", GetValue(mb, "Version"), "Win32_BaseBoard.Version");
                string rawSerial = GetValue(mb, "SerialNumber");
                section.Add("主板原始序列号", SafeText(rawSerial), "Win32_BaseBoard.SerialNumber", GetSerialStatus(rawSerial));
            });

            string dmiSystemVersion = FirstWmiValue("SELECT * FROM Win32_ComputerSystemProduct", "Version");
            string dmiChassisVersion = FirstWmiValue("SELECT * FROM Win32_SystemEnclosure", "Version");
            section.Add("DMI系统版本", SafeText(dmiSystemVersion), "Win32_ComputerSystemProduct.Version", IsMissing(dmiSystemVersion) ? ItemStatus.Warning : ItemStatus.Ok);
            section.Add("DMI主机版本", SafeText(dmiChassisVersion), "Win32_SystemEnclosure.Version", IsMissing(dmiChassisVersion) ? ItemStatus.Warning : ItemStatus.Ok);

            WmiForEach("SELECT * FROM Win32_BIOS", delegate(ManagementObject bios)
            {
                section.Add("BIOS 名称", GetValue(bios, "Name"), "Win32_BIOS.Name");
                section.Add("BIOS 版本", GetValue(bios, "SMBIOSBIOSVersion"), "Win32_BIOS.SMBIOSBIOSVersion");
                section.Add("BIOS 序列号", GetValue(bios, "SerialNumber"), "Win32_BIOS.SerialNumber", GetSerialStatus(GetValue(bios, "SerialNumber")));
                section.Add("BIOS 发布日期", FormatWmiDate(GetValue(bios, "ReleaseDate")), "Win32_BIOS.ReleaseDate");
            });

            SerialResult serial = ResolveBestSerialNumber();
            section.Add("推荐序列号", serial.Value, serial.Source, serial.Status);
            section.EnsureNotEmpty("未检测到主板或 BIOS 信息。");
            return section;
        }

        private static ReportSection CollectNetwork()
        {
            ReportSection section = new ReportSection("网络信息");
            int index = 0;
            WmiForEach("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True", delegate(ManagementObject nic)
            {
                index++;
                section.AddDivider("网卡" + index);
                section.Add("描述", GetValue(nic, "Description"), "Win32_NetworkAdapterConfiguration.Description");
                section.Add("MAC", GetValue(nic, "MACAddress"), "Win32_NetworkAdapterConfiguration.MACAddress");
                section.Add("IP", JoinArray(nic["IPAddress"]), "Win32_NetworkAdapterConfiguration.IPAddress");
                section.Add("子网掩码", JoinArray(nic["IPSubnet"]), "Win32_NetworkAdapterConfiguration.IPSubnet");
                section.Add("默认网关", JoinArray(nic["DefaultIPGateway"]), "Win32_NetworkAdapterConfiguration.DefaultIPGateway");
            });

            if (index > 0)
            {
                section.Insert(0, "检测到网卡设备数量", index.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_NetworkAdapterConfiguration");
                string reviewMessage = GetLaptopNetworkReviewMessage();
                if (!IsMissing(reviewMessage))
                {
                    section.Insert(1, "笔记本网卡核实提示", reviewMessage, "Win32_NetworkAdapter", ItemStatus.Warning);
                }
            }

            section.EnsureNotEmpty("未检测到已启用的网卡信息。");
            return section;
        }

        private static string GetLaptopNetworkReviewMessage()
        {
            string pcSystemType = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "PCSystemType");
            if (ToInt(pcSystemType) != 2)
            {
                return "";
            }

            NetworkAdapterStats stats = GetNetworkAdapterStats();
            bool onlyWifiEnabled = stats.WirelessEnabled > 0 && stats.WiredEnabled == 0;
            if (onlyWifiEnabled && stats.WiredDisabled > 0)
            {
                return "需核实：笔记本电脑当前仅检测到 WiFi 正在使用，且检测到有线网络适配器已禁用。";
            }

            if (onlyWifiEnabled && stats.WiredTotal == 0)
            {
                return "需核实：笔记本电脑当前仅检测到 WiFi 设备，可能为未集成有线网卡设备的笔记本电脑。";
            }

            if (onlyWifiEnabled)
            {
                return "需核实：笔记本电脑当前未检测到已启用的有线网络适配器。";
            }

            if (stats.WiredDisabled > 0)
            {
                return "需核实：检测到有线网络适配器已禁用。";
            }

            return "";
        }

        private static NetworkAdapterStats GetNetworkAdapterStats()
        {
            NetworkAdapterStats stats = new NetworkAdapterStats();
            WmiForEach("SELECT * FROM Win32_NetworkAdapter", delegate(ManagementObject adapter)
            {
                string name = GetValue(adapter, "Name");
                string description = GetValue(adapter, "Description");
                string adapterType = GetValue(adapter, "AdapterType");
                string netConnectionId = GetValue(adapter, "NetConnectionID");
                string text = (name + " " + description + " " + adapterType + " " + netConnectionId).Trim();
                if (IsVirtualOrNonPhysicalNetworkAdapter(text))
                {
                    return;
                }

                bool enabled = GetValue(adapter, "NetEnabled").Equals("True", StringComparison.OrdinalIgnoreCase);
                if (IsWirelessNetworkAdapter(text))
                {
                    stats.WirelessTotal++;
                    if (enabled)
                    {
                        stats.WirelessEnabled++;
                    }
                    return;
                }

                if (IsWiredNetworkAdapter(text))
                {
                    stats.WiredTotal++;
                    if (enabled)
                    {
                        stats.WiredEnabled++;
                    }
                    else
                    {
                        stats.WiredDisabled++;
                    }
                }
            });

            return stats;
        }

        private static bool IsWirelessNetworkAdapter(string text)
        {
            return ContainsAnyText(text, "Wi-Fi", "WiFi", "Wireless", "WLAN", "802.11", "无线");
        }

        private static bool IsWiredNetworkAdapter(string text)
        {
            if (IsWirelessNetworkAdapter(text))
            {
                return false;
            }

            return ContainsAnyText(text, "Ethernet", "以太网", "有线", "Gigabit", "GbE", "2.5G", "10G", "PCIe", "RJ45", "802.3", "LAN");
        }

        private static bool IsVirtualOrNonPhysicalNetworkAdapter(string text)
        {
            return ContainsAnyText(text, "Bluetooth", "蓝牙", "Virtual", "Hyper-V", "VMware", "VirtualBox", "TAP", "VPN", "WAN Miniport", "Loopback", "Teredo", "Kernel Debug", "Npcap");
        }

        private static ReportSection CollectAudioDevices()
        {
            ReportSection section = new ReportSection("音频设备信息");
            int soundIndex = 0;
            WmiForEach("SELECT * FROM Win32_SoundDevice", delegate(ManagementObject sound)
            {
                soundIndex++;
                section.Add("声卡" + soundIndex, GetValue(sound, "Name"), "Win32_SoundDevice.Name");
                section.Add("声卡" + soundIndex + " 制造商", GetValue(sound, "Manufacturer"), "Win32_SoundDevice.Manufacturer");
                section.AddBlankLine();
            });
            if (soundIndex == 0)
            {
                section.Add("声卡", "未检测到声卡信息。", "Win32_SoundDevice", ItemStatus.Warning);
            }
            else
            {
                section.Insert(0, "检测到声卡设备数量", soundIndex.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_SoundDevice");
            }

            return section;
        }

        private static ReportSection CollectBatteryDevices()
        {
            ReportSection section = new ReportSection("电池信息");
            int batteryIndex = 0;
            WmiForEach("SELECT * FROM Win32_Battery", delegate(ManagementObject battery)
            {
                batteryIndex++;
                section.Add("电池" + batteryIndex, GetValue(battery, "Name"), "Win32_Battery.Name");
                section.Add("电池" + batteryIndex + " 状态", GetBatteryStatus(GetValue(battery, "BatteryStatus")), "Win32_Battery.BatteryStatus");
                section.Add("电池" + batteryIndex + " 剩余时间", AppendUnit(GetValue(battery, "EstimatedRunTime"), "分钟"), "Win32_Battery.EstimatedRunTime");
            });
            if (batteryIndex == 0)
            {
                section.Add("电池", "未检测到电池，可能为台式机或 WMI 未提供。", "Win32_Battery", ItemStatus.Warning);
            }
            else
            {
                section.Add("检测到电池设备数量", batteryIndex.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_Battery");
            }

            return section;
        }

        private static ReportSection CollectMonitorDevices()
        {
            ReportSection section = new ReportSection("显示器信息");
            int monitorIndex = 0;
            WmiForEach("SELECT * FROM Win32_DesktopMonitor", delegate(ManagementObject monitor)
            {
                monitorIndex++;
                string width = GetValue(monitor, "ScreenWidth");
                string height = GetValue(monitor, "ScreenHeight");
                section.Add("显示器" + monitorIndex, GetValue(monitor, "Name"), "Win32_DesktopMonitor.Name");
                section.Add("显示器" + monitorIndex + " 分辨率", SafeText(width) + " x " + SafeText(height), "Win32_DesktopMonitor.ScreenWidth/ScreenHeight");
            });
            if (monitorIndex == 0)
            {
                section.Add("显示器", "未检测到显示器信息。", "Win32_DesktopMonitor", ItemStatus.Warning);
            }
            else
            {
                section.Add("检测到显示器设备数量", monitorIndex.ToString(CultureInfo.InvariantCulture) + " 个", "Win32_DesktopMonitor");
            }

            return section;
        }

        private static ReportSection CollectPeripheralDevices()
        {
            ReportSection section = new ReportSection("外围设备信息");
            int printerIndex = 0;
            WmiForEach("SELECT * FROM Win32_Printer", delegate(ManagementObject printer)
            {
                printerIndex++;
                string name = GetValue(printer, "Name");
                string driverName = GetValue(printer, "DriverName");
                string portName = GetValue(printer, "PortName");
                PrinterPortInfo portInfo = GetPrinterPortInfo(portName);

                section.AddDivider("打印机" + printerIndex);
                section.Add("打印机名称", SafeText(name), "Win32_Printer.Name");
                section.Add("品牌", GetPrinterBrand(name, driverName), "Win32_Printer.Name/DriverName");
                section.Add("型号", GetPrinterModel(name, driverName), "Win32_Printer.Name/DriverName");
                section.Add("驱动信息", SafeText(driverName), "Win32_Printer.DriverName");
                section.Add("驱动版本", GetPrinterDriverVersion(driverName), "Win32_PrinterDriver.DriverPath");
                section.Add("序列号", GetPrinterSerialNumber(name), "Win32_PnPEntity.PNPDeviceID", ItemStatus.Warning);
                section.Add("IP地址", portInfo.IpAddress, "Win32_TCPIPPrinterPort.HostAddress", IsMissing(portInfo.IpAddress) ? ItemStatus.Warning : ItemStatus.Ok);
                section.Add("连接方式", GetPrinterConnectionType(printer, portInfo), "Win32_Printer.PortName/Network");
                section.Add("端口", SafeText(portName), "Win32_Printer.PortName");
                section.Add("状态", GetPrinterStatusText(GetValue(printer, "PrinterStatus")), "Win32_Printer.PrinterStatus");
            });

            if (printerIndex == 0)
            {
                section.Add("打印机", "未检测到已安装打印机。", "Win32_Printer", ItemStatus.Warning);
            }
            else
            {
                section.Insert(0, "检测到打印机数量", printerIndex.ToString(CultureInfo.InvariantCulture) + " 台", "Win32_Printer");
            }

            return section;
        }

        private static void RunXinChuangJudgement()
        {
            ClearConsole();
            WriteHeader("信创产品判断");

            ReportSection section = null;
            using (ProgressDisplay progress = new ProgressDisplay(5, 0))
            {
                progress.Start();
                progress.BeginStep(1, "读取系统硬件信息");
                section = CollectXinChuang();
                Thread.Sleep(180);
                progress.CompleteStep(1);

                progress.BeginStep(2, "分析品牌与型号规则");
                Thread.Sleep(220);
                progress.CompleteStep(2);

                progress.BeginStep(3, "分析 CPU 与操作系统规则");
                Thread.Sleep(220);
                progress.CompleteStep(3);

                progress.BeginStep(4, "计算综合线索分值");
                Thread.Sleep(220);
                progress.CompleteStep(4);

                progress.BeginStep(5, "生成判断过程");
                Thread.Sleep(180);
                progress.CompleteStep(5);
                progress.Stop();
            }

            if (section == null)
            {
                section = new ReportSection("信创产品判定");
                section.Add("判断结果", "信创产品判断失败。", "本地评分规则", ItemStatus.Warning);
            }

            LastReport.Clear();
            LastReport.Add(section);
            _lastReportTitle = "信创产品判断 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            _lastReportIncludeSource = false;

            WriteSection(section, false);
            Pause();
        }

        private static ReportSection CollectXinChuang()
        {
            ReportSection section = new ReportSection("信创产品判定");
            string manufacturer = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Manufacturer");
            string model = FirstWmiValue("SELECT * FROM Win32_ComputerSystem", "Model");
            string cpuName = FirstWmiValue("SELECT * FROM Win32_Processor", "Name");
            string osName = FirstWmiValue("SELECT * FROM Win32_OperatingSystem", "Caption");
            XinChuangAssessment assessment = EvaluateXinChuang(manufacturer, model, cpuName, osName);

            section.Add("判断结果", assessment.Result, "本地评分规则", assessment.Status);
            section.Add("线索分值", assessment.Score + "/100", "品牌30 + CPU35 + 系统20 + 型号15", assessment.Status);
            section.Add("判断过程", "按品牌、型号、CPU、操作系统四类规则匹配关键词并汇总线索分值；结果仅用于初筛，不代表认证或合规结论。", "本地评分规则", assessment.Status);
            section.Add("品牌", SafeText(manufacturer), "Win32_ComputerSystem.Manufacturer", assessment.BrandHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("品牌命中", assessment.BrandHit.Hit ? assessment.BrandHit.Keyword : "未命中信创整机品牌关键词", "本地品牌规则", assessment.BrandHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("型号", SafeText(model), "Win32_ComputerSystem.Model", assessment.ModelHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("型号命中", assessment.ModelHit.Hit ? assessment.ModelHit.Keyword : "未命中信创型号关键词", "本地型号规则", assessment.ModelHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("CPU", SafeText(cpuName), "Win32_Processor.Name", assessment.CpuHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("CPU 命中", assessment.CpuHit.Hit ? assessment.CpuHit.Keyword : "未命中国产 CPU 关键词", "本地 CPU 规则", assessment.CpuHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("操作系统", SafeText(osName), "Win32_OperatingSystem.Caption", assessment.OsHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("系统命中", assessment.OsHit.Hit ? assessment.OsHit.Keyword : "未命中国产操作系统关键词", "本地系统规则", assessment.OsHit.Hit ? ItemStatus.Ok : ItemStatus.Warning);
            section.Add("复核提示", "请结合采购台账、产品认证、设备铭牌和现场信息人工复核。", "发布说明", ItemStatus.Warning);
            return section;
        }

        private static XinChuangAssessment EvaluateXinChuang(string manufacturer, string model, string cpuName, string osName)
        {
            XinChuangAssessment assessment = new XinChuangAssessment();
            assessment.BrandHit = ContainsAny(manufacturer, XinChuangBrandKeywords);
            assessment.CpuHit = ContainsAny(cpuName, XinChuangCpuKeywords);
            assessment.OsHit = ContainsAny(osName, XinChuangOsKeywords);
            assessment.ModelHit = ContainsAny(model, XinChuangModelKeywords);
            if (assessment.BrandHit.Hit) assessment.Score += 30;
            if (assessment.CpuHit.Hit) assessment.Score += 35;
            if (assessment.OsHit.Hit) assessment.Score += 20;
            if (assessment.ModelHit.Hit) assessment.Score += 15;

            if (assessment.Score >= 75)
            {
                assessment.Result = "高度疑似信创设备（需人工复核）";
                assessment.Status = ItemStatus.Ok;
            }
            else if (assessment.Score >= 45)
            {
                assessment.Result = "疑似信创设备（需人工复核）";
                assessment.Status = ItemStatus.Warning;
            }
            else
            {
                assessment.Result = "信创线索不足（不等于非信创）";
                assessment.Status = ItemStatus.Warning;
            }

            return assessment;
        }

        private static bool RunXinChuangRuleSelfTests()
        {
            var cases = new[]
            {
                new { Name = "银河麒麟飞腾整机", Brand = "中国长城", Model = "飞腾 D2000", Cpu = "Phytium D2000", Os = "银河麒麟桌面操作系统 V10", Minimum = 75 },
                new { Name = "统信兆芯终端", Brand = "清华同方", Model = "超翔 Z800", Cpu = "Zhaoxin KX-U6780A", Os = "UnionTech OS Desktop 20", Minimum = 75 },
                new { Name = "openEuler鲲鹏服务器", Brand = "Huawei", Model = "Kunpeng Server", Cpu = "Kunpeng 920", Os = "openEuler 24.03", Minimum = 75 },
                new { Name = "openKylin龙芯设备", Brand = "中科曙光", Model = "LoongArch Workstation", Cpu = "Loongson-3A6000", Os = "openKylin 2.0", Minimum = 75 },
                new { Name = "普通Windows电脑", Brand = "Lenovo", Model = "ThinkPad T14", Cpu = "Intel Core Ultra 7", Os = "Microsoft Windows 11", Minimum = 0 }
            };

            bool passed = true;
            foreach (var item in cases)
            {
                XinChuangAssessment assessment = EvaluateXinChuang(item.Brand, item.Model, item.Cpu, item.Os);
                bool casePassed = item.Minimum == 0 ? assessment.Score < 45 : assessment.Score >= item.Minimum;
                Console.WriteLine("[{0}] {1}: {2}/100 - {3}", casePassed ? "PASS" : "FAIL", item.Name, assessment.Score, assessment.Result);
                passed = passed && casePassed;
            }

            Console.WriteLine(passed ? "信创规则自检通过。" : "信创规则自检失败。");
            return passed;
        }

        private static void AddManufactureDateItems(ReportSection section, string manufacturer)
        {
            string biosDate = FirstWmiValue("SELECT ReleaseDate FROM Win32_BIOS", "ReleaseDate");
            string formattedDate = FormatWmiDate(biosDate);
            if (IsMissing(formattedDate))
            {
                section.Add("出厂日期参考", "无法自动检测。建议使用序列号到官网核实。", "Win32_BIOS.ReleaseDate", ItemStatus.Warning);
                string url = GetOfficialWarrantyUrl(manufacturer);
                if (!IsMissing(url))
                {
                    section.Add("官方查询链接", url, "本地品牌规则");
                }
                return;
            }

            section.Add("出厂日期参考", formattedDate, "Win32_BIOS.ReleaseDate");
            DateTime date;
            if (DateTime.TryParse(formattedDate, out date))
            {
                int diff = DateTime.Now.Year - date.Year;
                if (diff > 10 || diff < 0)
                {
                    section.Add("日期状态", "与当前年份相差 " + Math.Abs(diff) + " 年，建议官网核实。", "本地日期规则", ItemStatus.Warning);
                    string url = GetOfficialWarrantyUrl(manufacturer);
                    if (!IsMissing(url))
                    {
                        section.Add("官方查询链接", url, "本地品牌规则");
                    }
                }
                else
                {
                    section.Add("日期状态", "日期合理，与当前年份相差 " + diff + " 年。", "本地日期规则");
                }
            }
        }

        private static string GetEstimatedManufactureDate()
        {
            string biosDate = FirstWmiValue("SELECT ReleaseDate FROM Win32_BIOS", "ReleaseDate");
            string formattedDate = FormatWmiDate(biosDate);
            if (IsMissing(formattedDate))
            {
                return "无法自动检测，建议使用序列号到官网核实";
            }

            DateTime date;
            if (DateTime.TryParse(formattedDate, out date))
            {
                int diff = DateTime.Now.Year - date.Year;
                if (diff > 10 || diff < 0)
                {
                    return formattedDate + "（与当前年份相差 " + Math.Abs(diff) + " 年，建议官网核实）";
                }
            }

            return formattedDate;
        }

        private static string GetWindowsVersionDisplay(string caption, string wmiVersion)
        {
            string fullVersion = GetFullWindowsVersion(wmiVersion);
            string family = GetWindowsFamily(caption, fullVersion);
            string release = GetWindowsDisplayVersion(fullVersion);

            if (IsMissing(fullVersion))
            {
                return "系统未提供";
            }

            if (IsMissing(release))
            {
                return fullVersion + " (" + family + ")";
            }

            return fullVersion + " (" + family + " " + release + ")";
        }

        private static string GetFullWindowsVersion(string wmiVersion)
        {
            string version = SafeText(wmiVersion);
            int ubr = ReadRegistryInt(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
            if (ubr > 0 && !IsMissing(version) && version.Count(c => c == '.') == 2)
            {
                return version + "." + ubr.ToString(CultureInfo.InvariantCulture);
            }

            return version;
        }

        private static string GetWindowsFamily(string caption, string version)
        {
            int build = GetWindowsBuildNumber(version);
            if (Contains(caption, "Windows 11") || build >= 22000)
            {
                return "Win11";
            }

            if (Contains(caption, "Windows 10") || version.StartsWith("10.0.", StringComparison.OrdinalIgnoreCase))
            {
                return "Win10";
            }

            if (Contains(caption, "Windows 8")) return "Win8";
            if (Contains(caption, "Windows 7")) return "Win7";
            return SafeText(caption);
        }

        private static string GetWindowsDisplayVersion(string fullVersion)
        {
            string displayVersion = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            if (!IsMissing(displayVersion))
            {
                return displayVersion;
            }

            string releaseId = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ReleaseId");
            if (!IsMissing(releaseId))
            {
                return releaseId;
            }

            int build = GetWindowsBuildNumber(fullVersion);
            if (build >= 26200) return "24H2";
            if (build >= 26100) return "24H2";
            if (build >= 22631) return "23H2";
            if (build >= 22621) return "22H2";
            if (build >= 22000) return "21H2";
            if (build >= 19045) return "22H2";
            if (build >= 19044) return "21H2";
            if (build >= 19043) return "21H1";
            if (build >= 19042) return "20H2";
            if (build >= 19041) return "2004";
            return "";
        }

        private static int GetWindowsBuildNumber(string fullVersion)
        {
            if (IsMissing(fullVersion))
            {
                return 0;
            }

            string[] parts = fullVersion.Split('.');
            if (parts.Length >= 3)
            {
                return ToInt(parts[2]);
            }

            return 0;
        }

        private static string GetWindowsActivationStatus()
        {
            string best = "";
            WmiForEach("SELECT Name, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL", delegate(ManagementObject product)
            {
                string name = GetValue(product, "Name");
                if (!Contains(name, "Windows"))
                {
                    return;
                }

                string statusText = GetLicenseStatusText(GetValue(product, "LicenseStatus"));
                string key = GetValue(product, "PartialProductKey");
                best = SafeText(statusText) + (IsMissing(key) ? "" : "（尾号: " + key + "）");
            });

            return IsMissing(best) ? "系统未提供或无法读取激活信息" : best;
        }

        private static string GetLicenseStatusText(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 0) return "未授权";
            if (code == 1) return "已激活";
            if (code == 2) return "初始宽限期";
            if (code == 3) return "额外宽限期";
            if (code == 4) return "非正版宽限期";
            if (code == 5) return "通知状态";
            if (code == 6) return "延长宽限期";
            return "未知状态";
        }

        private static string ReadRegistryString(string subKey, string name)
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(subKey))
                {
                    object value = key == null ? null : key.GetValue(name);
                    return value == null ? "" : value.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private static int ReadRegistryInt(string subKey, string name)
        {
            string value = ReadRegistryString(subKey, name);
            return ToInt(value);
        }

        private static string FormatBrand(string manufacturer)
        {
            string brand = SafeText(manufacturer);
            string chineseName = GetChineseBrandName(manufacturer);
            if (!IsMissing(chineseName) && chineseName != "其他" && !Contains(brand, chineseName))
            {
                return brand + "（" + chineseName + "）";
            }

            return brand;
        }

        private static string ClassifyDeviceType(string manufacturer, string model, string pcSystemType)
        {
            int typeCode = ToInt(pcSystemType);
            if (typeCode == 2)
            {
                return "笔记本电脑";
            }

            bool desktopLike = typeCode == 1 || typeCode == 3 || typeCode == 0;
            if (!desktopLike)
            {
                return GetPcSystemType(pcSystemType);
            }

            if (IsCompatibleDesktop(manufacturer, model))
            {
                return "台式组装兼容机";
            }

            if (IsKnownBrandManufacturer(manufacturer))
            {
                return "台式品牌机";
            }

            if (!IsMissing(manufacturer) && !IsGenericHardwareText(manufacturer))
            {
                return "台式品牌机";
            }

            return "台式组装兼容机";
        }

        private static bool IsCompatibleDesktop(string manufacturer, string model)
        {
            if (IsMissing(manufacturer) && IsMissing(model))
            {
                return true;
            }

            return IsGenericHardwareText(manufacturer) || IsGenericHardwareText(model);
        }

        private static bool IsKnownBrandManufacturer(string manufacturer)
        {
            string brandName = GetChineseBrandName(manufacturer);
            return !IsMissing(brandName) && brandName != "其他";
        }

        private static bool IsGenericHardwareText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string normalized = value.Trim().ToUpperInvariant();
            string[] genericValues =
            {
                "SYSTEM MANUFACTURER",
                "SYSTEM PRODUCT NAME",
                "TO BE FILLED BY O.E.M.",
                "TO BE FILLED BY OEM",
                "DEFAULT STRING",
                "OEM",
                "O.E.M.",
                "UNKNOWN",
                "NONE",
                "N/A",
                "系统未提供"
            };

            return genericValues.Contains(normalized);
        }

        private static string BuildBoardName(string manufacturer, string product)
        {
            string m = SafeText(manufacturer);
            string p = SafeText(product);
            if (IsMissing(m) && IsMissing(p)) return "系统未提供";
            if (IsMissing(m)) return p;
            if (IsMissing(p)) return m;
            if (Contains(p, m)) return p;
            return m + " " + p;
        }

        private static string DetectChipsetName()
        {
            string chipset = "";
            WmiForEach("SELECT Name FROM Win32_PnPEntity", delegate(ManagementObject device)
            {
                if (!IsMissing(chipset))
                {
                    return;
                }

                string name = GetValue(device, "Name");
                if (IsPotentialChipsetName(name))
                {
                    chipset = name;
                }
            });

            return IsMissing(chipset) ? "系统未提供" : chipset;
        }

        private static bool IsPotentialChipsetName(string name)
        {
            if (IsMissing(name))
            {
                return false;
            }

            string upper = name.ToUpperInvariant();
            if (upper.Contains("CHIPSET")) return true;
            if (upper.Contains("SMBUS")) return true;
            if (upper.Contains("LPC CONTROLLER")) return true;
            if (upper.Contains("PCI EXPRESS ROOT")) return true;
            if (upper.Contains("ROOT COMPLEX")) return true;
            return false;
        }

        private static string GetMemoryFormFactor(string formFactorCode)
        {
            int code = ToInt(formFactorCode);
            if (code == 0) return "未知";
            if (code == 1) return "Other";
            if (code == 2) return "SIP";
            if (code == 3) return "DIP";
            if (code == 4) return "ZIP";
            if (code == 5) return "SOJ";
            if (code == 6) return "Proprietary";
            if (code == 7) return "SIMM";
            if (code == 8) return "DIMM";
            if (code == 9) return "TSOP";
            if (code == 10) return "PGA";
            if (code == 11) return "RIMM";
            if (code == 12) return "SODIMM";
            if (code == 13) return "SRIMM";
            if (code == 14) return "SMD";
            if (code == 15) return "SSMP";
            if (code == 16) return "QFP";
            if (code == 17) return "TQFP";
            if (code == 18) return "SOIC";
            if (code == 19) return "LCC";
            if (code == 20) return "PLCC";
            if (code == 21) return "BGA";
            if (code == 22) return "FPBGA";
            if (code == 23) return "LGA";
            return "未知";
        }

        private static int GetMemorySlotCount(int usedSlots)
        {
            int maxSlots = 0;
            WmiForEach("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray", delegate(ManagementObject array)
            {
                int count = ToInt(GetValue(array, "MemoryDevices"));
                if (count > maxSlots)
                {
                    maxSlots = count;
                }
            });

            return maxSlots > 0 ? maxSlots : usedSlots;
        }

        private static string GetDiskBrand(string model)
        {
            if (IsMissing(model))
            {
                return "系统未提供";
            }

            string text = model.Trim();
            string upper = text.ToUpperInvariant();
            if (upper.Contains("SAMSUNG")) return "Samsung";
            if (upper.Contains("WDC") || upper.Contains("WESTERN DIGITAL") || upper.StartsWith("WD")) return "Western Digital";
            if (upper.Contains("SEAGATE") || upper.Contains("ST")) return "Seagate";
            if (upper.Contains("KINGSTON")) return "Kingston";
            if (upper.Contains("MICRON") || upper.Contains("CRUCIAL")) return "Micron/Crucial";
            if (upper.Contains("SKHYNIX") || upper.Contains("HYNIX")) return "SK hynix";
            if (upper.Contains("INTEL")) return "Intel";
            if (upper.Contains("TOSHIBA") || upper.Contains("KIOXIA")) return "Kioxia/Toshiba";
            if (upper.Contains("SANDISK")) return "SanDisk";
            if (upper.Contains("LENOVO")) return "Lenovo";

            string[] parts = text.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "系统未提供";
        }

        private static string GetDiskInterfaceType(ManagementObject disk, string model, int index)
        {
            string busType = GetPhysicalDiskValue(model, index, "BusType");
            string mappedBus = MapStorageBusType(busType);
            if (!IsMissing(mappedBus))
            {
                return FormatDiskInterfaceWithMedia(mappedBus, model, index);
            }

            string interfaceType = GetValue(disk, "InterfaceType");
            string pnp = GetValue(disk, "PNPDeviceID");
            string mediaType = MapPhysicalDiskMediaType(GetPhysicalDiskValue(model, index, "MediaType"));
            string combined = (SafeText(model) + " " + SafeText(pnp) + " " + SafeText(interfaceType)).ToUpperInvariant();

            if (combined.Contains("NVME") || combined.Contains("NVM EXPRESS"))
            {
                return "SSD（NVMe / PCIe）";
            }

            if (combined.Contains("SATA") || combined.Contains("AHCI") || Contains(interfaceType, "IDE"))
            {
                return Contains(mediaType, "SSD") ? "SSD（SATA）" : "SATA";
            }

            if (combined.Contains("USB"))
            {
                return Contains(mediaType, "SSD") ? "外置 SSD（USB）" : "USB";
            }

            if (combined.Contains("SCSI") || combined.Contains("SAS"))
            {
                return Contains(mediaType, "SSD") ? "SSD（SCSI/SAS）" : "SCSI/SAS";
            }

            if (Contains(mediaType, "SSD"))
            {
                return "SSD（接口类型系统未提供）";
            }

            return SafeText(interfaceType);
        }

        private static string FormatDiskInterfaceWithMedia(string busType, string model, int index)
        {
            string mediaType = MapPhysicalDiskMediaType(GetPhysicalDiskValue(model, index, "MediaType"));
            if (Contains(mediaType, "SSD"))
            {
                if (Contains(busType, "NVMe") || Contains(busType, "PCIe"))
                {
                    return "SSD（NVMe / PCIe）";
                }

                if (Contains(busType, "SATA"))
                {
                    return "SSD（SATA）";
                }

                if (Contains(busType, "USB"))
                {
                    return "外置 SSD（USB）";
                }

                return "SSD（" + busType + "）";
            }

            return busType;
        }

        private static string GetPhysicalDiskValue(string model, int index, string property)
        {
            string result = "";
            int current = 0;
            WmiForEachInNamespace(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk", delegate(ManagementObject disk)
            {
                current++;
                if (!IsMissing(result))
                {
                    return;
                }

                string friendlyName = GetValue(disk, "FriendlyName");
                bool modelMatched = !IsMissing(model) && !IsMissing(friendlyName) && (Contains(model, friendlyName) || Contains(friendlyName, model));
                if (modelMatched || current == index)
                {
                    result = GetValue(disk, property);
                }
            });

            return result;
        }

        private static string MapStorageBusType(string busTypeText)
        {
            int busType = ToInt(busTypeText);
            if (busType == 0) return "";
            if (busType == 3) return "ATA/SATA";
            if (busType == 4) return "IEEE 1394";
            if (busType == 5) return "SSA";
            if (busType == 6) return "Fibre Channel";
            if (busType == 7) return "USB";
            if (busType == 8) return "RAID";
            if (busType == 9) return "iSCSI";
            if (busType == 10) return "SAS";
            if (busType == 11) return "SATA";
            if (busType == 12) return "SD";
            if (busType == 13) return "MMC";
            if (busType == 16) return "Storage Spaces";
            if (busType == 17) return "NVMe / PCIe";
            if (busType == 18) return "SCM";
            if (busType == 19) return "UFS";
            return "系统未提供";
        }

        private static string GetDiskCurrentSpeed(string interfaceType)
        {
            if (Contains(interfaceType, "SATA"))
            {
                return "系统未提供（SATA 协商速率 WMI 通常不公开）";
            }

            if (Contains(interfaceType, "NVMe") || Contains(interfaceType, "PCIe"))
            {
                return "系统未提供（PCIe 当前链路速率需控制器专用接口）";
            }

            if (Contains(interfaceType, "USB"))
            {
                return "系统未提供";
            }

            return "系统未提供";
        }

        private static string GetDiskMaxSpeed(string interfaceType, string model)
        {
            if (Contains(interfaceType, "SATA"))
            {
                return "SATA III 6.0 Gb/s（常见上限，需以设备规格为准）";
            }

            if (Contains(interfaceType, "NVMe") || Contains(interfaceType, "PCIe"))
            {
                string inferred = InferPcieGenerationFromDiskModel(model);
                return IsMissing(inferred) ? "系统未提供（需根据硬盘型号规格确认）" : inferred;
            }

            return "系统未提供";
        }

        private static string InferPcieGenerationFromDiskModel(string model)
        {
            string upper = SafeText(model).ToUpperInvariant();
            string[] pcie4Keywords = { "980 PRO", "990 PRO", "SN850", "SN850X", "P44", "P41", "P5 PLUS", "KC3000", "FURY RENEGADE", "GM7000", "T700", "P3 PLUS", "P44 PRO" };
            string[] pcie5Keywords = { "T700", "T705", "MP700", "GEN5", "PCIe 5" };

            foreach (string keyword in pcie5Keywords)
            {
                if (upper.Contains(keyword.ToUpperInvariant()))
                {
                    return "PCIe 5.0（根据型号关键词推测）";
                }
            }

            foreach (string keyword in pcie4Keywords)
            {
                if (upper.Contains(keyword.ToUpperInvariant()))
                {
                    return "PCIe 4.0（根据型号关键词推测）";
                }
            }

            return "";
        }

        private static string GetDiskHealthStatus(string model, int index)
        {
            string health = GetPhysicalDiskValue(model, index, "HealthStatus");
            string mapped = MapDiskHealthStatus(health);
            string smart = GetSmartPredictionStatus(index);

            if (!IsMissing(mapped) && !IsMissing(smart))
            {
                return mapped + "，" + smart;
            }

            return FirstNonEmpty(mapped, smart, "系统未提供");
        }

        private static string MapDiskHealthStatus(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 0) return "健康";
            if (code == 1) return "警告";
            if (code == 2) return "不健康";
            if (code == 5) return "未知";
            return "";
        }

        private static string GetSmartPredictionStatus(int index)
        {
            string result = "";
            int current = 0;
            WmiForEachInNamespace(@"root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus", delegate(ManagementObject status)
            {
                current++;
                if (!IsMissing(result))
                {
                    return;
                }

                if (current == index)
                {
                    string predictFailure = GetValue(status, "PredictFailure");
                    if (predictFailure.Equals("True", StringComparison.OrdinalIgnoreCase))
                    {
                        result = "SMART 预测失败";
                    }
                    else if (predictFailure.Equals("False", StringComparison.OrdinalIgnoreCase))
                    {
                        result = "SMART 未预测失败";
                    }
                }
            });

            return result;
        }

        private static string GetDiskLifeStatus(string model, int index)
        {
            string mediaType = GetPhysicalDiskValue(model, index, "MediaType");
            string mappedMediaType = MapPhysicalDiskMediaType(mediaType);
            if (Contains(mappedMediaType, "SSD") || Contains(GetDiskInterfaceTypeFromModel(model), "NVMe"))
            {
                return "系统未提供（SSD 寿命百分比通常需厂商 SMART/NVMe 扩展数据）";
            }

            if (!IsMissing(mappedMediaType))
            {
                return mappedMediaType + "，无 SSD 寿命指标";
            }

            return "系统未提供";
        }

        private static string GetDiskInterfaceTypeFromModel(string model)
        {
            if (Contains(model, "NVMe")) return "NVMe / PCIe";
            return "";
        }

        private static string MapPhysicalDiskMediaType(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 3) return "HDD";
            if (code == 4) return "SSD";
            if (code == 5) return "SCM";
            return "";
        }

        private static string GetDiskSerialFromPhysicalDisk(string model, int index)
        {
            return GetPhysicalDiskValue(model, index, "SerialNumber");
        }

        private static PrinterPortInfo GetPrinterPortInfo(string portName)
        {
            PrinterPortInfo info = new PrinterPortInfo();
            info.PortName = SafeText(portName);
            info.IpAddress = ExtractIpAddress(portName);
            info.Protocol = "";

            WmiForEach("SELECT * FROM Win32_TCPIPPrinterPort", delegate(ManagementObject port)
            {
                string name = GetValue(port, "Name");
                if (!name.Equals(portName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                info.IpAddress = FirstNonEmpty(GetValue(port, "HostAddress"), info.IpAddress);
                info.Protocol = GetValue(port, "Protocol");
            });

            return info;
        }

        private static string ExtractIpAddress(string text)
        {
            if (IsMissing(text))
            {
                return "";
            }

            Match match = Regex.Match(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
            return match.Success ? match.Value : "";
        }

        private static string GetPrinterBrand(string printerName, string driverName)
        {
            string text = (SafeText(printerName) + " " + SafeText(driverName)).ToUpperInvariant();
            string[] brands = { "HP", "CANON", "EPSON", "BROTHER", "FUJI", "XEROX", "RICOH", "KYOCERA", "KONICA", "MINOLTA", "LEXMARK", "SAMSUNG", "LENOVO", "PANTUM", "奔图", "联想", "佳能", "爱普生", "兄弟", "惠普", "理光", "京瓷" };
            foreach (string brand in brands)
            {
                if (text.Contains(brand.ToUpperInvariant()))
                {
                    return brand;
                }
            }

            return "系统未提供";
        }

        private static string GetPrinterModel(string printerName, string driverName)
        {
            return FirstNonEmpty(printerName, driverName, "系统未提供");
        }

        private static string GetPrinterDriverVersion(string driverName)
        {
            string version = "";
            WmiForEach("SELECT * FROM Win32_PrinterDriver", delegate(ManagementObject driver)
            {
                if (!IsMissing(version))
                {
                    return;
                }

                string name = GetValue(driver, "Name");
                if (!Contains(name, driverName) && !Contains(driverName, name))
                {
                    return;
                }

                string path = FirstNonEmpty(GetValue(driver, "DriverPath"), GetValue(driver, "InfName"));
                version = GetFileVersion(path);
            });

            return IsMissing(version) ? "系统未提供" : version;
        }

        private static Dictionary<string, string> BuildAudioDriverDateIndex()
        {
            Dictionary<string, string> index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            WmiForEach("SELECT DeviceID, DeviceName, DriverDate FROM Win32_PnPSignedDriver WHERE PNPClass = 'MEDIA'", delegate(ManagementObject driver)
            {
                string date = FormatWmiDate(GetValue(driver, "DriverDate"));
                if (IsMissing(date))
                {
                    return;
                }

                AddDriverDateIndexValue(index, GetValue(driver, "DeviceID"), date);
                AddDriverDateIndexValue(index, GetValue(driver, "DeviceName"), date);
            });

            return index;
        }

        private static void AddDriverDateIndexValue(Dictionary<string, string> index, string key, string value)
        {
            if (IsMissing(key) || IsMissing(value) || index.ContainsKey(key))
            {
                return;
            }

            index.Add(key, value);
        }

        private static string GetPnPSignedDriverDate(string deviceId, string deviceName)
        {
            return GetPnPSignedDriverDate(BuildAudioDriverDateIndex(), deviceId, deviceName);
        }

        private static string GetPnPSignedDriverDate(Dictionary<string, string> driverDateIndex, string deviceId, string deviceName)
        {
            string result;
            if (!IsMissing(deviceId) && driverDateIndex.TryGetValue(deviceId, out result))
            {
                return result;
            }

            if (!IsMissing(deviceName) && driverDateIndex.TryGetValue(deviceName, out result))
            {
                return result;
            }

            if (!IsMissing(deviceName))
            {
                foreach (KeyValuePair<string, string> entry in driverDateIndex)
                {
                    if (Contains(deviceName, entry.Key) || Contains(entry.Key, deviceName))
                    {
                        return entry.Value;
                    }
                }
            }

            return "系统未提供";
        }

        private static string GetFileVersion(string path)
        {
            try
            {
                if (IsMissing(path) || !File.Exists(path))
                {
                    return "";
                }

                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return FirstNonEmpty(info.FileVersion, info.ProductVersion);
            }
            catch
            {
                return "";
            }
        }

        private static string GetPrinterSerialNumber(string printerName)
        {
            string serial = "";
            WmiForEach("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Printer'", delegate(ManagementObject entity)
            {
                if (!IsMissing(serial))
                {
                    return;
                }

                string name = GetValue(entity, "Name");
                if (!Contains(name, printerName) && !Contains(printerName, name))
                {
                    return;
                }

                serial = ExtractLikelySerial(GetValue(entity, "PNPDeviceID"));
            });

            return IsMissing(serial) ? "系统未提供" : serial;
        }

        private static string ExtractLikelySerial(string pnpDeviceId)
        {
            if (IsMissing(pnpDeviceId))
            {
                return "";
            }

            string[] parts = pnpDeviceId.Split('\\', '&');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                string part = parts[i].Trim();
                if (part.Length >= 6 && part.Any(char.IsDigit))
                {
                    return part;
                }
            }

            return "";
        }

        private static string GetPrinterConnectionType(ManagementObject printer, PrinterPortInfo portInfo)
        {
            string network = GetValue(printer, "Network");
            string port = portInfo.PortName;
            if (network.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                return "网络共享打印机";
            }

            if (!IsMissing(portInfo.IpAddress) || Contains(port, "IP_") || Contains(port, "TCP"))
            {
                return "TCP/IP 网络打印机";
            }

            if (Contains(port, "USB"))
            {
                return "USB 本地打印机";
            }

            if (Contains(port, "LPT"))
            {
                return "并口本地打印机";
            }

            return "本地/未知连接";
        }

        private static string GetPrinterStatusText(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 1) return "其他";
            if (code == 2) return "未知";
            if (code == 3) return "空闲";
            if (code == 4) return "正在打印";
            if (code == 5) return "预热";
            if (code == 6) return "停止打印";
            if (code == 7) return "离线";
            return SafeText(codeText);
        }

        private static DdrResult DetectDdrType(ManagementObject mem)
        {
            string smbios = GetValue(mem, "SMBIOSMemoryType");
            string memoryType = GetValue(mem, "MemoryType");
            string speedText = FirstNonEmpty(GetValue(mem, "ConfiguredClockSpeed"), GetValue(mem, "Speed"));
            int speed = ToInt(speedText);

            string type = MapMemoryType(smbios);
            if (!IsMissing(type))
            {
                return new DdrResult(type, "Win32_PhysicalMemory.SMBIOSMemoryType=" + smbios, ItemStatus.Ok);
            }

            type = MapMemoryType(memoryType);
            if (!IsMissing(type))
            {
                return new DdrResult(type, "Win32_PhysicalMemory.MemoryType=" + memoryType, ItemStatus.Ok);
            }

            if (speed >= 4800)
            {
                return new DdrResult("疑似 DDR5", "频率推测: " + speed + " MHz", ItemStatus.Warning);
            }

            if (speed >= 2133)
            {
                return new DdrResult("疑似 DDR4", "频率推测: " + speed + " MHz", ItemStatus.Warning);
            }

            if (speed >= 1066)
            {
                return new DdrResult("疑似 DDR3", "频率推测: " + speed + " MHz", ItemStatus.Warning);
            }

            return new DdrResult("未知 DDR 代数", "WMI 未提供可靠类型或频率", ItemStatus.Warning);
        }

        private static string MapMemoryType(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 20) return "DDR";
            if (code == 21) return "DDR2";
            if (code == 24) return "DDR3";
            if (code == 26) return "DDR4";
            if (code == 34) return "DDR5";
            return "";
        }

        private static SerialResult ResolveBestSerialNumber()
        {
            List<SerialResult> candidates = new List<SerialResult>
            {
                new SerialResult(FirstWmiValue("SELECT * FROM Win32_BaseBoard", "SerialNumber"), "Win32_BaseBoard.SerialNumber"),
                new SerialResult(FirstWmiValue("SELECT * FROM Win32_BIOS", "SerialNumber"), "Win32_BIOS.SerialNumber"),
                new SerialResult(FirstWmiValue("SELECT * FROM Win32_ComputerSystemProduct", "IdentifyingNumber"), "Win32_ComputerSystemProduct.IdentifyingNumber"),
                new SerialResult(FirstWmiValue("SELECT * FROM Win32_ComputerSystemProduct", "UUID"), "Win32_ComputerSystemProduct.UUID")
            };

            foreach (SerialResult candidate in candidates)
            {
                if (!IsInvalidSerial(candidate.Value))
                {
                    candidate.Status = ItemStatus.Ok;
                    return candidate;
                }
            }

            SerialResult fallback = candidates.FirstOrDefault(c => !IsMissing(c.Value));
            if (fallback != null)
            {
                fallback.Status = ItemStatus.Warning;
                fallback.Value = "疑似无效: " + fallback.Value;
                return fallback;
            }

            return new SerialResult("未写入或无法识别", "多源序列号兜底") { Status = ItemStatus.Warning };
        }

        private static void WmiForEach(string query, Action<ManagementObject> action)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject item in collection)
                    {
                        action(item);
                    }
                }
            }
            catch
            {
            }
        }

        private static void WmiForEachInNamespace(string scopePath, string query, Action<ManagementObject> action)
        {
            try
            {
                ManagementScope scope = new ManagementScope(scopePath);
                scope.Connect();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query)))
                using (ManagementObjectCollection collection = searcher.Get())
                {
                    foreach (ManagementObject item in collection)
                    {
                        action(item);
                    }
                }
            }
            catch
            {
            }
        }

        private static string FirstWmiValue(string query, string property)
        {
            string result = "";
            WmiForEach(query, delegate(ManagementObject item)
            {
                if (IsMissing(result))
                {
                    result = GetValue(item, property);
                }
            });
            return result;
        }

        private static string GetValue(ManagementObject item, string property)
        {
            try
            {
                object value = item[property];
                return value == null ? "" : value.ToString().Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string SafeText(string value)
        {
            return IsMissing(value) ? "系统未提供" : value.Trim();
        }

        private static bool IsMissing(string value)
        {
            return string.IsNullOrWhiteSpace(value) || value.Trim() == "未知" || value.Trim() == "未检测到";
        }

        private static ItemStatus GetSerialStatus(string value)
        {
            return IsInvalidSerial(value) ? ItemStatus.Warning : ItemStatus.Ok;
        }

        private static bool IsInvalidSerial(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string normalized = value.Trim().ToUpperInvariant();
            string[] invalidValues =
            {
                "TO BE FILLED BY O.E.M.",
                "TO BE FILLED BY OEM",
                "DEFAULT STRING",
                "NONE",
                "UNKNOWN",
                "N/A",
                "NA",
                "00000000",
                "FFFFFFFF",
                "SYSTEM SERIAL NUMBER",
                "INVALID"
            };

            return invalidValues.Contains(normalized);
        }

        private static string FormatWmiDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8)
            {
                return "";
            }

            try
            {
                DateTime dt = ManagementDateTimeConverter.ToDateTime(raw);
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch
            {
                try
                {
                    int year = int.Parse(raw.Substring(0, 4), CultureInfo.InvariantCulture);
                    int month = int.Parse(raw.Substring(4, 2), CultureInfo.InvariantCulture);
                    int day = int.Parse(raw.Substring(6, 2), CultureInfo.InvariantCulture);
                    return new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                catch
                {
                    return "";
                }
            }
        }

        private static string GetChineseBrandName(string manufacturer)
        {
            if (Contains(manufacturer, "LENOVO") || Contains(manufacturer, "联想")) return "联想";
            if (Contains(manufacturer, "Dell") || Contains(manufacturer, "戴尔")) return "戴尔";
            if (Contains(manufacturer, "HP") || Contains(manufacturer, "Hewlett-Packard") || Contains(manufacturer, "惠普")) return "惠普";
            if (Contains(manufacturer, "ASUS") || Contains(manufacturer, "华硕")) return "华硕";
            if (Contains(manufacturer, "Xiaomi") || Contains(manufacturer, "小米")) return "小米";
            if (Contains(manufacturer, "Huawei") || Contains(manufacturer, "华为")) return "华为";
            if (Contains(manufacturer, "Acer") || Contains(manufacturer, "宏碁")) return "宏碁";
            if (Contains(manufacturer, "Hasee") || Contains(manufacturer, "神州")) return "神州";
            if (Contains(manufacturer, "Inspur") || Contains(manufacturer, "浪潮")) return "浪潮";
            if (Contains(manufacturer, "Sugon") || Contains(manufacturer, "中科曙光")) return "中科曙光";
            if (Contains(manufacturer, "Microsoft") || Contains(manufacturer, "微软")) return "微软";
            if (Contains(manufacturer, "Apple") || Contains(manufacturer, "苹果")) return "苹果";
            return "其他";
        }

        private static string GetPcSystemType(string typeCode)
        {
            int code = ToInt(typeCode);
            if (code == 1) return "台式机";
            if (code == 2) return "笔记本";
            if (code == 3) return "工作站";
            if (code == 4) return "企业服务器";
            if (code == 5) return "小型服务器";
            if (code == 6) return "设备";
            if (code == 7) return "高性能服务器";
            if (code == 8) return "最大型服务器";
            return "系统未提供";
        }

        private static string GetBatteryStatus(string codeText)
        {
            int code = ToInt(codeText);
            if (code == 1) return "正在放电";
            if (code == 2) return "接入交流电";
            if (code == 3) return "充满电";
            if (code == 4) return "电量低";
            if (code == 5) return "严重低电量";
            if (code == 6) return "正在充电";
            if (code == 7) return "充电且高电量";
            if (code == 8) return "充电且低电量";
            if (code == 9) return "充电且严重低电量";
            if (code == 10) return "状态未知";
            if (code == 11) return "部分充电";
            return SafeText(codeText);
        }

        private static string GetOfficialWarrantyUrl(string manufacturer)
        {
            if (Contains(manufacturer, "LENOVO") || Contains(manufacturer, "联想")) return "https://pcsupport.lenovo.com/";
            if (Contains(manufacturer, "Dell") || Contains(manufacturer, "戴尔")) return "https://www.dell.com/support/home/zh-cn";
            if (Contains(manufacturer, "HP") || Contains(manufacturer, "惠普")) return "https://support.hp.com/cn-zh/check-warranty";
            if (Contains(manufacturer, "ASUS") || Contains(manufacturer, "华硕")) return "https://www.asus.com.cn/support/warranty-status/";
            if (Contains(manufacturer, "Xiaomi") || Contains(manufacturer, "小米")) return "https://www.mi.com/service/imei";
            if (Contains(manufacturer, "Huawei") || Contains(manufacturer, "华为")) return "https://consumer.huawei.com/cn/support/warranty-query/";
            if (Contains(manufacturer, "Acer") || Contains(manufacturer, "宏碁")) return "https://www.acer.com.cn/support/warranty";
            return "";
        }

        private static RuleHit ContainsAny(string text, IEnumerable<string> keywords)
        {
            foreach (string keyword in keywords)
            {
                if (Contains(text, keyword))
                {
                    return new RuleHit(true, keyword);
                }
            }

            return new RuleHit(false, "");
        }

        private static bool ContainsAnyText(string text, params string[] keywords)
        {
            foreach (string keyword in keywords)
            {
                if (Contains(text, keyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(string text, string keyword)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            return text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!IsMissing(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        private static string AppendUnit(string value, string unit)
        {
            if (IsMissing(value))
            {
                return "系统未提供";
            }

            return value.Trim() + " " + unit;
        }

        private static string JoinArray(object value)
        {
            if (value == null)
            {
                return "系统未提供";
            }

            string[] array = value as string[];
            if (array == null || array.Length == 0)
            {
                return "系统未提供";
            }

            return string.Join(", ", array);
        }

        private static int ToInt(string value)
        {
            int result;
            return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static double ToDouble(string value)
        {
            double result;
            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ? result : 0;
        }

        private static void ExportReport(List<ReportSection> sections, string title, bool includeSource)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                ReportSaveResult result = SaveReportBundle(sections, title, includeSource, desktop);
                WriteSuccess("报告已保存，共生成 {0} 个文件:", result.Paths.Count);
                foreach (string savedPath in result.Paths)
                {
                    Console.WriteLine("  " + savedPath);
                }

                Console.Write("是否打开报告所在位置? (Y/N): ");
                string answer = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(answer) && answer.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start("explorer.exe", "/select,\"" + result.PrimaryPath + "\"");
                }
            }
            catch (Exception ex)
            {
                WriteError("报告导出失败: {0}", ex.Message);
            }
        }

        private static ReportSaveResult SaveReportBundle(List<ReportSection> sections, string title, bool includeSource, string targetDirectory)
        {
            return SaveReportBundle(sections, title, includeSource, targetDirectory, "");
        }

        private static ReportSaveResult SaveReportBundle(List<ReportSection> sections, string title, bool includeSource, string targetDirectory, string customIdentity)
        {
            Directory.CreateDirectory(targetDirectory);

            string generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string baseName = string.IsNullOrWhiteSpace(customIdentity)
                ? SanitizeFileName("设备信息采集_" + BuildReportIdentity(sections) + "_" + timestamp)
                : SanitizeFileName(customIdentity);
            string txtPath = Path.Combine(targetDirectory, baseName + ".txt");
            string jsonPath = Path.Combine(targetDirectory, baseName + ".json");
            string csvPath = Path.Combine(targetDirectory, baseName + ".csv");

            File.WriteAllText(txtPath, BuildReportText(sections, title, includeSource), new UTF8Encoding(true));
            File.WriteAllText(jsonPath, BuildReportJson(sections, title, generatedAt), new UTF8Encoding(true));
            File.WriteAllText(csvPath, BuildReportSummaryCsv(sections, title, generatedAt), new UTF8Encoding(true));

            ReportSaveResult result = new ReportSaveResult(txtPath);
            result.Paths.Add(txtPath);
            result.Paths.Add(jsonPath);
            result.Paths.Add(csvPath);
            return result;
        }

        private static ReportSaveResult SaveReportBundleToWebDav(List<ReportSection> sections, string title, bool includeSource, CollectTarget target, string remoteDirectory, string customIdentity)
        {
            string generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string baseName = string.IsNullOrWhiteSpace(customIdentity)
                ? SanitizeFileName("设备信息采集_" + BuildReportIdentity(sections) + "_" + timestamp)
                : SanitizeFileName(customIdentity);

            EnsureWebDavDirectoryPath(target, remoteDirectory);

            string txtName = baseName + ".txt";
            string jsonName = baseName + ".json";
            string csvName = baseName + ".csv";

            UploadTextToWebDav(target, CombineWebDavPath(remoteDirectory, txtName), BuildReportText(sections, title, includeSource));
            UploadTextToWebDav(target, CombineWebDavPath(remoteDirectory, jsonName), BuildReportJson(sections, title, generatedAt));
            UploadTextToWebDav(target, CombineWebDavPath(remoteDirectory, csvName), BuildReportSummaryCsv(sections, title, generatedAt));

            ReportSaveResult result = new ReportSaveResult(BuildWebDavDisplayPath(target, remoteDirectory, txtName));
            result.Paths.Add(BuildWebDavDisplayPath(target, remoteDirectory, txtName));
            result.Paths.Add(BuildWebDavDisplayPath(target, remoteDirectory, jsonName));
            result.Paths.Add(BuildWebDavDisplayPath(target, remoteDirectory, csvName));
            return result;
        }

        private static string BuildReportIdentity(List<ReportSection> sections)
        {
            string manufacturer = FindItemValue(sections, "制造商");
            string model = FindItemValue(sections, "型号");
            string identity = FirstNonEmpty(Environment.MachineName, manufacturer, model);
            if (!IsMissing(manufacturer) || !IsMissing(model))
            {
                identity = FirstNonEmpty(manufacturer, Environment.MachineName) + "_" + FirstNonEmpty(model, Environment.MachineName);
            }

            return identity;
        }

        private static string FindItemValue(List<ReportSection> sections, string name)
        {
            foreach (ReportSection section in sections)
            {
                InfoItem item = section.Items.FirstOrDefault(i => i.Name == name);
                if (item != null && !IsMissing(item.Value))
                {
                    return item.Value;
                }
            }

            return "";
        }

        private static string BuildReportText(List<ReportSection> sections, string title, bool includeSource)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(AppName1);
            sb.AppendLine("报告类型: " + title);
            sb.AppendLine("电脑名称: " + Environment.MachineName);
            sb.AppendLine("生成时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('=', 70));
            sb.AppendLine();

            foreach (ReportSection section in sections)
            {
                sb.AppendLine("========== " + section.Title + " ==========");
                foreach (InfoItem item in section.Items)
                {
                    if (item.IsBlankLine)
                    {
                        sb.AppendLine();
                        continue;
                    }

                    if (item.IsDivider)
                    {
                        sb.AppendLine();
                        sb.AppendLine("---- " + item.Name + " ----");
                        continue;
                    }

                    if (item.IsNumberedLine)
                    {
                        sb.AppendLine(item.Name + " " + item.Value);
                        continue;
                    }

                    sb.AppendLine(item.Name + ": " + item.Value + " [" + item.StatusText + "]");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildReportJson(List<ReportSection> sections, string title, string generatedAt)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"appName\": " + JsonString(AppName1) + ",");
            sb.AppendLine("  \"reportTitle\": " + JsonString(title) + ",");
            sb.AppendLine("  \"computerName\": " + JsonString(Environment.MachineName) + ",");
            sb.AppendLine("  \"generatedAt\": " + JsonString(generatedAt) + ",");
            sb.AppendLine("  \"sections\": [");

            for (int i = 0; i < sections.Count; i++)
            {
                ReportSection section = sections[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"title\": " + JsonString(section.Title) + ",");
                sb.AppendLine("      \"items\": [");
                for (int j = 0; j < section.Items.Count; j++)
                {
                    InfoItem item = section.Items[j];
                    sb.Append("        {");
                    sb.Append("\"name\": " + JsonString(item.Name));
                    sb.Append(", \"value\": " + JsonString(item.Value));
                    sb.Append(", \"status\": " + JsonString(item.StatusText));
                    sb.Append(", \"isDivider\": " + (item.IsDivider ? "true" : "false"));
                    sb.Append(", \"isBlankLine\": " + (item.IsBlankLine ? "true" : "false"));
                    sb.Append(", \"isNumberedLine\": " + (item.IsNumberedLine ? "true" : "false"));
                    sb.Append("}");
                    if (j < section.Items.Count - 1)
                    {
                        sb.Append(",");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("      ]");
                sb.Append("    }");
                if (i < sections.Count - 1)
                {
                    sb.Append(",");
                }
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildReportSummaryCsv(List<ReportSection> sections, string title, string generatedAt)
        {
            List<string> headers = new List<string>
            {
                "采集时间",
                "报告类型",
                "部门",
                "使用人员姓名",
                "使用位置",
                "计算机名",
                "品牌",
                "型号",
                "设备类型",
                "序列号",
                "预计出厂日期",
                "操作系统",
                "系统版本",
                "产品激活状态",
                "CPU",
                "内存",
                "内存插槽占用",
                "硬盘驱动器",
                "网卡信息",
                "笔记本网卡核实提示",
                "信创判断结果",
                "信创置信度"
            };

            List<string> values = new List<string>
            {
                generatedAt,
                title,
                FindItemValueByNames(sections, "部门"),
                FindItemValueByNames(sections, "使用人员姓名"),
                FindItemValueByNames(sections, "使用位置"),
                FirstNonEmpty(FindItemValueByNames(sections, "计算机名"), Environment.MachineName),
                FindItemValueByNames(sections, "制造商", "品牌"),
                FindItemValueByNames(sections, "型号"),
                FindItemValueByNames(sections, "设备类型"),
                FindItemValueByNames(sections, "主机序列号", "序列号", "主板序列号"),
                FindItemValueByNames(sections, "预计出厂日期", "出厂日期参考"),
                FindItemValueByNames(sections, "操作系统名称", "操作系统"),
                FindItemValueByNames(sections, "系统版本"),
                FindItemValueByNames(sections, "产品激活状态"),
                FindItemValueByNames(sections, "CPU1 名称", "CPU"),
                FindItemValueByNames(sections, "总内存"),
                FindItemValueByNames(sections, "内存插槽占用"),
                BuildDiskSummaryForCsv(sections),
                BuildNetworkSummaryForCsv(sections),
                FindItemValueByNames(sections, "笔记本网卡核实提示"),
                FindItemValueByNames(sections, "判断结果"),
                FindItemValueByNames(sections, "线索分值", "置信度")
            };

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ToCsvLine(headers));
            sb.AppendLine(ToCsvLine(values));
            return sb.ToString();
        }

        private static string BuildDiskSummaryForCsv(List<ReportSection> sections)
        {
            string physical = FindItemValueByNames(sections, "物理硬盘驱动器数量");
            string logical = FindItemValueByNames(sections, "逻辑磁盘数量");
            List<string> parts = new List<string>();
            if (!IsMissing(physical))
            {
                parts.Add("物理硬盘驱动器 " + physical);
            }

            if (!IsMissing(logical))
            {
                parts.Add("逻辑磁盘 " + logical);
            }

            return string.Join("；", parts.ToArray());
        }

        private static string BuildNetworkSummaryForCsv(List<ReportSection> sections)
        {
            List<string> adapters = new List<string>();
            int index = 0;
            WmiForEach("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True", delegate(ManagementObject nic)
            {
                string description = SafeText(GetValue(nic, "Description"));
                string mac = SafeText(GetValue(nic, "MACAddress"));
                string ip = SafeText(JoinArray(nic["IPAddress"]));
                if (IsMissing(mac) && IsMissing(ip))
                {
                    return;
                }

                index++;
                List<string> parts = new List<string>();
                parts.Add("网卡" + index.ToString(CultureInfo.InvariantCulture));
                parts.Add(description);
                parts.Add("MAC " + mac);
                parts.Add("IP " + ip);
                adapters.Add(string.Join(" | ", parts.ToArray()));
            });

            if (adapters.Count == 0)
            {
                return FindItemValueByNames(sections, "检测到网卡设备数量");
            }

            return adapters.Count.ToString(CultureInfo.InvariantCulture) + " 个；" + string.Join("；", adapters.ToArray());
        }

        private static void MergeCollectedReports(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                WriteWarning("归集目录不能为空。");
                return;
            }

            try
            {
                if (!Directory.Exists(sourceDirectory))
                {
                    WriteWarning("归集目录不存在: {0}", sourceDirectory);
                    return;
                }

                string[] files = Directory.GetFiles(sourceDirectory, "*.csv", SearchOption.AllDirectories)
                    .Where(file => !Path.GetFileName(file).StartsWith("设备信息统计表_", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (files.Length == 0)
                {
                    WriteWarning("未找到可汇总的采集 CSV 文件。");
                    return;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                string header = null;
                List<string> rows = new List<string>();
                foreach (string file in files)
                {
                    string[] lines = File.ReadAllLines(file, Encoding.UTF8);
                    if (lines.Length < 2)
                    {
                        continue;
                    }

                    if (header == null)
                    {
                        header = lines[0];
                    }

                    for (int i = 1; i < lines.Length; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(lines[i]))
                        {
                            rows.Add(lines[i]);
                        }
                    }
                }

                if (rows.Count == 0)
                {
                    WriteWarning("采集 CSV 文件中没有可汇总的数据行。");
                    return;
                }

                string outputPath = Path.Combine(sourceDirectory, "设备信息统计表_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(header);
                foreach (string row in rows)
                {
                    sb.AppendLine(row);
                }
                File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(true));
                WriteSuccess("设备信息统计表已生成: {0}", outputPath);
                Console.WriteLine("汇总设备数量: {0}", rows.Count);
            }
            catch (Exception ex)
            {
                WriteError("汇总统计失败: {0}", ex.Message);
            }
        }

        private static string FindItemValueByNames(List<ReportSection> sections, params string[] names)
        {
            foreach (string name in names)
            {
                string value = FindItemValue(sections, name);
                if (!IsMissing(value))
                {
                    return value;
                }
            }

            foreach (string name in names)
            {
                foreach (ReportSection section in sections)
                {
                    InfoItem item = section.Items.FirstOrDefault(i => !i.IsDivider && !i.IsBlankLine && i.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (item != null && !IsMissing(item.Value))
                    {
                        return item.Value;
                    }
                }
            }

            return "";
        }

        private static string ToCsvLine(IEnumerable<string> values)
        {
            return string.Join(",", values.Select(EscapeCsvValue).ToArray());
        }

        private static string EscapeCsvValue(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string JsonString(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in value)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (char.IsControl(c)) sb.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }

        private static void WriteHeader(string title)
        {
            BeginColor(ConsoleColor.Magenta, true);
            int width = GetHeaderWidth(title);
            Console.WriteLine(new string('═', width));
            Console.WriteLine(BuildCenteredHeaderLine(title, width));
            Console.WriteLine(new string('═', width));
            ResetTextColor();
            Console.WriteLine();
        }

        private static void WriteFeatureIntro()
        {
            BeginColor(ConsoleColor.Magenta, true);
            Console.WriteLine("  【功能亮点】");
            ResetTextColor();
            WriteFeatureLine("◆", "基础摘要", "快速提取品牌、序列号、出厂日期、系统、CPU、网卡等资产登记核心字段。");
            WriteFeatureLine("■", "完整检测", "覆盖主板 BIOS、内存、硬盘、显卡、音频、显示器、打印机等硬件明细。");
            WriteFeatureLine("✓", "复核增强", "主板序列号兜底、内存 DDR 代数、笔记本网卡状态与设备类型规则联动判断。");
            WriteFeatureLine("★", "信创判定", "按品牌、型号、CPU、操作系统四类规则评分，输出命中线索并提示人工复核。");
            WriteFeatureLine("▣", "批量归集", "支持 txt/json/csv 报告上传、部门与门牌号归档、共享目录统计表汇总。");
            WriteFeatureLine("→", "易用体验", "彩色终端、动态进度条、实时检测时间和菜单化交互，降低一线采集成本。");
            Console.WriteLine();
        }

        private static void WriteFeatureLine(string icon, string title, string description)
        {
            Console.Write("  ");
            BeginColor(ConsoleColor.Blue, true);
            Console.Write(PadRightByDisplayWidth(icon, 4));
            ResetTextColor();
            BeginColor(ConsoleColor.White, true);
            Console.Write(PadRightByDisplayWidth(title, 10));
            ResetTextColor();
            BeginColor(ConsoleColor.Gray);
            Console.WriteLine(description);
            ResetTextColor();
        }

        private static int GetHeaderWidth(string title)
        {
            int titleWidth = GetDisplayWidth(title);
            int desired = Math.Max(60, titleWidth + 12);
            try
            {
                desired = Math.Min(Math.Max(60, Console.WindowWidth - 1), desired);
                if (titleWidth + 8 > desired)
                {
                    desired = Math.Min(Math.Max(titleWidth + 8, 60), Math.Max(60, Console.WindowWidth - 1));
                }
            }
            catch
            {
            }

            return desired;
        }

        private static string BuildCenteredHeaderLine(string title, int width)
        {
            title = title ?? "";
            int titleWidth = GetDisplayWidth(title);
            int available = Math.Max(0, width - titleWidth);
            int left = available / 2;
            int right = available - left;
            return new string(' ', left) + title + new string(' ', right);
        }

        private static void ClearConsole()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    Console.Clear();
                }
            }
            catch
            {
            }
        }

        private static void WriteMenuItem(string key, string name, string description)
        {
            BeginColor(ConsoleColor.Blue, true);
            Console.Write("  {0,-4}", key);
            ResetTextColor();
            BeginColor(ConsoleColor.White, true);
            Console.Write(PadRightByDisplayWidth(name, 22));
            ResetTextColor();
            BeginColor(ConsoleColor.DarkGray);
            Console.WriteLine(description);
            ResetTextColor();
        }

        private static void WriteSection(ReportSection section, bool showSource)
        {
            BeginColor(ConsoleColor.Cyan, true);
            Console.WriteLine();
            Console.WriteLine("========== {0} ==========", section.Title);
            ResetTextColor();

            int nameWidth = section.Items.Count == 0 ? 18 : Math.Max(18, section.Items.Where(item => !item.IsNumberedLine).Select(item => GetDisplayWidth(item.Name)).DefaultIfEmpty(18).Max());
            int numberedWidth = section.Items.Where(item => item.IsNumberedLine).Select(item => GetDisplayWidth(item.Name)).DefaultIfEmpty(2).Max();
            foreach (InfoItem item in section.Items)
            {
                if (item.IsBlankLine)
                {
                    Console.WriteLine();
                    continue;
                }

                if (item.IsDivider)
                {
                    BeginColor(ConsoleColor.DarkCyan);
                    Console.WriteLine();
                    Console.WriteLine("  ---- {0} ----", item.Name);
                    ResetTextColor();
                    continue;
                }

                if (item.IsNumberedLine)
                {
                    Console.Write("  ");
                    BeginColor(ConsoleColor.Green);
                    Console.Write(PadRightByDisplayWidth(item.Name, numberedWidth + 1));
                    ResetTextColor();
                    SetStatusColor(item.Status);
                    Console.Write(GetStatusIcon(item.Status) + item.Value);
                    ResetTextColor();
                    Console.WriteLine();
                    continue;
                }

                Console.Write("  " + PadRightByDisplayWidth(item.Name, nameWidth) + ": ");
                SetStatusColor(item.Status);
                Console.Write(GetStatusIcon(item.Status) + item.Value);
                ResetTextColor();

                Console.WriteLine();
            }
        }

        private static string PadRightByDisplayWidth(string text, int targetWidth)
        {
            text = text ?? "";
            int width = GetDisplayWidth(text);
            if (width >= targetWidth)
            {
                return text;
            }

            return text + new string(' ', targetWidth - width);
        }

        private static int GetDisplayWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int width = 0;
            foreach (char c in text)
            {
                width += IsWideChar(c) ? 2 : 1;
            }

            return width;
        }

        private static bool IsWideChar(char c)
        {
            return c >= 0x2E80;
        }

        private static void SetStatusColor(ItemStatus status)
        {
            if (status == ItemStatus.Ok)
            {
                BeginColor(ConsoleColor.Green);
            }
            else if (status == ItemStatus.Warning)
            {
                BeginColor(ConsoleColor.Yellow);
            }
            else
            {
                BeginColor(ConsoleColor.Red);
            }
        }

        private static string GetStatusIcon(ItemStatus status)
        {
            if (status == ItemStatus.Ok)
            {
                return "✓ ";
            }

            if (status == ItemStatus.Warning)
            {
                return "⚠ ";
            }

            return "✗ ";
        }

        private static void WriteInfo(string format, params object[] args)
        {
            WriteColoredLine(ConsoleColor.Gray, format, args);
        }

        private static void WriteSuccess(string format, params object[] args)
        {
            WriteColoredLine(ConsoleColor.Green, "✓ " + format, args);
        }

        private static void WriteWarning(string format, params object[] args)
        {
            WriteColoredLine(ConsoleColor.Yellow, "⚠ " + format, args);
        }

        private static void WriteError(string format, params object[] args)
        {
            WriteColoredLine(ConsoleColor.Red, "✗ " + format, args);
        }

        private static void WriteColoredLine(ConsoleColor color, string format, params object[] args)
        {
            ShowCursorAfterProgress();
            BeginColor(color);
            Console.WriteLine(format, args);
            ResetTextColor();
        }

        private static void WritePrompt(string text)
        {
            ShowCursorAfterProgress();
            BeginColor(ConsoleColor.Yellow, true);
            Console.Write(text);
            ResetTextColor();
        }

        private static void BeginColor(ConsoleColor color)
        {
            BeginColor(color, false);
        }

        private static void BeginColor(ConsoleColor color, bool bold)
        {
            if (_supportsAnsi)
            {
                Console.Write((bold ? AnsiBold : "") + AnsiCode(color));
                return;
            }

            Console.ForegroundColor = color;
        }

        private static void ResetTextColor()
        {
            if (_supportsAnsi)
            {
                Console.Write(AnsiReset);
                return;
            }

            Console.ResetColor();
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine("按任意键继续...");
            ReadKeySafe();
        }

        private static ConsoleKeyInfo ReadKeySafe()
        {
            try
            {
                return Console.ReadKey(true);
            }
            catch
            {
                return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            }
        }
    }

    internal class ProgressDisplay : IDisposable
    {
        private readonly int totalSteps;
        private readonly bool interactive;
        private readonly object syncRoot = new object();
        private Thread worker;
        private volatile bool running;
        private int currentStep;
        private int completedSteps;
        private string currentModule;
        private int spinnerIndex;
        private bool moduleCompleted;
        private int lastRenderLength;

        public ProgressDisplay(int totalSteps, int completedBefore)
        {
            this.totalSteps = Math.Max(1, totalSteps);
            interactive = Program.IsInteractiveTerminal;
            currentModule = "\u51c6\u5907\u68c0\u6d4b";
            completedSteps = Math.Max(0, Math.Min(this.totalSteps, completedBefore));
        }

        public void Start()
        {
            if (!interactive)
            {
                return;
            }

            try
            {
                running = true;
                Program.HideCursorForProgress();
                worker = new Thread(Animate);
                worker.IsBackground = true;
                worker.Start();
            }
            catch
            {
                running = false;
            }
        }

        public void BeginStep(int step, string moduleName)
        {
            lock (syncRoot)
            {
                currentStep = step;
                currentModule = moduleName;
                moduleCompleted = false;
            }

            if (!interactive)
            {
                int percent = (int)Math.Round((step - 1) * 100.0 / totalSteps);
                Console.WriteLine("\u6b63\u5728\u68c0\u6d4b [{0}/{1}] {2}... \u603b\u8fdb\u5ea6: {3}%",
                    step,
                    totalSteps,
                    moduleName,
                    percent);
            }
        }

        public void CompleteStep(int step)
        {
            lock (syncRoot)
            {
                completedSteps = Math.Max(completedSteps, step);
                moduleCompleted = true;
            }

            if (!interactive)
            {
                int percent = (int)Math.Round(completedSteps * 100.0 / totalSteps);
                Console.WriteLine("\u68c0\u6d4b\u5b8c\u6210 [{0}/{1}] \u603b\u8fdb\u5ea6: {2}%",
                    step,
                    totalSteps,
                    percent);
            }
        }

        public void Stop()
        {
            if (!interactive)
            {
                return;
            }

            running = false;
            if (worker != null)
            {
                worker.Join(500);
            }

            Render();
            Program.ShowCursorAfterProgress();
            SafeWriteLine();
        }

        public void Dispose()
        {
            try
            {
                if (running)
                {
                    Stop();
                }
            }
            catch
            {
            }
        }

        private void Animate()
        {
            while (running)
            {
                Render();
                Thread.Sleep(120);
            }
        }

        private void Render()
        {
            try
            {
                int step;
                int completed;
                string module;
                bool completedModule;
                lock (syncRoot)
                {
                    step = currentStep;
                    completed = completedSteps;
                    module = currentModule;
                    completedModule = moduleCompleted;
                }

                double modulePercent = completedModule ? 100.0 : GetAnimatedModulePercent();
                double totalPercent = completedModule
                    ? completed * 100.0 / totalSteps
                    : ((completed + (modulePercent / 100.0)) * 100.0) / totalSteps;
                totalPercent = Math.Min(100.0, totalPercent);
                string spinner = GetSpinner();
                int consoleWidth = GetConsoleWidth();
                string moduleText = TrimToWidth(module, Math.Max(8, Math.Min(18, consoleWidth - 46)));
                string prefix = string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0}] {1} ({2}/{3}) \u603b:{4:0.0}% \u6a21\u5757:",
                    spinner,
                    moduleText,
                    Math.Max(1, step),
                    totalSteps,
                    totalPercent);
                string line = prefix + BuildDismProgressBar(modulePercent, Math.Max(24, consoleWidth - prefix.Length - 6));
                WriteSingleProgressLine(line);
            }
            catch
            {
            }
        }

        private void WriteSingleProgressLine(string line)
        {
            try
            {
                int width = GetConsoleWidth();
                line = TrimToWidth(line, width);
                int clearLength = Math.Max(0, lastRenderLength - line.Length);
                string outputLine = Program.SupportsAnsiOutput ? Program.Colorize(line, ConsoleColor.Blue) : line;
                Console.Write("\r" + outputLine + new string(' ', clearLength));
                lastRenderLength = line.Length;
            }
            catch
            {
            }
        }

        private static void SafeWriteLine()
        {
            try
            {
                Console.WriteLine();
            }
            catch
            {
            }
        }

        private int GetAnimatedModulePercent()
        {
            int frame = spinnerIndex % 18;
            return 10 + (int)Math.Round(frame * 80.0 / 17);
        }

        private string GetSpinner()
        {
            char[] frames = { '|', '/', '-', '\\' };
            char frame = frames[spinnerIndex % frames.Length];
            spinnerIndex++;
            return frame.ToString();
        }

        private static string BuildDismProgressBar(double percent, int width)
        {
            percent = Math.Max(0.0, Math.Min(100.0, percent));
            width = Math.Max(24, Math.Min(width, 54));

            string value = percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            int innerWidth = Math.Max(10, width - value.Length - 3);
            char[] chars = Enumerable.Repeat('░', innerWidth).ToArray();
            int filled = (int)Math.Round(percent / 100.0 * innerWidth);

            for (int i = 0; i < filled && i < chars.Length; i++)
            {
                chars[i] = '█';
            }

            return value.PadLeft(6) + " [" + new string(chars) + "]";
        }

        private static int GetConsoleWidth()
        {
            try
            {
                return Math.Max(40, Console.WindowWidth);
            }
            catch
            {
                return 80;
            }
        }

        private static string TrimToWidth(string text, int width)
        {
            if (string.IsNullOrEmpty(text) || text.Length < width - 1)
            {
                return text ?? "";
            }

            return text.Substring(0, Math.Max(0, width - 4)) + "...";
        }
    }

    internal class DetectionModule
    {
        public DetectionModule(string key, string name, string description, Func<ReportSection> collect)
        {
            Key = key;
            Name = name;
            Description = description;
            Collect = collect;
        }

        public string Key { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Func<ReportSection> Collect { get; private set; }
    }

    internal class ReportSaveResult
    {
        public ReportSaveResult(string primaryPath)
        {
            PrimaryPath = primaryPath;
            Paths = new List<string>();
        }

        public string PrimaryPath { get; private set; }
        public List<string> Paths { get; private set; }
    }

    internal class CollectTarget
    {
        public string Address { get; set; }
        public bool IsWebDav { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
    }

    internal class NetworkAdapterStats
    {
        public int WirelessTotal { get; set; }
        public int WirelessEnabled { get; set; }
        public int WiredTotal { get; set; }
        public int WiredEnabled { get; set; }
        public int WiredDisabled { get; set; }
    }

    internal class ReportSection
    {
        public ReportSection(string title)
        {
            Title = title;
            Items = new List<InfoItem>();
        }

        public string Title { get; private set; }
        public List<InfoItem> Items { get; private set; }

        public void Add(string name, string value, string source, ItemStatus status = ItemStatus.Ok)
        {
            string actualValue = string.IsNullOrWhiteSpace(value) ? "系统未提供" : value.Trim();
            ItemStatus actualStatus = string.IsNullOrWhiteSpace(value) ? ItemStatus.Warning : status;
            Items.Add(new InfoItem(name, actualValue, source, actualStatus, false, false, false));
        }

        public void Insert(int index, string name, string value, string source, ItemStatus status = ItemStatus.Ok)
        {
            string actualValue = string.IsNullOrWhiteSpace(value) ? "系统未提供" : value.Trim();
            ItemStatus actualStatus = string.IsNullOrWhiteSpace(value) ? ItemStatus.Warning : status;
            Items.Insert(index, new InfoItem(name, actualValue, source, actualStatus, false, false, false));
        }

        public void AddDivider(string title)
        {
            Items.Add(new InfoItem(title, "", "", ItemStatus.Ok, true, false, false));
        }

        public void AddBlankLine()
        {
            Items.Add(new InfoItem("", "", "", ItemStatus.Ok, false, true, false));
        }

        public void AddNumberedLine(string number, string value)
        {
            Items.Add(new InfoItem(number, string.IsNullOrWhiteSpace(value) ? "系统未提供" : value.Trim(), "", ItemStatus.Ok, false, false, true));
        }

        public void EnsureNotEmpty(string message)
        {
            if (Items.Count == 0)
            {
                Add("检测状态", message, "WMI", ItemStatus.Warning);
            }
        }
    }

    internal class InfoItem
    {
        public InfoItem(string name, string value, string source, ItemStatus status, bool isDivider, bool isBlankLine, bool isNumberedLine)
        {
            Name = name;
            Value = value;
            Source = source;
            Status = status;
            IsDivider = isDivider;
            IsBlankLine = isBlankLine;
            IsNumberedLine = isNumberedLine;
        }

        public string Name { get; private set; }
        public string Value { get; private set; }
        public string Source { get; private set; }
        public ItemStatus Status { get; private set; }
        public bool IsDivider { get; private set; }
        public bool IsBlankLine { get; private set; }
        public bool IsNumberedLine { get; private set; }

        public string StatusText
        {
            get
            {
                if (Status == ItemStatus.Ok) return "正常";
                if (Status == ItemStatus.Warning) return "需核实";
                return "异常";
            }
        }
    }

    internal enum ItemStatus
    {
        Ok,
        Warning,
        Error
    }

    internal class DdrResult
    {
        public DdrResult(string type, string source, ItemStatus status)
        {
            Type = type;
            Source = source;
            Status = status;
        }

        public string Type { get; private set; }
        public string Source { get; private set; }
        public ItemStatus Status { get; private set; }
    }

    internal class SerialResult
    {
        public SerialResult(string value, string source)
        {
            Value = string.IsNullOrWhiteSpace(value) ? "系统未提供" : value.Trim();
            Source = source;
            Status = ItemStatus.Warning;
        }

        public string Value { get; set; }
        public string Source { get; private set; }
        public ItemStatus Status { get; set; }
    }

    internal class RuleHit
    {
        public RuleHit(bool hit, string keyword)
        {
            Hit = hit;
            Keyword = keyword;
        }

        public bool Hit { get; private set; }
        public string Keyword { get; private set; }
    }

    internal class XinChuangAssessment
    {
        public XinChuangAssessment()
        {
            BrandHit = new RuleHit(false, "");
            CpuHit = new RuleHit(false, "");
            OsHit = new RuleHit(false, "");
            ModelHit = new RuleHit(false, "");
            Result = "";
            Status = ItemStatus.Warning;
        }

        public RuleHit BrandHit { get; set; }
        public RuleHit CpuHit { get; set; }
        public RuleHit OsHit { get; set; }
        public RuleHit ModelHit { get; set; }
        public int Score { get; set; }
        public string Result { get; set; }
        public ItemStatus Status { get; set; }
    }

    internal class PrinterPortInfo
    {
        public string PortName { get; set; }
        public string IpAddress { get; set; }
        public string Protocol { get; set; }
    }
}
