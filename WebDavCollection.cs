using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace SystemInfoTest
{
    internal sealed class WebDavDirectoryFailure : Exception
    {
        public WebDavDirectoryFailure(string message) : base(message) { }
    }
    internal sealed class WebDavRequestFailure : Exception
    {
        public readonly int Status;
        public readonly WebExceptionStatus NetworkStatus;
        public readonly string Method;
        public WebDavRequestFailure(int status, WebExceptionStatus networkStatus, string method)
            : base("WebDAV request did not complete.") { Status = status; NetworkStatus = networkStatus; Method = method; }
    }

    internal partial class Program
    {
        private const int WebDavTimeoutMilliseconds = 6000;
        private const int WebDavAuthenticatedTimeoutMilliseconds = 12000;

        private static void RunWebDavProgress(string label, Action action)
        {
            using (var progress = new ProgressDisplay(1, 0))
            {
                progress.Start(); progress.BeginStep(1, label);
                action();
                progress.CompleteStep(1); progress.Stop();
            }
        }

        private static CollectTarget ResolveAccessibleCollectTarget(string address, bool interactive, string userName, string password, bool rememberAddress)
        {
            while (true)
            {
                bool connected = false;
                try
                {
                    CollectTarget target = BuildCollectTarget(address, userName, password, interactive);
                    if (!target.IsWebDav)
                    {
                        TestLocalCollectDirectory(target.Address, interactive);
                        WriteSuccess("归集目录访问测试成功。");
                        return target;
                    }

                    if (interactive)
                        RunWebDavProgress("WebDAV 服务连接检测", () => ProbeWebDavService(target));
                    else
                        ProbeWebDavService(target);
                    connected = true;
                    WriteSuccess("WebDAV 服务连接成功。");

                    if (string.IsNullOrWhiteSpace(target.UserName) && interactive)
                        target.UserName = PromptRequiredText("请输入 WebDAV 账号: ");
                    if (string.IsNullOrEmpty(target.Password) && interactive)
                        target.Password = PromptPassword("请输入 WebDAV 密码: ");
                    if (string.IsNullOrWhiteSpace(target.UserName) || string.IsNullOrEmpty(target.Password))
                        throw new WebDavDirectoryFailure("请提供 WebDAV 账号和密码。");

                    if (interactive)
                        RunWebDavProgress("WebDAV 登录及写入验证", () => TestWebDavTarget(target));
                    else
                        TestWebDavTarget(target);
                    WriteSuccess("WebDAV 登录成功，写入及清理验证通过。");
                    if (rememberAddress)
                        SaveSuccessfulCollectAddress(target.Address);
                    return target;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception error)
                {
                    WriteError(connected ? "WebDAV 登录或写入验证失败: {0}" : IsWebDavAddress(address) ? "WebDAV 服务连接失败: {0}" : "归集目标连接失败: {0}", GetSafeCollectFailure(error));
                }

                if (!interactive)
                    return null;
                Console.WriteLine("请输入新的归集地址，或直接回车取消批量采集。");
                Console.Write("示例 https://dav.example.com/DeviceReports 或 \\\\server\\DeviceReports: ");
                address = UnicodeRegistration.ReadLine();
                if (string.IsNullOrWhiteSpace(address))
                {
                    WriteWarning("已取消批量采集。");
                    return null;
                }
                // A manually selected host must receive newly entered credentials.
                userName = "";
                password = "";
            }
        }

        private static CollectTarget BuildCollectTarget(string address, string userName, string password, bool interactive)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new WebDavDirectoryFailure("尚未配置归集地址。");

            address = address.Trim();
            var target = new CollectTarget { Address = address, IsWebDav = IsWebDavAddress(address) };
            if (!target.IsWebDav)
            {
                if (address.Contains("://") || (interactive && !Path.IsPathRooted(address)))
                    throw new WebDavDirectoryFailure("请输入有效的 WebDAV 地址或绝对目录路径。");
                return target;
            }

            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new WebDavDirectoryFailure("WebDAV 地址格式无效，请输入不含账号、查询参数和片段的目录地址。");

            target.Address = uri.AbsoluteUri.TrimEnd('/');
            target.UserName = (userName ?? "").Trim();
            target.Password = password ?? "";
            return target;
        }

        private static HttpWebRequest CreateWebDavRequest(CollectTarget target, string method, string relativePath, bool authenticate)
        {
            string url = target.Address.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(relativePath)) url += "/" + EncodeWebDavRelativePath(CombineWebDavPath(relativePath));
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = authenticate ? WebDavAuthenticatedTimeoutMilliseconds : WebDavTimeoutMilliseconds;
            request.ReadWriteTimeout = request.Timeout;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            if (authenticate)
            {
                request.PreAuthenticate = true;
                request.Credentials = new NetworkCredential(target.UserName, target.Password);
                request.Headers[HttpRequestHeader.Authorization] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(target.UserName + ":" + target.Password));
            }
            return request;
        }

        private static void ProbeWebDavService(CollectTarget target)
        {
            var request = CreateWebDavRequest(target, "OPTIONS", "", false);
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                        throw new InvalidOperationException("服务发生重定向，请向管理员核实目录地址。");
                    string dav = response.Headers["DAV"] ?? "";
                    string allow = response.Headers["Allow"] ?? "";
                    if (dav.Length == 0 && allow.IndexOf("PROPFIND", StringComparison.OrdinalIgnoreCase) < 0 && allow.IndexOf("MKCOL", StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("服务已响应，但未识别到WebDAV目录，请核实地址。");
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response == null) throw new WebDavRequestFailure(0, ex.Status, "OPTIONS");
                int status = (int)response.StatusCode;
                response.Close();
                // Authentication-required is a reachable service, not a network outage.
                if (status != 401 && status != 403) throw new WebDavRequestFailure(status, ex.Status, "OPTIONS");
            }
        }

        private static void TestWebDavTarget(CollectTarget target)
        {
            string directory = ".systeminfo_access_test_" + Guid.NewGuid().ToString("N");
            string file = CombineWebDavPath(directory, "probe.txt");
            bool created = false;
            try
            {
                WebDavRequest(target, "MKCOL", directory, null, false);
                created = true;
                WebDavRequest(target, "PUT", file, Encoding.UTF8.GetBytes("SystemInfo access test"), true);
                WebDavRequest(target, "DELETE", file, null, false);
                WebDavRequest(target, "DELETE", directory, null, false);
                created = false;
            }
            finally
            {
                if (created) { TryDeleteWebDav(target, file); TryDeleteWebDav(target, directory); }
            }
        }

        private static void WebDavRequest(CollectTarget target, string method, string relativePath, byte[] payload, bool hasBody)
        {
            var request = CreateWebDavRequest(target, method, relativePath, true);
            try
            {
                if (hasBody)
                {
                    byte[] data = payload ?? new byte[0];
                    request.ContentLength = data.Length; request.ContentType = "application/octet-stream";
                    using (Stream stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
                }
                else request.ContentLength = 0;
                using (var response = (HttpWebResponse)request.GetResponse())
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                        throw new InvalidOperationException("服务发生重定向，请向管理员核实目录地址。");
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                int status = response == null ? 0 : (int)response.StatusCode;
                if (ex.Response != null) ex.Response.Close();
                throw new WebDavRequestFailure(status, ex.Status, method);
            }
        }

        private static bool WebDavCollectionExists(CollectTarget target, string relativePath)
        {
            var request = CreateWebDavRequest(target, "PROPFIND", relativePath, true);
            request.Headers["Depth"] = "0";
            request.ContentType = "application/xml; charset=utf-8";
            byte[] body = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>");
            request.ContentLength = body.Length;
            try
            {
                using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode != 207 && (int)response.StatusCode != 200)
                        throw new WebDavDirectoryFailure("目录类型核验未成功，未继续上传，请联系信息化管理员。");
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 131072 };
                    var document = new XmlDocument { XmlResolver = null };
                    using (var reader = XmlReader.Create(response.GetResponseStream(), settings)) document.Load(reader);
                    var ns = new XmlNamespaceManager(document.NameTable);
                    ns.AddNamespace("d", "DAV:");
                    foreach (XmlNode item in document.SelectNodes("/d:multistatus/d:response", ns))
                    {
                        XmlNode href = item.SelectSingleNode("d:href", ns);
                        if (href == null) continue;
                        Uri resource;
                        if (!Uri.TryCreate(request.RequestUri, href.InnerText, out resource) ||
                            !resource.Scheme.Equals(request.RequestUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                            !resource.Authority.Equals(request.RequestUri.Authority, StringComparison.OrdinalIgnoreCase) ||
                            resource.UserInfo.Length != 0 || resource.Query.Length != 0 || resource.Fragment.Length != 0 ||
                            !SameWebDavPath(resource, request.RequestUri)) continue;
                        foreach (XmlNode property in item.SelectNodes("d:propstat", ns))
                        {
                            XmlNode status = property.SelectSingleNode("d:status", ns);
                            if (status != null && Regex.IsMatch(status.InnerText, @"^HTTP/\S+\s+200(?:\s|$)") &&
                                property.SelectSingleNode("d:prop/d:resourcetype/d:collection", ns) != null) return true;
                        }
                    }
                    return false;
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                int status = response == null ? 0 : (int)response.StatusCode;
                if (ex.Response != null) ex.Response.Close();
                throw new WebDavRequestFailure(status, ex.Status, "PROPFIND");
            }
            catch (XmlException)
            {
                throw new WebDavDirectoryFailure("服务器返回的目录类型信息无法识别，未将其当作已有目录，也未继续上传。请联系信息化管理员。");
            }
        }

        private static bool SameWebDavPath(Uri resource, Uri requested)
        {
            string[] actual = resource.AbsolutePath.TrimEnd('/').Split('/');
            string[] expected = requested.AbsolutePath.TrimEnd('/').Split('/');
            if (actual.Length != expected.Length)
            {
                return false;
            }

            for (int index = 0; index < actual.Length; index++)
            {
                if (!Uri.UnescapeDataString(actual[index]).Equals(Uri.UnescapeDataString(expected[index]), StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        // Never include raw exception text, URI, credentials, or Authorization in employee-facing errors.
        private static string GetSafeCollectFailure(Exception error)
        {
            if (error is WebDavDirectoryFailure) return error.Message;
            var failure = error as WebDavRequestFailure;
            if (failure != null)
            {
                string phase = failure.Method == "MKCOL" ? "创建目录失败：" : failure.Method == "PUT" ? "写入文件失败：" : failure.Method == "DELETE" ? "清理文件失败：" : failure.Method == "PROPFIND" ? "核验已有目录失败：" : "";
                if (failure.Status == 401) return phase + "账户认证失败（HTTP 401），请联系信息化管理员。";
                if (failure.Status == 403) return phase + "服务器拒绝访问或目录权限不足（HTTP 403），请联系信息化管理员。";
                if (failure.Status == 404) return phase + "目标目录不存在，请核实WebDAV地址。";
                if (failure.Status == 405) return phase + "服务器未允许此项操作（HTTP 405），请核实目录类型与访问权限。";
                if (failure.Status == 409) return phase + "父目录不存在或归档路径有冲突（HTTP 409），请核实目录层级。";
                if (failure.Status > 0) return phase + "服务器返回错误（HTTP " + failure.Status + "），请联系信息化管理员。";
                if (failure.NetworkStatus == WebExceptionStatus.Timeout) return phase + "连接或操作超时，请确认办公网络连接后重试。";
                if (failure.NetworkStatus == WebExceptionStatus.NameResolutionFailure) return "无法解析服务名称，请核实地址及办公网络。";
                if (failure.NetworkStatus == WebExceptionStatus.TrustFailure || failure.NetworkStatus == WebExceptionStatus.SecureChannelFailure) return "HTTPS证书或安全连接校验失败，请联系信息化管理员。";
                return phase + "服务不可达或网络连接中断，请确认办公网络连接后重试。";
            }
            var web = error as WebException;
            if (web != null)
            {
                var response = web.Response as HttpWebResponse;
                if (response != null)
                {
                    int status = (int)response.StatusCode;
                    response.Close();
                    if (status == 401) return "账户认证失败，请联系信息化管理员。";
                    if (status == 403) return "服务器拒绝访问或目录权限不足，请联系信息化管理员。";
                    if (status == 404) return "目标目录不存在，请核实WebDAV地址。";
                    if (status == 405) return "服务器未允许此项操作（HTTP 405），请核实目录类型与访问权限。";
                    return "服务器返回错误（HTTP " + status + "），请联系信息化管理员。";
                }
                if (web.Status == WebExceptionStatus.Timeout) return "连接或操作超时，请确认办公网络连接后重试。";
                if (web.Status == WebExceptionStatus.NameResolutionFailure) return "无法解析服务名称，请核实地址及办公网络。";
                if (web.Status == WebExceptionStatus.TrustFailure || web.Status == WebExceptionStatus.SecureChannelFailure) return "HTTPS证书或安全连接校验失败，请联系信息化管理员。";
                return "服务不可达或网络连接中断，请确认办公网络连接后重试。";
            }
            if (error is ArgumentException || error is UriFormatException) return "地址格式不正确，请输入有效的HTTP或HTTPS目录地址。";
            if (error is UnauthorizedAccessException) return "访问或写入权限不足，请联系信息化管理员。";
            if (error is InvalidOperationException && (error.Message.StartsWith("服务发生重定向") || error.Message.StartsWith("服务已响应"))) return error.Message;
            return "操作未完成，请核实网络、地址、目录权限或联系信息化管理员。";
        }
    }
}
