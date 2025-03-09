using MemoryRestriction;
using System.Resources;
using System.ServiceProcess;

var resourceManager = new ResourceManager("MemoryRestriction.Properties.Resources.Resources", typeof(Program).Assembly);

ServiceBase.Run(new MemoryLimitService(resourceManager));
