using MemoryRestriction;
using System.Diagnostics;
using System.ServiceProcess;

ServiceBase.Run(new MemoryLimitService());

