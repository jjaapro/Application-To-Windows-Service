using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Management;

namespace ATWS
{
    class Program
    {
        /*
        This header is used by Security and Identity.
        https://docs.microsoft.com/en-us/windows/win32/api/winsvc/
        */
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr OpenSCManager(string lpMachineName, string lpSCDB, int scParameter);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr CreateService(IntPtr SC_HANDLE, string lpSvcName, string lpDisplayName, int dwDesiredAccess, int dwServiceType, int dwStartType, int dwErrorControl, string lpPathName, string lpLoadOrderGroup, int lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword);
        [DllImport("advapi32.dll")] public static extern void CloseServiceHandle(IntPtr SCHANDLE);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern int StartService(IntPtr SVHANDLE, int dwNumServiceArgs, string lpServiceArgVectors);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern int DeleteService(IntPtr SVHANDLE);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr OpenService(IntPtr SCHANDLE, string lpSvcName, int dwNumServiceArgs);
        [DllImport("kernel32.dll")] public static extern int GetLastError();

        static void Main(string[] args)
        {
            var options = args.ToList();
            if (options.Count() > 0 && options[0].ToLower() == "apptowinservice")
            {
                var executable = options[1];
                options.RemoveRange(0, Math.Min(2, options.Count()));

                ServiceBase.Run(new Service(executable, options.ToArray()));
            }
            else if (options.Count() > 0)
            {
                for (int i = 0; i < options.Count(); i += 2)
                {
                    switch (options[i].ToLower())
                    {
                        case "--install":
                            var j = options.FindIndex(item => item.ToLower().Contains("--path"));
                            if (j == -1 || j + 1 == options.Count())
                            {
                                Console.WriteLine("You must specify the path to a console application executable.");
                                return;
                            }
                            var name = options[i + 1];
                            var executable = options[j + 1];
                            var sep = ';';

                            if (!ValidatePath(executable))
                            {
                                Console.WriteLine("Path was not valid");
                                return;
                            }

                            if (Any(ServiceController.GetServices(), o => o.ServiceName.Trim().ToLower() == name.ToLower()))
                            {
                                Console.WriteLine("A service with that name already exists");
                                return;
                            }

                            var pathWithArguments = $"\"{Assembly.GetExecutingAssembly().Location}\" apptowinservice \"{executable}\"";

                            var k = options.FindIndex(item => item.ToLower().Contains("--sep"));
                            if (k != -1 && k + 1 < options.Count() && options[k + 1].Length == 1)
                                sep = Convert.ToChar(options[k + 1]);

                            var l = options.FindIndex(item => item.ToLower().Contains("--arguments"));
                            if (l != -1 && l + 1 < options.Count())
                            {
                                string[] arguments = options[l + 1].Split(sep);
                                pathWithArguments += $" {string.Join(" ", arguments.Select(x => string.Format("\"{0}\"", x)))}";
                            }

