using System.Diagnostics;
using System.Resources;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Windows.UI.Notifications;


namespace MemoryRestriction
{
    public class MemoryLimitService : ServiceBase
    {
#pragma warning disable CA1416
        private const long MEMORY_LIMIT = 250 * 1024 * 1024; // 200 MB
        private const string PROCESS_NAME = "AdGuardVpnSvc";
        private const string PROCESS_PATH = "C:\\Program Files\\AdGuardVpn\\AdGuardVpnSvc.exe";
        private Thread monitorThread;
        private bool isRunning = true;
        private readonly ResourceManager RM;
        private new readonly EventLog EventLog;
        public MemoryLimitService(ResourceManager resourceManager) : base()
        {
            RM = resourceManager;

            if (!EventLog.SourceExists("Memory Limit Service"))
            {
                EventLog.CreateEventSource("Memory Limit Service", "MemoryLimit");
            }

            EventLog = new EventLog("MemoryLimit")
            {
                Source = "Memory Limit Service"
            };
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern nint CreateJobObject(nint lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetInformationJobObject(nint hJob, int JobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public int LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public int ActiveProcessLimit;
            public nint Affinity;
            public int PriorityClass;
            public int SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public long ProcessMemoryLimit;
            public long JobMemoryLimit;
            public long PeakProcessMemoryUsed;
            public long PeakJobMemoryUsed;
        }

        const int JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
        const int JobObjectExtendedLimitInformation = 9;

        protected override void OnStart(string[] args)
        {
            isRunning = true;
            monitorThread = new Thread(MonitorProcess) { IsBackground = true };
            monitorThread.Start();
            EventLog.WriteEntry(RM.GetString("ServiceStarted"));
        }

        protected override void OnStop()
        {
            isRunning = false;
            monitorThread?.Join();
            EventLog.WriteEntry(RM.GetString("ServiceStopped"));
            ShowWindowsNotification(RM.GetString("SerivceStoppedNotify"));
        }

        private void MonitorProcess()
        {
            try
            {
                while (isRunning)
                {
                    Process process = FindProcess(PROCESS_NAME);

                    if (process == null)
                    {
                        EventLog.WriteEntry(string.Format(RM.GetString("ProcessNotFound"), PROCESS_NAME));
                        RestartProcess();
                        Thread.Sleep(15000);
                        continue;
                    }

                    nint hJob = CreateJobObject(nint.Zero, null);
                    if (hJob == nint.Zero)
                    {
                        EventLog.WriteEntry(RM.GetString("ErrorCreating"));
                        return;
                    }

                    if (!AssignProcessToJobObject(hJob, process.Handle))
                    {
                        EventLog.WriteEntry(RM.GetString("ErrorAssigning"));
                        return;
                    }

                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobInfo = new();
                    jobInfo.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
                    jobInfo.ProcessMemoryLimit = MEMORY_LIMIT;

                    if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ref jobInfo, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                    {
                        EventLog.WriteEntry(RM.GetString("ErrorRamSetting"));
                        return;
                    }

                    EventLog.WriteEntry(string.Format(RM.GetString("SetLimit"), PROCESS_NAME));

                    while (!process.HasExited && isRunning)
                    {
                        process.Refresh();
                        long memoryUsage = process.WorkingSet64;

                        if (memoryUsage > MEMORY_LIMIT)
                        {
                            string message = string.Format(RM.GetString("LimitExceeded"), memoryUsage / (1024 * 1024));
                            EventLog.WriteEntry(message);
                            ShowWindowsNotification(message);
                            process.Kill();
                            process.WaitForExit();
                            RestartProcess();
                            break;
                        }

                        Thread.Sleep(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                string message = RM.GetString("Exception") + ex.GetType().ToString() + Environment.NewLine + ex.Message;
                EventLog.WriteEntry(message + ex.StackTrace?.ToString(), EventLogEntryType.Error);
                ShowWindowsNotification(message);
            }
        }

        private Process FindProcess(string name)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                return process;
            }
            return null;
        }

        private void RestartProcess()
        {
            try
            {
                Process.Start(PROCESS_PATH);
                EventLog.WriteEntry(string.Format(RM.GetString("StartingProcess"), PROCESS_NAME));
            }
            catch (Exception ex)
            {
                ShowWindowsNotification("Служба была остановлена.");
                EventLog.WriteEntry(string.Format(RM.GetString("ErrorStartingProcess"), PROCESS_NAME, ex.Message));
            }
        }

        private void ShowWindowsNotification(string message)
        {
            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textElements = toastXml.GetElementsByTagName("text");
            textElements[0].AppendChild(toastXml.CreateTextNode(PROCESS_NAME));
            textElements[1].AppendChild(toastXml.CreateTextNode(message));

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("MemoryLimitService").Show(toast);
        }
    }
}