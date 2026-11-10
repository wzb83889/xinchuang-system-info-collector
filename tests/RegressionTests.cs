using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SystemInfoTest
{
    internal static partial class RegressionTests
    {
        private const string FixtureUser = "fixture-user";
        private const string FixturePassword = "fixture-password.";
        private static int passed;
        private static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        private static object Call(string method, params object[] args)
        {
            try { return typeof(Program).GetMethod(method, Flags).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }
        private static void Snapshot(PhysicalNetworkSnapshot snapshot) { typeof(Program).GetField("_networkSnapshot", Flags).SetValue(null, snapshot); }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        private static PhysicalNetworkSnapshot Sample(bool mac, bool disabled)
        {
            var snapshot = new PhysicalNetworkSnapshot { InventoryVerified = true };
            snapshot.Adapters.Add(new PhysicalNic { Name = "Physical USB Ethernet", DeviceId = @"USB\VID_1234\DEVICE", Kind = "有线", PermanentMac = mac ? "02:11:22:33:44:55" : "", PermanentSource = "fixture", Disabled = disabled, State = disabled ? "已禁用" : "未连接", Connected = false });
            return snapshot;
        }
        private static string Capture(Action action, string input)
        {
            TextWriter output = Console.Out; TextReader reader = Console.In;
            var text = new StringWriter();
            try { Console.SetOut(text); Console.SetIn(new StringReader(input)); action(); return text.ToString(); }
            finally { Console.SetOut(output); Console.SetIn(reader); }
        }
        private static void Main(string[] args)
        {
            try
            {
                if (args.Contains("--unicode-child")) { UnicodeChild(args); return; }
                if (args.Contains("--live")) { Live(); return; }
                Run(); Console.WriteLine("RESULT: " + passed + " regression checks passed.");
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); Environment.ExitCode = 1; }
        }
        private static void Run()
        {
            Check(Program.AppName1 == "Windows 企业IT信创系统信息采集工具（V1.3.1）", "exact application title");
            Check(Marshal.SizeOf(typeof(PhysicalNetworkInventory).GetNestedType("IfRow", BindingFlags.NonPublic)) == 1352, "native MAC fallback structure size");
            Check(PhysicalNetworkInventory.NormalizeMac("02-11-22-33-44-55") == "02:11:22:33:44:55", "MAC normalized");
            Check(new[] { "", "00:00:00:00:00:00", "FF:FF:FF:FF:FF:FF", "01:11:22:33:44:55", "not-a-mac" }.All(x => PhysicalNetworkInventory.NormalizeMac(x) == ""), "invalid, zero, broadcast and multicast MAC rejected");
            Check(PhysicalNetworkInventory.IsPhysical(@"USB\VID_0BDA\1", "USB Gigabit Ethernet", "rtux64", true), "real USB dock Ethernet retained");
            Check(PhysicalNetworkInventory.IsPhysical(@"PCI\VEN_8086\1", "Intel Wireless", "Netwtw", true), "PCI wireless retained");
            Check(!PhysicalNetworkInventory.IsPhysical(@"ROOT\VMWARE\1", "VMware Virtual Ethernet", "VMnetAdapter", true), "VMware rejected despite physical flag");
            Check(!PhysicalNetworkInventory.IsPhysical(@"ROOT\NET\1", "Sangfor aTrust VNIC", "aTrust", true), "VPN rejected despite physical flag");
            Check(!PhysicalNetworkInventory.IsPhysical(@"BTH\MS_BTHPAN\1", "Bluetooth PAN", "BthPan", true), "Bluetooth PAN excluded");
            Check(PhysicalNetworkInventory.IsPhysical(@"USB\VID_1234\1", "TAPCO USB Ethernet", "rtl", true), "substring TAP does not remove physical hardware");
            var nic = new PhysicalNic { Kind = "无线" };
            PhysicalNetworkInventory.ApplyState(nic, 0, 7, 1, 2);
            Check(!nic.Disabled && !nic.Connected && nic.State.Contains("未连接Wi-Fi"), "wireless disconnected is not disabled");
            PhysicalNetworkInventory.ApplyState(nic, 22, 0, -1, -1);
            Check(nic.Disabled && nic.State == "已禁用", "disabled device recognized by code 22");
            var mobile = new PhysicalNetworkSnapshot { InventoryVerified = true };
            mobile.Adapters.Add(new PhysicalNic { Kind = "无线", CurrentMac = "02:11:22:33:44:55" });
            PhysicalNetworkInventory.ApplyExpectedAdapters(mobile, true, false);
            Check(mobile.Complete, "laptop without wired NIC accepted");
            var desktop = new PhysicalNetworkSnapshot { InventoryVerified = true };
            desktop.Adapters.Add(new PhysicalNic { Kind = "无线", CurrentMac = "02:11:22:33:44:55" });
            PhysicalNetworkInventory.ApplyExpectedAdapters(desktop, false, true);
            Check(!desktop.Complete, "desktop missing wired NIC requires review");
            Check(!new PhysicalNetworkSnapshot { InventoryVerified = false }.Complete, "query failure cannot report completeness");
            var complete = Sample(true, true); Snapshot(complete);
            string output = Capture(() => Call("EnsureBatchNetworkComplete", new ReportSection("网络信息"), true), "");
            Check(output.Contains("采集完成") && !output.Contains("已暂停"), "disabled NIC with MAC does not pause");
            Snapshot(Sample(false, true));
            bool cancelled = false;
            output = Capture(() => { try { Call("EnsureBatchNetworkComplete", new ReportSection("网络信息"), true); } catch (OperationCanceledException) { cancelled = true; } }, "0\n");
            Check(cancelled && output.Contains("已暂停") && output.Contains("启用此适配器"), "missing MAC pauses with action and cancellation");
            Snapshot(Sample(false, false)); cancelled = false;
            output = Capture(() => { try { Call("EnsureBatchNetworkComplete", new ReportSection("网络信息"), false); } catch (OperationCanceledException) { cancelled = true; } }, "");
            Check(cancelled && !output.Contains("采集完成，共"), "noninteractive missing MAC aborts");
            Snapshot(Sample(true, true));
            var section = (ReportSection)Call("CollectNetwork");
            string csv = (string)Call("BuildNetworkSummaryForCsv", new List<ReportSection> { section });
            Check(csv.Contains("02:11:22:33:44:55") && csv.Contains("已禁用"), "CSV uses collected disabled NIC data");

            using (var server = new DavStub("normal"))
            {
                CollectTarget target = null;
                output = Capture(() => target = (CollectTarget)Call("ResolveAccessibleCollectTarget", server.Address, true, FixtureUser, FixturePassword, false), "");
                if (target == null) Console.WriteLine("DIAGNOSTIC: " + output + " | methods=" + string.Join(",", server.Methods.ToArray()));
                Check(target != null && output.Contains("WebDAV 服务连接成功") && output.Contains("登录成功"), "automatic preflight and runtime login succeed");
                Check(server.Authenticated && server.AnonymousOptions && !output.Contains(server.Address) && !output.Contains("是否更换") && !output.Contains("请输入 WebDAV 密码"), "probe anonymous, auth correct, URL hidden and no credential questions");
                Check(server.Methods.Contains("MKCOL") && server.Methods.Contains("PUT") && server.Methods.Count(x => x == "DELETE") == 2, "write and cleanup validated once");
            }
            foreach (string mode in new[] { "unauthorized", "forbidden", "notfound", "generic", "redirect" })
            {
                using (var server = new DavStub(mode))
                {
                    CollectTarget target = null;
                    output = Capture(() => target = (CollectTarget)Call("ResolveAccessibleCollectTarget", server.Address, true, FixtureUser, FixturePassword, false), mode == "unauthorized" || mode == "forbidden" ? "0\n" : "\n");
                    Check(target == null && !output.Contains("登录成功") && !output.Contains(server.Address), mode + " rejected without URL leakage");
                    if (mode == "notfound") Check(output.Contains("示例") && output.Contains("已取消"), "connection failure allows Enter cancellation with example");
                }
            }
            using (var bad = new DavStub("notfound"))
            using (var good = new DavStub("normal"))
            {
                CollectTarget target = null;
                output = Capture(() => target = (CollectTarget)Call("ResolveAccessibleCollectTarget", bad.Address, true, FixtureUser, FixturePassword, false), good.Address + "\n" + FixtureUser + "\n" + FixturePassword + "\n");
                Check(target != null && good.Authenticated && output.Contains("登录成功"), "manual replacement URL reruns preflight with newly entered credentials");
            }
            using (var server = new DavStub("notfound"))
            {
                output = Capture(() => Call("ResolveAccessibleCollectTarget", server.Address, true, FixtureUser, FixturePassword, false), "invalid-address\n\n");
                Check(server.Methods.Count == 1 && output.Contains("已取消"), "invalid replacement does not probe old address again");
            }
            Check((string)Call("BuildArchiveSegment", "..") == "未填写" && (string)Call("BuildArchiveSegment", "NUL.txt") == "_NUL.txt", "directory inputs cannot traverse or use a Windows device name");
            Check(!(bool)Call("SameWebDavPath", new Uri("https://dav.example.com/root/a%2Fb"), new Uri("https://dav.example.com/root/a/b")), "encoded path separator does not match a nested directory");
            var formulaRow = new Dictionary<string, string> { { "使用人员姓名", " =1+1" }, { "门牌号/工位号", "-1" } };
            var formulaTable = CollectionCsv.Parse(CollectionCsv.Render(new[] { formulaRow }));
            Check(formulaTable[1][formulaTable[0].IndexOf("使用人员姓名")] == "' =1+1" && formulaTable[1][formulaTable[0].IndexOf("门牌号/工位号")] == "-1", "CSV neutralizes formulas and preserves numeric values");
            BatchRoundTrip(false);
            BatchRoundTrip(true);
            ExistingDirectoryRegression();
            CsvRegression();
            UnicodeConsoleRegression();
        }
        private static void ExistingDirectoryRegression()
        {
            using (var server = new DavStub("stateful-existing", false))
            {
                var target = (CollectTarget)Call("BuildCollectTarget", server.Address, FixtureUser, FixturePassword, false);
                Call("EnsureWebDavDirectoryPath", target, "数智部/304");
                Call("EnsureWebDavDirectoryPath", target, "数智部/304");
                Check(true, "existing department and room directory reused on second submission");
                Call("EnsureWebDavDirectoryPath", target, "数智部/305");
                Check(server.PropfindCalls >= 3 && server.ProbesDepthZero, "existing department reused for a different room with Depth 0 verification");
                Parallel.Invoke(() => Call("EnsureWebDavDirectoryPath", target, "数智部/306"), () => Call("EnsureWebDavDirectoryPath", target, "数智部/306"));
                Check(true, "concurrent clients safely reuse the same archive directory");
            }
            foreach (string mode in new[] { "stateful-file", "stateful-missing-parent", "stateful-no-mkcol", "stateful-malformed", "stateful-wrong-href", "stateful-prop-denied", "stateful-probe-forbidden" })
            {
                using (var server = new DavStub(mode, false))
                {
                    var target = (CollectTarget)Call("BuildCollectTarget", server.Address, FixtureUser, FixturePassword, false);
                    bool rejected = false;
                    try { Call("EnsureWebDavDirectoryPath", target, "数智部/304"); }
                    catch (WebDavDirectoryFailure) { rejected = true; }
                    catch (WebDavRequestFailure) { rejected = true; }
                    Check(rejected && server.ReportPuts == 0, mode + " never treated as an existing writable directory");
                    if (mode == "stateful-missing-parent") Check(server.PropfindCalls == 0, "409 missing parent is not ignored as success");
                }
            }
            var modules = (List<DetectionModule>)typeof(Program).GetField("Modules", Flags).GetValue(null);
            var originals = modules.ToList();
            try
            {
                modules.Clear();
                modules.Add(new DetectionModule("8", "网络", "fixture", () => { Snapshot(Sample(true, true)); return (ReportSection)Call("CollectNetwork"); }));
                using (var server = new DavStub("stateful-existing", false))
                {
                    string first = Capture(() => Call("RunBatchCollection", server.Address, true, FixtureUser, FixturePassword, false), "9\n测试甲\n304\n");
                    var firstPayloads = server.ReportPayloads.ToDictionary(x => x.Key, x => x.Value);
                    string second = Capture(() => Call("RunBatchCollection", server.Address, true, FixtureUser, FixturePassword, false), "9\n测试甲\n304\n");
                    Check(first.Contains("批量归集完成") && second.Contains("批量归集完成") && server.ReportPuts == 6, "two staff submissions to same department and room both upload three formats");
                    Check(server.ReportPayloads.Count == 6 && firstPayloads.All(p => server.ReportPayloads[p.Key].SequenceEqual(p.Value)), "second submission preserves all first submission files");
                    Check(!second.Contains(server.Address), "existing directory verification does not reveal server URL");
                }
            }
            finally { modules.Clear(); modules.AddRange(originals); }
        }
        private static void BatchRoundTrip(bool missingMac)
        {
            var modules = (List<DetectionModule>)typeof(Program).GetField("Modules", Flags).GetValue(null);
            var originals = modules.ToList();
            try
            {
                modules.Clear();
                modules.Add(new DetectionModule("8", "网络", "fixture", () => { Snapshot(Sample(!missingMac, true)); return (ReportSection)Call("CollectNetwork"); }));
                using (var server = new DavStub("normal"))
                {
                    string output = Capture(() => Call("RunBatchCollection", server.Address, true, FixtureUser, FixturePassword, false), "1\n测试人员\nA001\n0\n");
                    Check(output.Contains("门牌号/工位号") && !output.Contains(server.Address), "batch registration label and URL privacy " + missingMac);
                    if (missingMac) Check(server.ReportPuts == 0 && !output.Contains("批量归集完成"), "MAC incomplete cancels before report upload");
                    else Check(server.ReportPuts == 3 && output.Contains("批量归集完成"), "batch round trip uploads txt/json/csv");
                }
            }
            finally { modules.Clear(); modules.AddRange(originals); }
        }
        private static void Live()
        {
            var snapshot = PhysicalNetworkInventory.Capture();
            Snapshot(snapshot);
            Check(snapshot.InventoryVerified && snapshot.Adapters.Count > 0, "live physical inventory verified");
            Check(snapshot.Adapters.All(n => PhysicalNetworkInventory.IsPhysical(n.DeviceId, n.Name, "", true)), "live inventory contains physical devices only");
            Check(snapshot.Adapters.Select(n => n.DeviceId).Distinct().Count() == snapshot.Adapters.Count, "live PnP identity deduplicated");
            Check(snapshot.Adapters.All(n => n.Mac.Length > 0), "live all detected physical MACs available");
            var native = snapshot.Adapters.First(n => n.InterfaceIndex > 0);
            var nativeOnly = new PhysicalNic { InterfaceIndex = native.InterfaceIndex, Guid = native.Guid };
            typeof(PhysicalNetworkInventory).GetMethod("NativeMac", Flags).Invoke(null, new object[] { nativeOnly });
            Check(nativeOnly.CurrentMac.Length > 0 && nativeOnly.PermanentMac.Length > 0, "live native API returns current and permanent MAC");
            var basic = (ReportSection)Call("CollectBasicHardwareSummary");
            var full = (ReportSection)Call("CollectNetwork");
            Check(basic.NetworkSummary == full.NetworkSummary && full.NetworkSummary == snapshot.Summary, "basic and full workflows use same inventory");
            string csv = (string)Call("BuildNetworkSummaryForCsv", new List<ReportSection> { full });
            Check(csv == snapshot.Summary, "live CSV equals on-screen snapshot");
            var split = CollectionCsv.Parse((string)Call("BuildReportSummaryCsv", new List<ReportSection> { full }, "live", "2026-11-06 17:00:00"));
            Check(split[1][split[0].IndexOf("网卡数量")] == snapshot.Adapters.Count.ToString() && split[0].Contains("MAC采集结论"), "live export has separate NIC count and conclusion");
            Check(snapshot.Adapters.Select((nic, i) => split[1][split[0].IndexOf("网卡" + (i + 1))].Contains(nic.Mac)).All(v => v), "live every physical MAC remains in its own exported NIC column");
            foreach (var nic in snapshot.Adapters) Console.WriteLine("LIVE: " + nic.Name + " | " + nic.Kind + " | " + nic.State + " | permanentMAC=" + (nic.PermanentMac.Length > 0) + " | currentMAC=" + (nic.CurrentMac.Length > 0));
            // Start from a synthetic missing result, then exercise Enter -> real, read-only re-enumeration.
            Snapshot(Sample(false, true));
            string output = Capture(() => Call("EnsureBatchNetworkComplete", new ReportSection("网络信息"), true), "\n0\n");
            Check(output.Contains("已暂停") && output.Contains("采集完成，共"), "Enter retry re-enumerates physical hardware and resumes");
            var modules = (List<DetectionModule>)typeof(Program).GetField("Modules", Flags).GetValue(null);
            var originals = modules.ToList(); int beforeCount = 0, afterCount = 0;
            try
            {
                modules.Clear();
                modules.Add(new DetectionModule("1", "已完成模块", "fixture", () => { beforeCount++; return new ReportSection("已完成模块"); }));
                modules.Add(new DetectionModule("8", "网络", "fixture", () => { Snapshot(Sample(false, true)); return (ReportSection)Call("CollectNetwork"); }));
                modules.Add(new DetectionModule("9", "后续模块", "fixture", () => { afterCount++; return new ReportSection("后续模块"); }));
                using (var server = new DavStub("normal", false))
                {
                    output = Capture(() => Call("RunBatchCollection", server.Address, true, FixtureUser, FixturePassword, false), "1\n测试人员\nA001\n\n0\n");
                    Check(output.Contains("已暂停") && output.Contains("批量归集完成") && server.ReportPuts == 3, "live retry resumes same batch and uploads three formats to loopback only");
                    Check(beforeCount == 1 && afterCount == 1 && output.Split(new[] { "请输入该计算机设备使用人员姓名" }, StringSplitOptions.None).Length == 2, "retry preserves previous modules and registration");
                }
            }
            finally { modules.Clear(); modules.AddRange(originals); }
            Console.WriteLine("RESULT: " + passed + " live read-only checks passed; no adapter state changed.");
        }

        private sealed class DavStub : IDisposable
        {
            private readonly TcpListener listener;
            private readonly Thread worker;
            private readonly string mode;
            private readonly bool saveReports;
            private volatile bool running = true;
            public readonly List<string> Methods = new List<string>();
            public bool Authenticated, AnonymousOptions;
            public int ReportPuts;
            public int PropfindCalls;
            public bool ProbesDepthZero = true;
            public readonly Dictionary<string, byte[]> ReportPayloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            private readonly HashSet<string> directories = new HashSet<string>(StringComparer.Ordinal);
            public string Address;
            public DavStub(string mode, bool saveReports = true)
            {
                this.mode = mode;
                this.saveReports = saveReports;
                listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
                Address = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/DeviceReports";
                worker = new Thread(Serve) { IsBackground = true }; worker.Start();
            }
            private void Serve()
            {
                while (running)
                {
                    try
                    {
                        using (TcpClient client = listener.AcceptTcpClient())
                        using (NetworkStream stream = client.GetStream())
                        {
                            client.ReceiveTimeout = 8000;
                            var header = new StringBuilder(); int value;
                            while ((value = stream.ReadByte()) >= 0) { header.Append((char)value); if (header.ToString().EndsWith("\r\n\r\n")) break; if (header.Length > 65536) throw new IOException(); }
                            string[] lines = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                            string[] request = lines[0].Split(' '); string method = request[0]; string path = request[1];
                            Methods.Add(method);
                            string auth = lines.FirstOrDefault(x => x.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) ?? "";
                            string lengthLine = lines.FirstOrDefault(x => x.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                            int length = lengthLine == null ? 0 : int.Parse(lengthLine.Split(':')[1]);
                            if (lines.Any(line => line.Equals("Expect: 100-continue", StringComparison.OrdinalIgnoreCase)))
                            {
                                byte[] continueResponse = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                                stream.Write(continueResponse, 0, continueResponse.Length);
                            }
                            byte[] body = new byte[length]; int received = 0;
                            while (received < length) { int count = stream.Read(body, received, length - received); if (count == 0) break; received += count; }
                            bool valid = auth.EndsWith("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(FixtureUser + ":" + FixturePassword)), StringComparison.Ordinal);
                            string responseBody = "";
                            int status;
                            if (method == "OPTIONS") { AnonymousOptions = auth.Length == 0; status = mode == "notfound" ? 404 : mode == "redirect" ? 301 : mode == "generic" ? 200 : 401; }
                            else if (mode.StartsWith("stateful", StringComparison.Ordinal))
                            {
                                Authenticated |= valid;
                                if (!valid) status = 401;
                                else if (method == "MKCOL") status = mode == "stateful-missing-parent" ? 409 : mode != "stateful-existing" ? 405 : directories.Add(path.TrimEnd('/')) ? 201 : 405;
                                else if (method == "PROPFIND")
                                {
                                    PropfindCalls++;
                                    ProbesDepthZero &= lines.Any(x => x.Equals("Depth: 0", StringComparison.OrdinalIgnoreCase));
                                    status = mode == "stateful-no-mkcol" ? 405 : mode == "stateful-probe-forbidden" ? 403 : directories.Contains(path.TrimEnd('/')) || mode != "stateful-existing" ? 207 : 404;
                                    string href = mode == "stateful-wrong-href" ? "/unrelated/resource" : path;
                                    string type = mode == "stateful-file" ? "" : "<d:collection/>";
                                    string propStatus = mode == "stateful-prop-denied" ? "403 Forbidden" : "200 OK";
                                    if (status == 207) responseBody = mode == "stateful-malformed" ? "<!DOCTYPE x [<!ENTITY x SYSTEM 'http://127.0.0.1:1/should-not-fetch'>]><x>&x;</x>" : "<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>" + href + "/</d:href><d:propstat><d:prop><d:resourcetype>" + type + "</d:resourcetype></d:prop><d:status>HTTP/1.1 " + propStatus + "</d:status></d:propstat></d:response></d:multistatus>";
                                }
                                else { status = method == "DELETE" ? 204 : 201; if (method == "DELETE") directories.Remove(path.TrimEnd('/')); }
                            }
                            else { Authenticated |= valid; status = mode == "unauthorized" || !valid ? 401 : mode == "forbidden" ? 403 : method == "DELETE" ? 204 : 201; }
                            if (method == "PUT" && status == 201 && !path.Contains(".systeminfo_access_test_"))
                            {
                                ReportPuts++;
                                ReportPayloads[path] = body;
                                if (saveReports)
                                {
                                    string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-reports");
                                    Directory.CreateDirectory(folder);
                                    string extension = Path.GetExtension(Uri.UnescapeDataString(path));
                                    File.WriteAllBytes(Path.Combine(folder, "batch" + extension), body);
                                }
                            }
                            string extra = status == 401 ? "WWW-Authenticate: Basic realm=\"test\"\r\n" : status == 301 ? "Location: http://127.0.0.1:1/must-not-follow\r\n" : "";
                            byte[] response = Encoding.UTF8.GetBytes("HTTP/1.1 " + status + " Test\r\n" + extra + "Content-Type: application/xml; charset=utf-8\r\nContent-Length: " + Encoding.UTF8.GetByteCount(responseBody) + "\r\nConnection: close\r\n\r\n" + responseBody);
                            stream.Write(response, 0, response.Length);
                        }
                    }
                    catch { if (!running) break; }
                }
            }
            public void Dispose() { running = false; listener.Stop(); worker.Join(1500); }
        }
    }
}
