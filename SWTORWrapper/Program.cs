using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Management;

namespace SWTORWrapper
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern void OutputDebugString(string lpOutputString);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess,
            IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        // privileges
        const int PROCESS_CREATE_THREAD = 0x0002;
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int PROCESS_VM_OPERATION = 0x0008;
        const int PROCESS_VM_WRITE = 0x0020;
        const int PROCESS_VM_READ = 0x0010;

        // used for memory allocation
        const uint MEM_COMMIT = 0x00001000;
        const uint MEM_RESERVE = 0x00002000;
        const uint PAGE_READWRITE = 4;

        // cached login token lifetime, in hours
        const int DefaultCacheHours = 0;    // no cache
        const int MaxCacheHours = 48;

        static string ExeDirectory()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch { }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        static void Diag(string message)
        {
            OutputDebugString("[SWTORWrapper] " + message + Environment.NewLine);
        }

        /// <summary>
        /// Minimal ini reader. File.ReadAllLines handles UTF-8, UTF-16 with a BOM, and both
        /// CRLF and LF. GetPrivateProfileInt only copes with ANSI and fails silently on
        /// anything else, which is indistinguishable from the key being absent.
        /// </summary>
        static int ReadIniInt(string iniPath, string section, string key, int fallback)
        {
            try
            {
                if (!File.Exists(iniPath)) return fallback;

                string current = string.Empty;
                foreach (string raw in File.ReadAllLines(iniPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        current = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int value;
                    if (int.TryParse(line.Substring(eq + 1).Trim(), out value))
                        return value;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Hours a cached login token may be reused, from SWTORWrapper.ini beside the executable.
        /// 0 disables the cache entirely; anything above 48 is clamped to 48.
        /// </summary>
        static int GetCacheHours()
        {
            string iniPath = Path.Combine(ExeDirectory(), "SWTORWrapper.ini");
            int hours = ReadIniInt(iniPath, "launcher", "CacheHours", DefaultCacheHours);

            int clamped = hours;
            if (clamped < 0) clamped = 0;
            if (clamped > MaxCacheHours) clamped = MaxCacheHours;

            Diag(string.Format("ini={0} exists={1} CacheHours={2} -> {3}",
                               iniPath, File.Exists(iniPath), hours, clamped));
            return clamped;
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //Watch mode: something else starts the game (Steam, a shortcut, launcher.exe
            //directly) and we only inject when swtor.exe appears.
            foreach (string arg in args)
            {
                if (arg.Equals("-watch", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--watch", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/watch", StringComparison.OrdinalIgnoreCase))
                {
                    RunWatcher();
                    return;
                }
            }

            bool forceLauncher = false;
            foreach (string arg in args)
            {
                string lowerArg = arg.ToLower();
                if (lowerArg == "-ignorecache" || lowerArg == "--ignorecache" || lowerArg == "/ignorecache")
                {
                    forceLauncher = true;
                    break;
                }
            }

            int cacheHours = GetCacheHours();

            string cacheFile = Path.Combine(ExeDirectory(), "swtor_cmdline_cache.txt");
            bool useCache = false;
            string cachedCmd = null;

            if (!forceLauncher && cacheHours > 0 && File.Exists(cacheFile))
            {
                try
                {
                    string[] lines = File.ReadAllLines(cacheFile);
                    if (lines.Length >= 2)
                    {
                        if (DateTime.TryParse(lines[0], out DateTime cachedTime))
                        {
                            cachedTime = cachedTime.ToUniversalTime();
                            if ((DateTime.UtcNow - cachedTime).TotalHours < cacheHours)
                            {
                                useCache = true;
                                cachedCmd = lines[1];
                            }
                        }
                    }
                }
                catch { }
            }

            Process proc = new Process();
            bool updateCache = false;

            if (useCache && !string.IsNullOrEmpty(cachedCmd))
            {
                string executablePath;
                string launchArgs = GetCommandLineArguments(cachedCmd, out executablePath);

                proc.StartInfo.FileName = executablePath;
                proc.StartInfo.Arguments = launchArgs;
                try
                {
                    proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(executablePath);
                }
                catch { }
            }
            else
            {
                //Start the SWTOR launcher
                proc.StartInfo.FileName = "launcher.exe";
                updateCache = true;
            }

            proc.StartInfo.UseShellExecute = true;
            try
            {
                proc.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start process: " + ex.Message);
                Application.Exit();
                return;
            }

            //Start looking for the swtor.exe process (blocking)
            Diag(string.Format("useCache={0} updateCache={1} cacheFile={2}",
                               useCache, updateCache, cacheFile));
            WaitToInject(proc, updateCache && cacheHours > 0);

            //All done
            Application.Exit();
        }

        static string GetCommandLineArguments(string commandLine, out string executablePath)
        {
            executablePath = commandLine;
            string args = "";
            if (string.IsNullOrEmpty(commandLine)) return args;

            commandLine = commandLine.Trim();
            if (commandLine.StartsWith("\""))
            {
                int quoteEnd = commandLine.IndexOf('"', 1);
                if (quoteEnd != -1)
                {
                    executablePath = commandLine.Substring(1, quoteEnd - 1);
                    args = commandLine.Substring(quoteEnd + 1).Trim();
                }
            }
            else
            {
                int spaceIndex = commandLine.IndexOf(' ');
                if (spaceIndex != -1)
                {
                    executablePath = commandLine.Substring(0, spaceIndex);
                    args = commandLine.Substring(spaceIndex + 1).Trim();
                }
            }
            return args;
        }

        static string GetCommandLine(int processId)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId))
            using (ManagementObjectCollection objects = searcher.Get())
            {
                foreach (ManagementBaseObject obj in objects)
                {
                    return obj["CommandLine"]?.ToString();
                }
            }
            return null;
        }

        static void WaitToInject(Process pLauncher, bool updateCache)
        {
            bool injected = false;
            string dllName = Path.Combine(ExeDirectory(), "SWTORFix.dll");
            while (!injected)
            {
                Process[] pList = Process.GetProcessesByName("swtor");
                if (pList.Length > 0) //Make sure we found a process
                {
                    //Loop through processes
                    for (int i = 0; i < pList.Length; i++)
                    {
                        InjectDLL(pList[i], dllName);

                        if (updateCache)
                        {
                            try
                            {
                                string cmdLine = GetCommandLine(pList[i].Id);
                                if (!string.IsNullOrEmpty(cmdLine))
                                {
                                    string cacheFile = Path.Combine(ExeDirectory(), "swtor_cmdline_cache.txt");
                                    File.WriteAllLines(cacheFile, new string[] { DateTime.UtcNow.ToString("o"), cmdLine });
                                    Diag("cache written: " + cacheFile);
                                }
                                else
                                {
                                    Diag("WMI returned no command line for pid " + pList[i].Id);
                                }
                            }
                            catch (Exception ex)
                            {
                                Diag("cache write failed: " + ex.Message);
                            }
                        }
                    }

                    //Processes were injected, so we're done
                    injected = true;
                    return;
                }

                //Wait 200 ms, then try again
                System.Threading.Thread.Sleep(200);

                //Check if the launcher died
                try
                {
                    if (pLauncher.HasExited)
                    {
                        //If the launcher has exited for more than 10 seconds, exit
                        if ((DateTime.Now - pLauncher.ExitTime).TotalSeconds > 10)
                        {
                            injected = true;
                            return;
                        }
                    }
                }
                catch
                {
                    // pLauncher might have been closed or access denied
                }
            }
        }
        /// <summary>
        /// Watch mode. Sits in the tray and injects into any swtor.exe that appears,
        /// no matter what started it. Nothing is launched.
        /// </summary>
        static void RunWatcher()
        {
            string dllName = Path.Combine(ExeDirectory(), "SWTORFix.dll");
            HashSet<int> injected = new HashSet<int>();
            bool reported = false;

            NotifyIcon tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            try
            {
                tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }
            tray.Text = "SWTORFix - waiting for the game";
            tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (s, e) =>
            {
                tray.Visible = false;
                Application.Exit();
            });
            tray.ContextMenuStrip = menu;

            Timer timer = new Timer();
            timer.Interval = 1000;
            timer.Tick += (s, e) =>
            {
                HashSet<int> alive = new HashSet<int>();
                foreach (Process p in Process.GetProcessesByName("swtor"))
                {
                    try
                    {
                        alive.Add(p.Id);
                        if (!injected.Contains(p.Id))
                        {
                            InjectDLL(p, dllName);
                            injected.Add(p.Id);

                            if (!reported)
                            {
                                reported = true;
                                tray.Text = "SWTORFix - injected";
                            }
                        }
                    }
                    catch
                    {
                        // process exited between enumeration and injection
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                //Forget exited processes so a reused PID gets injected again
                injected.IntersectWith(alive);
            };
            timer.Start();

            Application.Run();
        }

        static void InjectDLL(Process targetProcess, String dllName)
        {
            // geting the handle of the process - with required privileges
            IntPtr procHandle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, targetProcess.Id);

            // searching for the address of LoadLibraryA and storing it in a pointer
            IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");

            // alocating some memory on the target process - enough to store the name of the dll
            // and storing its address in a pointer
            IntPtr allocMemAddress = VirtualAllocEx(procHandle, IntPtr.Zero, (uint)((dllName.Length + 1) * Marshal.SizeOf(typeof(char))), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

            // writing the name of the dll there
            UIntPtr bytesWritten;
            WriteProcessMemory(procHandle, allocMemAddress, System.Text.Encoding.Default.GetBytes(dllName), (uint)((dllName.Length + 1) * Marshal.SizeOf(typeof(char))), out bytesWritten);

            // creating a thread that will call LoadLibraryA with allocMemAddress as argument
            CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
        }
    }
}
