using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

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

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //Start the SWTOR launcher
            Process proc = new Process();
            proc.StartInfo.FileName = "launcher.exe";
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
            WaitToInject(proc);

            //All done
            Application.Exit();
        }

        static void WaitToInject(Process pLauncher)
        {
            bool injected = false;
            string dllName = System.IO.Directory.GetCurrentDirectory() + "\\SWTORFix.dll";
            while (!injected)
            {
                Process[] pList = Process.GetProcessesByName("swtor");
                if (pList.Length > 0) //Make sure we found a process
                {
                    //Loop through processes
                    for (int i = 0; i < pList.Length; i++)
                    {
                        InjectDLL(pList[i], dllName);
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
