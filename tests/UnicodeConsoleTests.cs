using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SystemInfoTest
{
    internal static partial class RegressionTests
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public uint Size; public string Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort Show, ReservedBytes; public IntPtr ReservedPtr, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Explicit, Size = 20, CharSet = CharSet.Unicode)]
        private struct InputRecord
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public int Down;
            [FieldOffset(8)] public ushort Repeat;
            [FieldOffset(10)] public ushort VirtualKey;
            [FieldOffset(14), MarshalAs(UnmanagedType.U2)] public char Unicode;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string app, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle, uint code);
        [DllImport("kernel32.dll")] private static extern bool SetConsoleCP(uint code);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WriteConsoleInputW(IntPtr handle, InputRecord[] input, uint length, out uint written);
        private static void UnicodeChild(string[] args)
        {
            int index = Array.IndexOf(args, "--unicode-child");
            uint page = uint.Parse(args[index + 1]); string output = args[index + 2];
            try
            {
                Console.InputEncoding = Encoding.UTF8;
                if (!SetConsoleCP(page)) throw new Exception("Cannot set isolated console code page.");
                string expected = "陈梓钰 \uD842\uDFB7 A";
                var input = new InputRecord[(expected.Length + 1) * 2]; int position = 0;
                foreach (char c in expected + "\r")
                {
                    input[position++] = new InputRecord { EventType = 1, Down = 1, Repeat = 1, Unicode = c, VirtualKey = c == '\r' ? (ushort)13 : (ushort)0 };
                    input[position++] = new InputRecord { EventType = 1, Down = 0, Repeat = 1, Unicode = c, VirtualKey = c == '\r' ? (ushort)13 : (ushort)0 };
                }
                uint written;
                if (!WriteConsoleInputW(GetStdHandle(-10), input, (uint)input.Length, out written) || written != input.Length) throw new Exception("Cannot inject isolated Unicode input events.");
                string value = UnicodeRegistration.ReadLine();
                File.WriteAllText(output, value ?? "<null>", new UTF8Encoding(false));
                if (value != expected) Environment.ExitCode = 2;
            }
            catch (Exception ex) { File.WriteAllText(output, "ERROR: " + ex.Message, new UTF8Encoding(false)); Environment.ExitCode = 1; }
        }
        private static void UnicodeConsoleRegression()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            foreach (uint page in new uint[] { 936, 65001, 437 })
            {
                string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unicode-console-" + page + ".txt");
                var startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfo)), Flags = 1, Show = 0 };
                ProcessInfo process;
                // A new hidden console belongs only to this helper: the user's console/codepage is untouched.
                if (!CreateProcessW(exe, new StringBuilder("\"" + exe + "\" --unicode-child " + page + " \"" + output + "\""), IntPtr.Zero, IntPtr.Zero, false, 0x10, IntPtr.Zero, AppDomain.CurrentDomain.BaseDirectory, ref startup, out process))
                    throw new Exception("Cannot create hidden Unicode console test: " + Marshal.GetLastWin32Error());
                try
                {
                    if (WaitForSingleObject(process.Process, 10000) != 0) { TerminateProcess(process.Process, 3); throw new Exception("Isolated Unicode console test timed out."); }
                    uint exit; GetExitCodeProcess(process.Process, out exit);
                    Check(exit == 0 && File.ReadAllText(output, Encoding.UTF8) == "陈梓钰 \uD842\uDFB7 A", "native Unicode registration survives console codepage " + page);
                }
                finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
            }
        }
    }
}
