// ===== 6. PowerToysLogger.cs =====
using System;
using ManagedCommon;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public class PowerToysLogger : ILogger
    {
        public void LogInfo(string message)
        {
            Logger.LogInfo(message);
        }

        public void LogWarning(string message)
        {
            Logger.LogWarning(message);
        }

        public void LogError(string message, Exception exception = null)
        {
            if (exception != null)
                Logger.LogError(message, exception);
            else
                Logger.LogError(message);
        }

        public void LogDebug(string message)
        {
            Logger.LogDebug(message);
        }
    }
}
