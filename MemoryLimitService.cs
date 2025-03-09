using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;


namespace MemoryRestriction
{
    public class MemoryLimitService : ServiceBase
    {
#pragma warning disable CA1416 // Проверка совместимости платформы
        private const long MEMORY_LIMIT = 200 * 1024 * 1024; // 200 MB
        private const string PROCESS_NAME = "AdGuardVpnSvc";
        private const string PROCESS_PATH = "C:\\Program Files\\AdGuardVpn\\AdGuardVpnSvc.exe";
        private const short MY_EVENT_CATEGORY_ID = 322;
        private Thread monitorThread;
        private bool isRunning = true;

        private new readonly EventLog EventLog;
        public MemoryLimitService() : base()
        {
            // Убедитесь, что создаем источник события только один раз
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
            EventLog.WriteEntry("MemoryLimitService запущена.");
        }

        protected override void OnStop()
        {
            isRunning = false;
            monitorThread?.Join();
            EventLog.WriteEntry("MemoryLimitService остановлена.");
            ShowWindowsNotification("AdGuard VPN", "Служба была остановлена.");
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
                        EventLog.WriteEntry($"Процесс {PROCESS_NAME} не найден, перезапускаем...");
                        RestartProcess();
                        Thread.Sleep(15000);
                        continue;
                    }

                    nint hJob = CreateJobObject(nint.Zero, null);
                    if (hJob == nint.Zero)
                    {
                        EventLog.WriteEntry("Ошибка создания Job Object!");
                        return;
                    }

                    if (!AssignProcessToJobObject(hJob, process.Handle))
                    {
                        EventLog.WriteEntry("Ошибка назначения процесса в Job Object!");
                        return;
                    }

                    JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobInfo = new();
                    jobInfo.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
                    jobInfo.ProcessMemoryLimit = MEMORY_LIMIT;

                    if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ref jobInfo, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                    {
                        EventLog.WriteEntry("Ошибка установки ограничения памяти!");
                        return;
                    }

                    EventLog.WriteEntry($"Ограничение памяти для {PROCESS_NAME} установлено.");

                    while (!process.HasExited && isRunning)
                    {
                        process.Refresh();
                        long memoryUsage = process.WorkingSet64;

                        if (memoryUsage > MEMORY_LIMIT)
                        {
                            string message = $"Превышен лимит памяти! ({memoryUsage / (1024 * 1024)} MB). Перезапускаем процесс...";
                            EventLog.WriteEntry(message);
                            ShowWindowsNotification("AdGuard VPN", message);
                            process.Kill();
                            process.WaitForExit();
                            RestartProcess();
                            break;
                        }

                        Thread.Sleep(5000);
                    }
                }
            }
            catch(Exception ex)
            {
                EventLog.WriteEntry("Возникло исключение: " + ex.Message + Environment.NewLine + ex.StackTrace?.ToString(),EventLogEntryType.Error);
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
                EventLog.WriteEntry($"Запускаем процесс {PROCESS_NAME}.");
            }
            catch (Exception ex)
            {
                ShowWindowsNotification("AdGuard VPN", "Служба была остановлена.");
                EventLog.WriteEntry($"Ошибка при запуске процесса {PROCESS_NAME}: {ex.Message}");
            }
        }

        private void ShowWindowsNotification(string title, string message)
        {
            var toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textElements = toastXml.GetElementsByTagName("text");
            textElements[0].AppendChild(toastXml.CreateTextNode(title));
            textElements[1].AppendChild(toastXml.CreateTextNode(message));

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("MemoryLimitService").Show(toast);
        }
    }
}