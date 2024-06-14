using Microsoft.Extensions.Logging;
using System;

namespace UpdateDevices.Extensions
{
    internal static class LoggerExtensions
    {
        public static void DSLogCritical(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogCritical("[CRITICAL} ({methodName}) " + message, methodName);

        }

        public static void DSLogError(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogError("[ERROR] ({methodName}) " + message, methodName);
        }

        public static void DSLogInformation(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogInformation("[INFO] ({methodName}) " + message, methodName);
        }

        public static void DSLogWarning(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogWarning("[WARNING] ({methodName}) " + message, methodName);
        }

        public static void DSLogDebug(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogDebug("[DEBUG] ({methodName}) " + message, methodName);
        }
        public static void DSLogTrace(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogTrace("[TRACE] ({methodName}) " + message, methodName);
        }

        public static void DSLogException(this ILogger logger, string? message, Exception ex, string methodName = "")
        {
            logger.LogError("[EXCEPTION] ({methodName}) " + message + " " + ex.Message, methodName);
        }

        public static void DSLogAudit(this ILogger logger, string? message, string methodName = "")
        {
            logger.LogInformation("[AUDIT] ({methodName}) " + message, methodName);
        }

    }

}
