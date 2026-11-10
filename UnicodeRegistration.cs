using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemInfoTest
{
    internal static class UnicodeRegistration
    {
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool ReadConsoleW(IntPtr handle, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2, SizeParamIndex = 2)] char[] buffer, uint length, out uint read, IntPtr control);

        internal static string ReadLine()
        {
            if (Console.IsInputRedirected) return Console.ReadLine();
            IntPtr handle = GetStdHandle(-10);
            uint mode;
            if (!GetConsoleMode(handle, out mode)) return Console.ReadLine();
            var line = new StringBuilder();
            while (true)
            {
                var buffer = new char[4096];
                uint read;
                if (!ReadConsoleW(handle, buffer, 4096, out read, IntPtr.Zero))
                    throw new IOException("无法以Unicode读取控制台输入，请关闭本窗口后重试或联系信息化管理员。");
                if (read == 0) return line.Length == 0 ? null : line.ToString();
                if (read > buffer.Length) throw new IOException("控制台返回的输入长度无效，请重新运行。");
                // ReadConsole does not append a NUL terminator. Consume exactly its returned count.
                line.Append(buffer, 0, (int)read);
                if (line.Length > 32768) throw new IOException("输入内容过长，请重新运行并输入简短的登记信息。");
                if (line.ToString().IndexOfAny(new[] { '\r', '\n' }) >= 0) return line.ToString().TrimEnd('\r', '\n');
            }
        }

        internal static bool HasDamagedText(string value)
        {
            if (value == null) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\ufffd' || (char.IsControl(c) && c != '\t')) return true;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return true;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return true;
            }
            return false;
        }
    }
}
