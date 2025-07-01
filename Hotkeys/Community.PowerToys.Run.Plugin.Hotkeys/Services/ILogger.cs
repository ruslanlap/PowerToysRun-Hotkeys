// ===== 5. ILogger.cs =====
using System;

namespace Community.PowerToys.Run.Plugin.Hotkeys.Services
{
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception exception = null);
        void LogDebug(string message);
    }
}