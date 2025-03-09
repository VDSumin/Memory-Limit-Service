### Configuration
* To set the RAM limit, modify the **MEMORY_LIMIT** constant.
* The path to the target application is defined in the **PROCESS_PATH** constant.
* **PROCESS_NAME** is used for searching process and logging.
### Add Service
1. Publish project
2. Create Service in Power Shell 7+ (<code>Install-Module -Name Microsoft.PowerShell.Management -Force -Scope CurrentUser</code> for start, stop, remove needed)
```
  New-Service -Name "Memory Limit Service" -Binary ".\bin\Release\net8.0\publish\win-x64\MemoryRestriction.exe" -StartupType Automatic
  Start-Service -Name "Memory Limit Service"
```
### Remove Service
```
  Stop-Service -Name "Memory Limit Service"
  Remove-Service -Name "Memory Limit Service"
```
### Logging and notification
The log is located in the Windows **Event Log** under **Application and Services Logs** with the name **MemoryLimit**.

The application will create a desktop notification, when the memory limit is exceeded, and when the application is restarted.

### Develop in Visual Studio 2022
In Visual Studio Installer add following:
1. Universal Windows Platform development
2. Windows SDK > 10.19...
