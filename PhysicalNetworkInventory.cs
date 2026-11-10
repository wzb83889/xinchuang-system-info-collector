using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SystemInfoTest
{
    internal sealed class PhysicalNic
    {
        public string DeviceId = "", Guid = "", Name = "", Description = "", Kind = "未知类型";
        public string CurrentMac = "", PermanentMac = "", CurrentSource = "", PermanentSource = "";
        public string State = "状态未知", Ip = "", Mask = "", Gateway = "";
        public bool Disabled, Connected, DriverError;
        public uint InterfaceIndex;
        public string Mac { get { return PermanentMac.Length > 0 ? PermanentMac : CurrentMac; } }
        public string MacSource { get { return PermanentMac.Length > 0 ? PermanentSource : CurrentSource; } }
        public ItemStatus Status { get { return Mac.Length == 0 || Disabled || !Connected || DriverError || Ip.Length == 0 ? ItemStatus.Warning : ItemStatus.Ok; } }
        public string Summary
        {
            get
            {
                return Name + " [" + Kind + "] | " + Details;
            }
        }
        public string CsvSummary { get { return Kind + " | " + Name + " | " + Details; } }
        private string Details { get { return State + " | MAC: " + Display(Mac) + " | 永久MAC: " + Display(PermanentMac) + " | 当前MAC: " + Display(CurrentMac) + " | IP: " + (Connected && !Disabled ? Display(Ip) : "无当前连接地址") + " | 来源: " + Display(MacSource); } }
        private static string Display(string s) { return string.IsNullOrEmpty(s) ? "未能读取" : s; }
    }

    internal sealed class PhysicalNetworkSnapshot
    {
        public readonly List<PhysicalNic> Adapters = new List<PhysicalNic>();
        public readonly List<string> BlockingIssues = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public bool InventoryVerified;
        public bool Complete { get { return InventoryVerified && Adapters.Count > 0 && Adapters.All(n => n.Mac.Length > 0) && BlockingIssues.Count == 0; } }
        public string Summary { get { return Adapters.Count + " 个；" + string.Join("；", Adapters.Select(n => n.Summary).ToArray()); } }
    }

    internal static class PhysicalNetworkInventory
    {
        // Identity is the present PnP device, never MAC, name, or the transient interface index.
        internal static string Identity(string value) { return (value ?? "").Trim().Trim('{', '}').ToUpperInvariant(); }
        internal static string NormalizeMac(string value)
        {
            string s = Regex.Replace(value ?? "", "[:.\\-\\s]", "").ToUpperInvariant();
            if (!Regex.IsMatch(s, "^[0-9A-F]{12}$") || s == "000000000000" || s == "FFFFFFFFFFFF") return "";
            if ((int.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 1) != 0) return "";
            return string.Join(":", Enumerable.Range(0, 6).Select(i => s.Substring(i * 2, 2)).ToArray());
        }

        internal static bool IsPhysical(string id, string description, string service, bool hardware)
        {
            string key = Identity(id);
            string text = (description ?? "") + " " + (service ?? "");
            if (key.StartsWith("ROOT\\", StringComparison.Ordinal) || key.StartsWith("SWD\\", StringComparison.Ordinal) ||
                key.StartsWith("VMBUS\\", StringComparison.Ordinal) || key.StartsWith("BTH\\", StringComparison.Ordinal) || key.StartsWith("BTHENUM\\", StringComparison.Ordinal)) return false;
            if (Regex.IsMatch(text, @"VMware|Hyper-V|VirtualBox|Wi-Fi Direct Virtual|Hosted Network Virtual|WAN Miniport|Kernel Debug|Teredo|Npcap Loopback|aTrust VNIC|Sangfor.*VNIC|TAP-Windows|TAP-Win32|WireGuard|Tailscale|ZeroTier|Bluetooth|蓝牙", RegexOptions.IgnoreCase)) return false;
            bool bus = new[] { "PCI\\", "USB\\", "PCMCIA\\", "SD\\", "SDIO\\", "ACPI\\" }.Any(p => key.StartsWith(p, StringComparison.Ordinal));
            return bus || (hardware && key.Length > 0);
        }

        private static string S(ManagementBaseObject o, string key)
        {
            try { return Convert.ToString(o[key], CultureInfo.InvariantCulture) ?? ""; } catch { return ""; }
        }
        private static bool? B(ManagementBaseObject o, string key)
        {
            try { return o[key] == null ? (bool?)null : Convert.ToBoolean(o[key], CultureInfo.InvariantCulture); } catch { return null; }
        }
        private static int N(ManagementBaseObject o, string key)
        {
            int value; return int.TryParse(S(o, key), out value) ? value : -1;
        }
        private static string[] A(ManagementBaseObject o, string key)
        {
            try { return (o[key] as string[]) ?? new string[0]; } catch { return new string[0]; }
        }
        private static bool Query(string scope, string query, Action<ManagementObject> action)
        {
            try
            {
                var options = new EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(10) };
                using (var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), options))
                using (var rows = searcher.Get())
                    foreach (ManagementObject row in rows) { using (row) { action(row); } }
                return true;
            }
            catch { return false; }
        }
        private static string Kind(string text, int physicalMedium)
        {
            if (physicalMedium == 1 || physicalMedium == 9 || Regex.IsMatch(text ?? "", @"Wi-?Fi|Wireless|WLAN|802\.11|无线", RegexOptions.IgnoreCase)) return "无线";
            if (physicalMedium == 14 || Regex.IsMatch(text ?? "", @"Ethernet|以太网|有线|Gigabit|GbE|RJ45|802\.3|\bLAN\b", RegexOptions.IgnoreCase)) return "有线";
            return "未知类型";
        }
        private static void Mac(PhysicalNic nic, string value, string source, bool permanent)
        {
            string mac = NormalizeMac(value);
            if (mac.Length == 0) return;
            if (permanent && nic.PermanentMac.Length == 0) { nic.PermanentMac = mac; nic.PermanentSource = source; }
            if (!permanent && nic.CurrentMac.Length == 0) { nic.CurrentMac = mac; nic.CurrentSource = source; }
        }

        internal static PhysicalNetworkSnapshot Capture()
        {
            var result = new PhysicalNetworkSnapshot();
            var present = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pnpProblems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var records = new Dictionary<string, PhysicalNic>(StringComparer.OrdinalIgnoreCase);
            bool pnpOk = Query(@"root\cimv2", "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Net' OR ConfigManagerErrorCode <> 0", p =>
            {
                if (B(p, "Present") == false) return;
                string id = Identity(S(p, "PNPDeviceID"));
                if (id.Length == 0) return;
                bool network = S(p, "PNPClass").Equals("Net", StringComparison.OrdinalIgnoreCase) ||
                    A(p, "CompatibleID").Any(x => x.StartsWith(@"PCI\CC_02", StringComparison.OrdinalIgnoreCase)) ||
                    Regex.IsMatch(S(p, "Name"), "Network Controller|Ethernet Controller|网络控制器|以太网控制器", RegexOptions.IgnoreCase);
                if (network) { present[id] = S(p, "Name"); pnpProblems[id] = N(p, "ConfigManagerErrorCode"); }
            });
            bool winOk = Query(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapter", a =>
            {
                string id = Identity(S(a, "PNPDeviceID"));
                if (pnpOk && !present.ContainsKey(id)) return;
                if (!IsPhysical(id, S(a, "Description"), S(a, "ServiceName"), B(a, "PhysicalAdapter") == true)) return;
                var nic = new PhysicalNic { DeviceId = id, Guid = Identity(S(a, "GUID")), Name = S(a, "Name"), Description = S(a, "Description") };
                nic.Kind = Kind(nic.Description, -1);
                nic.InterfaceIndex = (uint)Math.Max(0, N(a, "InterfaceIndex"));
                ApplyState(nic, N(a, "ConfigManagerErrorCode"), N(a, "NetConnectionStatus"), -1, -1);
                Mac(nic, S(a, "MACAddress"), "Win32_NetworkAdapter.MACAddress（当前）", false);
                PhysicalNic prior;
                if (!records.TryGetValue(id, out prior) || (!prior.Connected && nic.Connected)) records[id] = nic;
            });
            var win32Identities = new HashSet<string>(records.Keys, StringComparer.OrdinalIgnoreCase);
            var modernScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool modernOk = Query(@"root\StandardCimv2", "SELECT * FROM MSFT_NetAdapter", a =>
            {
                string id = Identity(S(a, "PnPDeviceID"));
                if (pnpOk && !present.ContainsKey(id)) return;
                if (B(a, "Virtual") == true || !IsPhysical(id, S(a, "InterfaceDescription"), S(a, "DriverName"), B(a, "HardwareInterface") == true)) return;
                PhysicalNic nic;
                string guid = Identity(S(a, "InterfaceGuid"));
                int score = (N(a, "InterfaceAdminStatus") == 1 ? 100 : 0) + (N(a, "MediaConnectState") == 1 ? 20 : 0) +
                    (NormalizeMac(S(a, "PermanentAddress")).Length > 0 ? 10 : 0) + (A(a, "NetworkAddresses").Any(x => NormalizeMac(x).Length > 0) ? 10 : 0);
                if (records.TryGetValue(id, out nic))
                {
                    // Some drivers leave older MSFT interface records for this same physical device.
                    if (nic.Guid.Length > 0 && nic.Guid != guid)
                    {
                        if (win32Identities.Contains(id)) return;
                        int previous; modernScores.TryGetValue(id, out previous);
                        if (score <= previous) return;
                        records.Remove(id);
                        nic = null;
                    }
                }
                if (nic == null)
                {
                    nic = new PhysicalNic { DeviceId = id, Guid = guid, Name = S(a, "InterfaceDescription"), Description = S(a, "InterfaceDescription") };
                    nic.InterfaceIndex = (uint)Math.Max(0, N(a, "InterfaceIndex"));
                    records[id] = nic;
                }
                modernScores[id] = score;
                if (nic.Guid.Length == 0) nic.Guid = guid;
                if (nic.InterfaceIndex == 0) nic.InterfaceIndex = (uint)Math.Max(0, N(a, "InterfaceIndex"));
                string kind = Kind(nic.Description, N(a, "NdisPhysicalMedium"));
                if (kind != "未知类型") nic.Kind = kind;
                int error; pnpProblems.TryGetValue(id, out error);
                ApplyState(nic, error, -1, N(a, "InterfaceAdminStatus"), N(a, "MediaConnectState"));
                Mac(nic, S(a, "PermanentAddress"), "MSFT_NetAdapter.PermanentAddress", true);
                foreach (string address in A(a, "NetworkAddresses")) Mac(nic, address, "MSFT_NetAdapter.NetworkAddresses（当前）", false);
            });
            // Keep present hardware even if an adapter provider omits it or its driver is missing.
            foreach (var p in present)
            {
                if (records.ContainsKey(p.Key) || !IsPhysical(p.Key, p.Value, "", true)) continue;
                int error = pnpProblems[p.Key];
                var nic = new PhysicalNic { DeviceId = p.Key, Name = p.Value, Description = p.Value, Kind = Kind(p.Value, -1) };
                ApplyState(nic, error, -1, -1, -1);
                records[p.Key] = nic;
            }
            var byGuid = records.Values.Where(n => n.Guid.Length > 0).GroupBy(n => n.Guid).ToDictionary(g => g.Key, g => g.First());
            Query(@"root\cimv2", "SELECT * FROM Win32_NetworkAdapterConfiguration", a =>
            {
                PhysicalNic nic;
                if (!byGuid.TryGetValue(Identity(S(a, "SettingID")), out nic)) return;
                Mac(nic, S(a, "MACAddress"), "Win32_NetworkAdapterConfiguration.MACAddress（当前）", false);
                if (nic.Connected && !nic.Disabled)
                {
                    nic.Ip = string.Join(", ", A(a, "IPAddress"));
                    nic.Mask = string.Join(", ", A(a, "IPSubnet"));
                    nic.Gateway = string.Join(", ", A(a, "DefaultIPGateway"));
                }
            });
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    PhysicalNic nic;
                    if (!byGuid.TryGetValue(Identity(adapter.Id), out nic)) continue;
                    Mac(nic, adapter.GetPhysicalAddress().ToString(), ".NET NetworkInterface（当前）", false);
                    if (nic.Ip.Length == 0 && nic.Connected && !nic.Disabled)
                        nic.Ip = string.Join(", ", adapter.GetIPProperties().UnicastAddresses.Select(x => x.Address.ToString()).ToArray());
                }
            }
            catch { result.Notes.Add("系统网络接口补充查询不可用，已使用其他来源。"); }
            foreach (var nic in records.Values)
            {
                NativeMac(nic);
                if (nic.Kind == "未知类型") result.Notes.Add(nic.Name + "：物理设备已保留，连接类型需核实。");
                if (nic.Connected && !nic.Disabled && !HasUsableIp(nic.Ip)) nic.State += "；未获取有效IP";
                if (nic.PermanentMac.Length == 0 && nic.CurrentMac.Length > 0) result.Notes.Add(nic.Name + "：仅获取当前MAC，永久硬件MAC未能读取。");
                if (nic.PermanentMac.Length > 0 && nic.CurrentMac.Length > 0 && nic.PermanentMac != nic.CurrentMac)
                    result.Notes.Add(nic.Name + "：当前MAC与永久硬件MAC不同，可能启用了随机地址或地址覆盖。");
                result.Adapters.Add(nic);
            }
            result.Adapters.Sort((a, b) => string.Compare((a.Kind == "有线" ? "0" : a.Kind == "无线" ? "1" : "2") + a.DeviceId, (b.Kind == "有线" ? "0" : b.Kind == "无线" ? "1" : "2") + b.DeviceId, StringComparison.Ordinal));
            result.InventoryVerified = pnpOk && (winOk || modernOk);
            if (!result.InventoryVerified) result.BlockingIssues.Add("物理网卡清单校验未完成，请确认系统WMI服务和读取权限，或联系信息化管理员。");
            if (result.Adapters.Count == 0) result.BlockingIssues.Add("未发现可核实的物理有线/无线网卡，请核实驱动、设备及BIOS设置。");
            string deviceType = "";
            Query(@"root\cimv2", "SELECT PCSystemType FROM Win32_ComputerSystem", x => deviceType = S(x, "PCSystemType"));
            var chassis = new List<int>();
            Query(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure", x =>
            {
                try { foreach (ushort c in (ushort[])x["ChassisTypes"]) chassis.Add(c); } catch { }
            });
            bool mobile = deviceType == "2" || chassis.Any(c => new[] { 8, 9, 10, 14, 30, 31, 32 }.Contains(c));
            bool desktop = !mobile && (deviceType == "1" || deviceType == "3" || chassis.Any(c => new[] { 3, 4, 5, 6, 7, 13, 15, 16, 35, 36 }.Contains(c)));
            ApplyExpectedAdapters(result, mobile, desktop);
            return result;
        }

        internal static void ApplyExpectedAdapters(PhysicalNetworkSnapshot snapshot, bool mobile, bool desktop)
        {
            if (mobile && !snapshot.Adapters.Any(n => n.Kind == "无线")) snapshot.BlockingIssues.Add("按笔记本设备规则，未发现物理无线网卡；请核实驱动、设备或BIOS设置。");
            if (desktop && !snapshot.Adapters.Any(n => n.Kind == "有线")) snapshot.BlockingIssues.Add("按台式机设备规则，未发现物理有线网卡；请核实驱动、设备或BIOS设置。");
        }
        internal static bool HasUsableIp(string ips)
        {
            return (ips ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Any(s =>
                s != "0.0.0.0" && s != "::" && s != "::1" && !s.StartsWith("169.254.", StringComparison.Ordinal) &&
                !s.StartsWith("127.", StringComparison.Ordinal) && !s.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase));
        }
        internal static void ApplyState(PhysicalNic nic, int error, int connection, int admin, int media)
        {
            nic.Disabled = error == 22 || error == 29 || connection == 5 || admin == 2;
            nic.DriverError = error > 0 && error != 22;
            nic.Connected = !nic.Disabled && (connection == 2 || media == 1);
            if (nic.Disabled) nic.State = error == 29 ? "固件禁用/资源异常，请联系管理员" : "已禁用";
            else if (nic.DriverError) nic.State = "设备/驱动异常（代码" + error + "）";
            else if (nic.Connected) nic.State = "已连接";
            else if (connection == 0 || connection == 7 || media == 2) nic.State = nic.Kind == "无线" ? "已启用，未连接Wi-Fi" : "未连接，可能未接网线";
            else nic.State = "连接状态未知，需核实";
        }

        // MIB_IF_ROW2 is 1352 bytes on Windows; the complete sequential layout avoids buffer overrun.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct IfRow
        {
            public ulong Luid; public uint Index; public Guid Guid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Alias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string Description;
            public uint AddressLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Address;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] PermanentAddress;
            public uint Mtu, Type, Tunnel, Media, PhysicalMedia, Access, Direction;
            public byte Flags; public uint Oper, Admin, MediaState; public Guid NetworkGuid; public uint Connection;
            public ulong TxSpeed, RxSpeed, InOctets, InUcast, InNUcast, InDiscards, InErrors, InUnknown;
            public ulong InUcastOctets, InMulticastOctets, InBroadcastOctets, OutOctets, OutUcast, OutNUcast;
            public ulong OutDiscards, OutErrors, OutUcastOctets, OutMulticastOctets, OutBroadcastOctets, OutQLen;
        }
        [DllImport("iphlpapi.dll")] private static extern uint GetIfEntry2(ref IfRow row);
        private static void NativeMac(PhysicalNic nic)
        {
            if (nic.InterfaceIndex == 0) return;
            try
            {
                if (Marshal.SizeOf(typeof(IfRow)) != 1352) return;
                var row = new IfRow { Index = nic.InterfaceIndex, Address = new byte[32], PermanentAddress = new byte[32] };
                if (GetIfEntry2(ref row) != 0 || row.AddressLength != 6 || Identity(row.Guid.ToString()) != nic.Guid) return;
                Mac(nic, BitConverter.ToString(row.PermanentAddress, 0, 6), "IP Helper.PermanentPhysicalAddress", true);
                Mac(nic, BitConverter.ToString(row.Address, 0, 6), "IP Helper.PhysicalAddress（当前）", false);
            }
            catch { /* Older drivers may not expose this API; never synthesize a MAC. */ }
        }
    }

    internal partial class Program
    {
        private static PhysicalNetworkSnapshot _networkSnapshot;
        private static PhysicalNetworkSnapshot GetPhysicalNetworkSnapshot()
        {
            if (_networkSnapshot == null) _networkSnapshot = PhysicalNetworkInventory.Capture();
            return _networkSnapshot;
        }
        private static void AddNetworkReport(ReportSection section, bool detailed)
        {
            var snapshot = GetPhysicalNetworkSnapshot();
            section.NetworkSummary = snapshot.Summary;
            section.NetworkCards = snapshot.Adapters.Select(n => n.CsvSummary).ToList();
            section.NetworkConclusion = (snapshot.Complete ? "已完成" : "未完成，需核实") + "；有效MAC " + snapshot.Adapters.Count(n => n.Mac.Length > 0) + "/" + snapshot.Adapters.Count + "；有线 " + snapshot.Adapters.Count(n => n.Kind == "有线") + "，无线 " + snapshot.Adapters.Count(n => n.Kind == "无线");
            section.Add("物理网卡数量", snapshot.Adapters.Count + " 个（有线 " + snapshot.Adapters.Count(n => n.Kind == "有线") + "，无线 " + snapshot.Adapters.Count(n => n.Kind == "无线") + "）", "物理设备清单交叉核验");
            section.Add("物理网卡MAC采集状态", snapshot.Complete ? "已完成（所有已确认物理网卡均有有效MAC）" : "未完成，需核实", "物理设备清单及MAC校验", snapshot.Complete ? ItemStatus.Ok : ItemStatus.Warning);
            int index = 0;
            foreach (var nic in snapshot.Adapters)
            {
                index++;
                if (!detailed) { section.Add("网卡" + index, nic.Summary, nic.MacSource, nic.Status); continue; }
                section.AddDivider("网卡" + index + "（" + nic.Kind + "）");
                section.Add("描述", nic.Name, "物理适配器");
                section.Add("状态", nic.State, "设备错误码/管理状态/连接状态", nic.Status);
                section.Add("MAC", nic.Mac, nic.MacSource, nic.Mac.Length == 0 ? ItemStatus.Warning : ItemStatus.Ok);
                section.Add("永久硬件MAC", nic.PermanentMac, nic.PermanentSource);
                section.Add("当前MAC", nic.CurrentMac, nic.CurrentSource);
                section.Add("MAC来源", nic.MacSource, "系统报告来源");
                section.Add("IP", nic.Connected && !nic.Disabled ? nic.Ip : "无当前连接地址", "当前接口地址", PhysicalNetworkInventory.HasUsableIp(nic.Ip) && nic.Connected && !nic.Disabled ? ItemStatus.Ok : ItemStatus.Warning);
                section.Add("子网掩码", nic.Mask, "当前接口配置");
                section.Add("默认网关", nic.Gateway, "当前接口配置");
                section.Add("设备标识", nic.DeviceId, "PnP设备身份（去重）");
            }
            var reviews = snapshot.BlockingIssues.Concat(snapshot.Notes).Concat(snapshot.Adapters.Where(n => n.Disabled || !n.Connected || n.DriverError || !PhysicalNetworkInventory.HasUsableIp(n.Ip)).Select(n => n.Name + "：" + n.State)).Distinct().ToArray();
            if (reviews.Length > 0) section.Add("网卡核实提示", string.Join("；", reviews), "物理网卡完整性核验", ItemStatus.Warning);
        }
        private static ReportSection CollectNetwork()
        {
            var section = new ReportSection("网络信息");
            AddNetworkReport(section, true);
            return section;
        }
        private static ReportSection EnsureBatchNetworkComplete(ReportSection section, bool interactive)
        {
            while (!GetPhysicalNetworkSnapshot().Complete)
            {
                WriteWarning("物理网卡MAC采集尚未完成，已暂停批量采集。");
                foreach (string issue in _networkSnapshot.BlockingIssues) WriteWarning(issue);
                foreach (var nic in _networkSnapshot.Adapters.Where(n => n.Mac.Length == 0))
                {
                    WriteWarning("设备：{0}；状态：{1}；MAC：未能读取。", nic.Name, nic.State);
                    if (nic.DriverError) Console.WriteLine("请联系信息化管理员核实此网卡的驱动或设备状态。");
                    else if (nic.Disabled) Console.WriteLine("请在“网络连接”中启用此适配器，等待设备就绪；通常无需插网线或连接Wi-Fi。");
                    else if (nic.Kind == "无线") Console.WriteLine("请确认无线适配器已启用；仍无法读取时，连接Wi-Fi后重试。");
                    else Console.WriteLine("请确认适配器已启用且设备就绪，必要时连接网络后重试。");
                }
                if (!interactive) throw new OperationCanceledException("物理网卡MAC不完整，已取消批量采集，未上传报告。");
                Console.Write("处理后按 Enter 重新检测网卡；输入 0 取消本次批量采集：");
                string answer = Console.ReadLine();
                if (answer == null || answer.Trim() == "0") throw new OperationCanceledException("已取消批量采集，未上传报告。");
                if (answer.Trim().Length > 0) { WriteWarning("请按 Enter 重试，或输入 0 取消。"); continue; }
                _networkSnapshot = null;
                using (var progress = new ProgressDisplay(1, 0))
                {
                    progress.Start(); progress.BeginStep(1, "重新检测物理网卡");
                    section = CollectNetwork();
                    progress.CompleteStep(1); progress.Stop();
                }
            }
            foreach (var nic in _networkSnapshot.Adapters)
            {
                if (nic.Status == ItemStatus.Warning) WriteWarning("{0}：{1}；MAC已采集。", nic.Name, nic.State);
            }
            foreach (string note in _networkSnapshot.Notes) WriteWarning(note);
            WriteSuccess("物理网卡MAC采集完成，共 {0} 个物理设备。", _networkSnapshot.Adapters.Count);
            return section;
        }
    }
}