                            InstallService(pathWithArguments, name);
                            break;
                        case "--uninstall":
                            if (!Any(ServiceController.GetServices(), o => o.ServiceName.Trim().ToLower() == options[i + 1].Trim().ToLower()))
                            {
                                Console.WriteLine($"No service with the name '{options[i + 1]}' exists.");
                                return;
                            }
                            if (new ServiceController(options[i + 1]).Status != ServiceControllerStatus.Stopped)
                            {
                                try
                                {
                                    ServiceControl(options[i + 1], true);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e.Message);
                                    return;
                                }
                            }
                            try
                            {
                                UninstallService(options[i + 1]);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                                return;
                            }
                            break;
                        case "--start":
                            try
                            {
                                ServiceControl(options[i + 1], false);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                                return;
                            }
                            break;
                        case "--stop":
                            try
                            {
                                ServiceControl(options[i + 1], true);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                                return;
                            }
                            break;
                        case "--help":
                        case "help":
                        case "?":
                        case "-?":
                            Help();
                            break;
                    }
                }
            }
            else
            {
                Help();
            }
        }

        private static void Help()
        {
            Console.WriteLine(@"
--install          >> atws --install ""My Console Application Service"" --path ""C:\Program Files\MyApp\App.exe"" --sep "";"" --arguments ""arg 0;arg 1;arg 2;arg 3""
                   >> Arguments --install and --path are required when installing new service. Default separator for the arguments is "";"".
--uninstall        >> atws --uninstall ""My Console Application Service""
--start            >> atws --start ""My Console Application Service""
--stop             >> atws --stop ""My Console Application Service""
            ");
        }

        private static bool ValidatePath(string path)
        {
            var extension = Path.GetExtension(path);
            return File.Exists(path) && (extension == ".exe" || extension == ".bat");
        }

        public static bool Any<T>(IEnumerable<T> @this, Func<T, bool> condition)
        {
            foreach (var element in @this)
            {
                if (condition(element)) return true;
            }
            return false;
        }

        private static void InstallService(string path, string name)
        {
            var sc = OpenSCManager(null, null, 0x0002);
            if (sc.ToInt32() == 0)
            {
                var errorCode = GetLastError();
                if (errorCode == 5) throw new Exception("Could not install service, you have to run this installer as an administrator");
                throw new Exception(GetLastError().ToString());
            }

            /*
            Creates a service object and adds it to the specified service control manager database.
            https://docs.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-createservicea
            https://docs.microsoft.com/en-us/windows/win32/services/installing-a-service
            */
            var service = CreateService(
                sc,
                name,
                name,
                (0xF0000 | 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x0010 | 0x0020 | 0x0040 | 0x0080 | 0x0100),
                0x00000010,
                0x00000002,
                0x00000001,
                path,
                null,
                0,
                null,
                null,
                null);

            if (service.ToInt32() == 0)
            {
                CloseServiceHandle(sc);
                throw new Exception();
            }

            CloseServiceHandle(sc);

            Console.WriteLine("Service installed");
        }

        private static void UninstallService(string name)
        {
            var sc = OpenSCManager(null, null, 0x40000000);
            if (sc.ToInt32() == 0)
            {
                throw new Exception(GetLastError().ToString());
            }

            var service = OpenService(sc, name, 0x10000);
            if (service.ToInt32() == 0)
            {
                var errorCode = GetLastError();
                if (errorCode == 1060) throw new Exception("No service with that name exist.");
                throw new Exception("Error calling OpenService() - " + errorCode.ToString());
            }

            /*
            Marks the specified service for deletion from the service control manager database.
            https://docs.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-deleteservice
            */
            var code = DeleteService(service);
            if (code == 0)
            {
                var errorCode = GetLastError();
                CloseServiceHandle(sc);
                if (errorCode == 1072) throw new Exception("Service already marked for deletion");
                throw new Exception("Error calling DeleteService() - " + errorCode.ToString());
            }

            CloseServiceHandle(sc);

            Console.WriteLine("Service uninstalled");
        }

        private static void ServiceControl(string name, bool stop)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo() { UseShellExecute = false, RedirectStandardOutput = true };
            info.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            info.FileName = "cmd.exe";

            if (stop)
                info.Arguments = $"/C net stop \"{name}\"";
            else
                info.Arguments = $"/C net start \"{name}\"";

            process.StartInfo = info;
            process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine(e.Data);
                }
            });

            process.Start();

            process.BeginOutputReadLine();
            process.WaitForExit();
        }
    }
    public partial class Service : ServiceBase
    {
        private Process process;
        private string executable;
        private string[] arguments;
        private string outDir = "logs\\out.txt";
        private string errDir = "logs\\err.txt";

        public Service(string executable, string[] arguments)
        {
            this.executable = executable;
            this.arguments = arguments;
        }

        protected override void OnStart(string[] args)
        {
            var outPath = Path.Combine(Path.GetDirectoryName(executable), outDir);
            var errPath = Path.Combine(Path.GetDirectoryName(executable), errDir);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            }
            catch (Exception)
            {
            }

            process = new Process();

            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/C cd \"{Path.GetDirectoryName(executable)}\" && \"{Path.GetFileName(executable)}\" {string.Join(" ", arguments.Select(x => string.Format("\"{0}\"", x)))}";

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.ErrorDialog = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            process.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    try
                    {
                        if (File.Exists(errPath) && File.ReadAllBytes(errPath).Length >= 1024000)
                        {
                            File.Copy(errPath, $"{Path.GetDirectoryName(errPath)}\\{Path.GetFileNameWithoutExtension(errPath)}-{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}{Path.GetExtension(errPath)}");
                            File.Delete(errPath);
                        }
                        using (StreamWriter w = File.AppendText(errPath))
                        {
                            w.WriteLine($"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()} : {e.Data}");
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            });

            process.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    try
                    {
                        if (File.Exists(outPath) && File.ReadAllBytes(outPath).Length >= 1024000)
                        {
                            File.Copy(outPath, $"{Path.GetDirectoryName(outPath)}\\{Path.GetFileNameWithoutExtension(outPath)}-{DateTime.Now.ToShortDateString()}-{DateTime.Now.ToLongTimeString()}{Path.GetExtension(outPath)}");
                            File.Delete(outPath);
                        }
                        using (StreamWriter w = File.AppendText(outPath))
                        {
                            w.WriteLine($"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()} : {e.Data}");
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            });

            process.Start();

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        protected override void OnStop()
        {
            try
            {
                List<Process> children = new List<Process>();
                ManagementObjectSearcher mos = new ManagementObjectSearcher(String.Format("Select * From Win32_Process Where ParentProcessID={0}", process.Id));

                foreach (ManagementObject mo in mos.Get())
                {
                    children.Add(Process.GetProcessById(Convert.ToInt32(mo["ProcessID"])));
                }

                process.Kill();
                process = null;

                foreach (Process child in children)
                {
                    child.Kill();
                }
            }
            catch (Exception e)
            {
                EventLog.WriteEntry("Application", e.ToString(), EventLogEntryType.Error);
            }
        }
    }
}
