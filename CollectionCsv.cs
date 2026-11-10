using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace SystemInfoTest
{
    internal sealed class CsvMergeResult { public string Path; public int Rows, BadNames, Skipped; }
    internal static class CollectionCsv
    {
        private static readonly string[] BeforeNetwork = { "采集时间", "报告类型", "部门", "使用人员姓名", "姓名校验", "门牌号/工位号", "计算机名", "品牌", "型号", "设备类型", "序列号", "预计出厂日期", "操作系统", "系统版本", "产品激活状态", "CPU", "内存", "内存插槽占用", "硬盘驱动器" };
        private static readonly string[] AfterNetwork = { "网卡核实提示", "信创判断结果", "信创置信度" };
        private static string Value(IDictionary<string, string> row, string key) { string v; return row.TryGetValue(key, out v) ? v ?? "" : ""; }
        private static int CardNumber(string key) { int n; return Regex.IsMatch(key, @"^网卡\d+$") && int.TryParse(key.Substring(2), out n) ? n : 0; }
        private static string Quote(string value) { value = value ?? ""; return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value; }
        private static string QuoteSpreadsheetCell(string value)
        {
            value = value ?? "";
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@＝＋－＠".IndexOf(trimmed[0]) >= 0)
            {
                // Preserve numeric measurements while making spreadsheet formulas inert.
                decimal number;
                if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                    value = "'" + value;
            }
            return Quote(value);
        }

        internal static string Render(IEnumerable<Dictionary<string, string>> source)
        {
            var rows = source.ToList();
            int cards = Math.Max(2, rows.SelectMany(r => r.Keys).Select(CardNumber).DefaultIfEmpty(0).Max());
            if (cards > 1024) throw new InvalidDataException("网卡列数量异常，请核实源CSV。");
            var headers = BeforeNetwork.ToList(); headers.Add("网卡数量"); headers.Add("MAC采集结论");
            for (int i = 1; i <= cards; i++) headers.Add("网卡" + i);
            headers.AddRange(AfterNetwork);
            foreach (string key in rows.SelectMany(r => r.Keys)) if (key != "网卡信息" && !headers.Contains(key)) headers.Add(key);
            var text = new StringBuilder(); text.AppendLine(string.Join(",", headers.Select(QuoteSpreadsheetCell).ToArray()));
            foreach (var row in rows) text.AppendLine(string.Join(",", headers.Select(h => QuoteSpreadsheetCell(Value(row, h))).ToArray()));
            return text.ToString();
        }
        internal static List<List<string>> Parse(string text)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
            bool quoted = false, closed = false, start = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } }
                    else cell.Append(c);
                    continue;
                }
                if (closed && (c == ' ' || c == '\t')) continue;
                if (c == ',' || c == '\r' || c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear(); start = true; closed = false;
                    if (c != ',') { if (row.Any(v => v.Length > 0)) rows.Add(row); row = new List<string>(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
                }
                else if (c == '"' && start) { quoted = true; start = false; }
                else { if (closed || c == '"') throw new InvalidDataException("CSV引号格式异常。"); cell.Append(c); start = false; }
            }
            if (quoted) throw new InvalidDataException("CSV引号字段未结束。");
            if (cell.Length > 0 || row.Count > 0 || closed) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }
        internal static string Decode(byte[] bytes, out string encoding)
        {
            if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) { encoding = "UTF-8 BOM"; return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3); }
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) { encoding = "UTF-16 LE"; return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2); }
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) { encoding = "UTF-16 BE"; return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2); }
            try { encoding = "UTF-8"; return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { encoding = "GB18030兼容"; return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
        }
        internal static void NormalizeRow(Dictionary<string, string> row, string csvPath)
        {
            Alias(row, "使用位置", "门牌号/工位号"); Alias(row, "笔记本网卡核实提示", "网卡核实提示");
            string name = Value(row, "使用人员姓名"); bool bad = UnicodeRegistration.HasDamagedText(name);
            bool missing = string.IsNullOrWhiteSpace(name) || name == "系统未提供" || name == "未填写";
            JsonReport json = csvPath != null && (bad || missing || !row.ContainsKey("MAC采集结论")) ? ReadMatchingJson(csvPath, row) : null;
            if ((bad || missing) && json != null)
            {
                string original = JsonValue(json, "使用人员姓名");
                if (!string.IsNullOrWhiteSpace(original) && original != "系统未提供" && original != "未填写" && !UnicodeRegistration.HasDamagedText(original)) { row["使用人员姓名"] = original; name = original; bad = false; row["姓名校验"] = "已从同批JSON原文恢复"; }
            }
            if (bad) row["姓名校验"] = "姓名含损坏字符，需人工核实";
            else if (string.IsNullOrWhiteSpace(name) || name == "系统未提供" || name == "未填写") row["姓名校验"] = "姓名未填写，需核实";
            else if (Value(row, "姓名校验") != "已从同批JSON原文恢复") row["姓名校验"] = "正常";
            if (!row.ContainsKey("网卡信息")) return;
            string raw = Value(row, "网卡信息"); var count = Regex.Match(raw, @"^\s*(\d+)\s*个");
            string body = Regex.Replace(raw, @"^\s*\d+\s*个\s*[；;]\s*", "");
            var cards = Regex.Split(body, @"[；;](?=(?:[^；;]*\[(?:有线|无线|未知类型)\]\s*\||网卡\d+\s*\|))")
                .Where(p => Regex.IsMatch(p, @"\[(?:有线|无线|未知类型)\]\s*\||^网卡\d+\s*\|")).OrderBy(CardRank).ToList();
            if (cards.Count > 0)
            {
                for (int i = 0; i < cards.Count; i++) { string kind = CardRank(cards[i]) == 0 ? "有线" : CardRank(cards[i]) == 1 ? "无线" : "类型未注明"; row["网卡" + (i + 1)] = kind + " | " + cards[i].Replace(" [" + kind + "]", ""); }
                row["网卡数量"] = count.Success ? count.Groups[1].Value : cards.Count.ToString(CultureInfo.InvariantCulture);
                int valid = cards.Count(c => Regex.IsMatch(c, @"\|\s*MAC\s*:?\s*[0-9A-Fa-f]{2}(?:[:-][0-9A-Fa-f]{2}){5}"));
                string conclusion = json == null ? "" : JsonValue(json, "物理网卡MAC采集状态");
                row["MAC采集结论"] = conclusion.Length > 0 ? conclusion : "旧版记录：有效MAC " + valid + "/" + Value(row, "网卡数量") + "；原完整性结论未提供";
                if (count.Success && count.Groups[1].Value != cards.Count.ToString(CultureInfo.InvariantCulture)) row["旧版网卡原文"] = raw;
            }
            else if (!string.IsNullOrWhiteSpace(raw) && raw != "未采集") { row["旧版网卡原文"] = raw; row["网卡数量"] = count.Success ? count.Groups[1].Value : "未能解析"; row["MAC采集结论"] = "旧版网卡文本未能完整分列，原文已保留"; }
            row.Remove("网卡信息");
        }
        private static int CardRank(string card)
        {
            if (Regex.IsMatch(card, @"\[无线\]|^无线\s*\|")) return 1;
            if (Regex.IsMatch(card, @"\[有线\]|^有线\s*\|")) return 0;
            if (Regex.IsMatch(card, @"Wi-?Fi|Wireless|WLAN|802\.11", RegexOptions.IgnoreCase)) return 1;
            if (Regex.IsMatch(card, @"Ethernet|GbE|以太网", RegexOptions.IgnoreCase)) return 0;
            return 2;
        }
        private static void Alias(Dictionary<string, string> row, string oldKey, string newKey) { if (!row.ContainsKey(oldKey)) return; if (!row.ContainsKey(newKey)) row[newKey] = row[oldKey]; row.Remove(oldKey); }
        internal static CsvMergeResult MergeDirectory(string directory)
        {
            var rows = new List<Dictionary<string, string>>(); var errors = new List<Dictionary<string, string>>();
            string sourceRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var files = Directory.GetFiles(directory, "*.csv", SearchOption.AllDirectories).Where(p => !Path.GetFileName(p).StartsWith("设备信息统计表_", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string file in files)
            {
                try
                {
                    string encoding; var table = Parse(Decode(File.ReadAllBytes(file), out encoding));
                    if (table.Count < 2 || !table[0].Contains("使用人员姓名")) throw new InvalidDataException("不是可识别的采集CSV或没有数据行。");
                    var headers = table[0]; if (headers.Distinct().Count() != headers.Count) throw new InvalidDataException("CSV含重复列名。");
                    var fileRows = new List<Dictionary<string, string>>();
                    foreach (var cells in table.Skip(1))
                    {
                        if (cells.Count != headers.Count) throw new InvalidDataException("CSV列数与表头不一致，未按位置猜测字段。");
                        var row = headers.Select((h, i) => new { h, value = cells[i] }).ToDictionary(p => p.h, p => p.value, StringComparer.Ordinal);
                        NormalizeRow(row, file); row["源报告文件"] = Path.GetFullPath(file).Substring(sourceRoot.Length); row["源文件编码"] = encoding; fileRows.Add(row);
                    }
                    rows.AddRange(fileRows);
                }
                catch (Exception ex) { errors.Add(new Dictionary<string, string> { { "源报告文件", file }, { "读取异常", ex is DecoderFallbackException ? "编码损坏，未使用替代字符强行读取" : ex.Message } }); }
            }
            if (rows.Count == 0) throw new InvalidDataException("没有可安全汇总的数据行，请核实采集CSV格式和编码。");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture); string path = Path.Combine(directory, "设备信息统计表_" + stamp + ".csv");
            File.WriteAllText(path, Render(rows), new UTF8Encoding(true));
            if (errors.Count > 0)
            {
                var text = new StringBuilder("源报告文件,读取异常\r\n"); foreach (var error in errors) text.AppendLine(QuoteSpreadsheetCell(error["源报告文件"]) + "," + QuoteSpreadsheetCell(error["读取异常"]));
                File.WriteAllText(Path.Combine(directory, "设备信息统计表_读取异常_" + stamp + ".csv"), text.ToString(), new UTF8Encoding(true));
            }
            return new CsvMergeResult { Path = path, Rows = rows.Count, BadNames = rows.Count(r => Value(r, "姓名校验").Contains("核实")), Skipped = errors.Count };
        }
        [DataContract] private sealed class JsonReport
        {
            [DataMember(Name = "computerName")] public string Computer { get; set; }
            [DataMember(Name = "generatedAt")] public string Generated { get; set; }
            [DataMember(Name = "sections")] public List<JsonSection> Sections { get; set; }
        }
        [DataContract] private sealed class JsonSection { [DataMember(Name = "items")] public List<JsonItem> Items { get; set; } }
        [DataContract] private sealed class JsonItem { [DataMember(Name = "name")] public string Name { get; set; } [DataMember(Name = "value")] public string Value { get; set; } }
        private static string JsonValue(JsonReport report, string name)
        {
            if (report == null || report.Sections == null) return "";
            var item = report.Sections.Where(s => s.Items != null).SelectMany(s => s.Items).FirstOrDefault(i => i.Name == name);
            return item == null ? "" : item.Value ?? "";
        }
        private static JsonReport ReadMatchingJson(string csvPath, Dictionary<string, string> row)
        {
            try
            {
                string path = Path.ChangeExtension(csvPath, ".json"); if (!File.Exists(path) || new FileInfo(path).Length > 8388608) return null;
                byte[] bytes = File.ReadAllBytes(path);
                int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                using (var stream = new MemoryStream(bytes, offset, bytes.Length - offset))
                {
                    var report = (JsonReport)new DataContractJsonSerializer(typeof(JsonReport)).ReadObject(stream);
                    if (string.IsNullOrEmpty(report.Computer) || string.IsNullOrEmpty(report.Generated) || report.Computer != Value(row, "计算机名") || report.Generated != Value(row, "采集时间")) return null;
                    return report;
                }
            }
            catch { return null; }
        }
    }
}
