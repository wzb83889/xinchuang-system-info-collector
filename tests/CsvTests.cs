using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SystemInfoTest
{
    internal static partial class RegressionTests
    {
        private static string CsvValue(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
        private static string LegacyCsv(string name, bool reordered)
        {
            var row = new Dictionary<string, string> {
                { "采集时间", "2026-11-06 17:00:00" }, { "报告类型", "批量设备信息归集" },
                { "部门", "数智部" }, { "使用人员姓名", name }, { "使用位置", "304" },
                { "计算机名", Environment.MachineName },
                { "网卡信息", "2 个；Intel Wi-Fi [无线] | 未连接Wi-Fi | MAC: 02:11:22:33:44:55 | IP: 无当前连接地址；Realtek Ethernet [有线] | 已连接 | MAC: 02:22:33:44:55:66 | IP: 192.0.2.1, fe80::1" },
                { "笔记本网卡核实提示", "无线未连接" }, { "保留原字段", "带逗号,带引号\"，及换行\r\n第二行" }
            };
            var keys = reordered ? row.Keys.Reverse().ToList() : row.Keys.ToList();
            return string.Join(",", keys.Select(CsvValue).ToArray()) + "\r\n" + string.Join(",", keys.Select(k => CsvValue(row[k])).ToArray()) + "\r\n";
        }
        private static void MatchingJson(string path, string name, bool mismatch)
        {
            var sections = new List<ReportSection>();
            Call("InsertCollectionRegistrationSection", sections, "数智部", name, "304");
            var network = new ReportSection("网络信息"); network.Add("物理网卡MAC采集状态", "已完成（原报告）", "fixture"); sections.Add(network);
            string json = (string)Call("BuildReportJson", sections, "批量设备信息归集", "2026-11-06 17:00:00");
            if (mismatch) json = json.Replace(Environment.MachineName, "UNRELATED-COMPUTER");
            File.WriteAllText(path, json, new UTF8Encoding(true));
        }
        private static void CsvRegression()
        {
            Check(!UnicodeRegistration.HasDamagedText("陈梓钰 𠮷 A"), "Chinese and supplementary-plane characters remain valid input");
            Check(UnicodeRegistration.HasDamagedText("\ufffd\ufffd") && UnicodeRegistration.HasDamagedText("\ud842"), "replacement characters and broken surrogate rejected");
            string accepted = null;
            string prompt = Capture(() => accepted = (string)Call("PromptRequiredText", "姓名："), "\ufffd\ufffd\n陈梓钰\n");
            Check(accepted == "陈梓钰" && prompt.Contains("不会将乱码写入报告"), "damaged registration requires reentry before collection");
            var snapshot = Sample(true, false);
            snapshot.Adapters.Add(new PhysicalNic { Name = "Intel Wi-Fi", Kind = "无线", CurrentMac = "02:33:44:55:66:77", State = "未连接Wi-Fi" });
            Snapshot(snapshot);
            var full = (ReportSection)Call("CollectNetwork");
            var sections = new List<ReportSection> { full };
            Call("InsertCollectionRegistrationSection", sections, "数智部", "陈梓钰", "304");
            string csv = (string)Call("BuildReportSummaryCsv", sections, "fixture", "2026-11-06 17:00:00");
            var parsed = CollectionCsv.Parse(csv);
            var data = parsed[0].Select((h, i) => new { h, v = parsed[1][i] }).ToDictionary(x => x.h, x => x.v);
            Check(data["网卡数量"] == "2" && data["MAC采集结论"].Contains("2/2"), "NIC count and MAC conclusion have independent columns");
            Check(data["网卡1"].StartsWith("有线 |") && data["网卡2"].StartsWith("无线 |") && !data.ContainsKey("网卡信息"), "wired and wireless occupy separate columns");
            Check(data["使用人员姓名"] == "陈梓钰" && data["姓名校验"] == "正常", "new report preserves and validates Chinese name");
            var special = CollectionCsv.Parse("a,b\r\n\"张,三\",\"第一行\r\n第二行 \"\"引号\"\"\"\r\n");
            Check(special[1][0] == "张,三" && special[1][1] == "第一行\r\n第二行 \"引号\"", "CSV parser preserves quoted commas, quotes and multiline values");
            string encoding;
            bool rejected = false;
            try { CollectionCsv.Decode(new byte[] { 239, 187, 191, 228 }, out encoding); } catch (DecoderFallbackException) { rejected = true; }
            Check(rejected, "invalid declared UTF8 rejected instead of inserting replacement characters");
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "merge-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.csv"), LegacyCsv("\ufffd\ufffd", false), new UTF8Encoding(true)); MatchingJson(Path.Combine(folder, "a.json"), "李梅", false);
            File.WriteAllText(Path.Combine(folder, "b.csv"), LegacyCsv("\ufffd\ufffd", true), new UTF8Encoding(true)); MatchingJson(Path.Combine(folder, "b.json"), "\ufffd\ufffd", false);
            File.WriteAllText(Path.Combine(folder, "c.csv"), LegacyCsv("\ufffd\ufffd", false), new UTF8Encoding(true)); MatchingJson(Path.Combine(folder, "c.json"), "错误配对姓名", true);
            File.WriteAllText(Path.Combine(folder, "d.csv"), LegacyCsv("王晶晶", true), Encoding.GetEncoding(936));
            File.WriteAllText(Path.Combine(folder, "e.csv"), LegacyCsv("赵一", false), Encoding.Unicode);
            data["网卡3"] = "有线 | 第三张物理网卡 | MAC: 02:44:55:66:77:88"; data["网卡数量"] = "3";
            File.WriteAllText(Path.Combine(folder, "f.csv"), CollectionCsv.Render(new[] { data }), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder, "g.csv"), LegacyCsv("", true), new UTF8Encoding(true)); MatchingJson(Path.Combine(folder, "g.json"), "孙一", false);
            File.WriteAllText(Path.Combine(folder, "broken.csv"), "使用人员姓名,计算机名\r\n\"未结束", new UTF8Encoding(true));
            var originals = Directory.GetFiles(folder).ToDictionary(p => p, p => File.ReadAllBytes(p));
            var merged = CollectionCsv.MergeDirectory(folder);
            var table = CollectionCsv.Parse(File.ReadAllText(merged.Path, Encoding.UTF8));
            var rows = table.Skip(1).Select(v => table[0].Select((h, i) => new { h, value = v[i] }).ToDictionary(x => x.h, x => x.value)).ToList();
            Check(merged.Rows == 7 && merged.Skipped == 1 && merged.BadNames == 2, "mixed-version merge keeps seven records, flags damaged names and reports unreadable file");
            var a = rows.Single(r => r["源报告文件"] == "a.csv");
            Check(a["使用人员姓名"] == "李梅" && a["姓名校验"] == "已从同批JSON原文恢复", "name recovered only from matching same-batch JSON");
            Check(rows.Single(r => r["源报告文件"] == "g.csv")["使用人员姓名"] == "孙一", "blank CSV name restored only from matching complete JSON");
            Check(rows.Where(r => r["源报告文件"] == "b.csv" || r["源报告文件"] == "c.csv").All(r => r["使用人员姓名"] == "\ufffd\ufffd" && r["姓名校验"].Contains("核实")), "irreversible names and mismatched JSON never guessed or overwritten");
            Check(rows.Single(r => r["源报告文件"] == "d.csv")["使用人员姓名"] == "王晶晶" && rows.Single(r => r["源报告文件"] == "e.csv")["使用人员姓名"] == "赵一", "GBK and UTF16 legacy names decoded correctly");
            Check(a["网卡1"].StartsWith("有线 |") && a["网卡2"].StartsWith("无线 |") && a["MAC采集结论"] == "已完成（原报告）", "legacy NIC text split and original JSON conclusion retained");
            Check(rows.All(r => r.ContainsKey("网卡3")) && rows.Single(r => r["源报告文件"] == "f.csv")["网卡3"].Contains("第三张"), "dynamic NIC columns align mixed one/two/three-NIC reports");
            Check(rows.Where(r => r["源报告文件"] != "f.csv").All(r => r["保留原字段"].Contains("第二行")), "unknown legacy columns and multiline values preserved");
            Check(originals.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "merge leaves every original CSV and JSON byte unchanged");
            var repeat = CollectionCsv.MergeDirectory(folder);
            Check(repeat.Rows == 7 && repeat.Skipped == 1, "remerge excludes prior summaries and error lists");
        }
    }
}
