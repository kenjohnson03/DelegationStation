using System.Runtime.CompilerServices;

namespace DelegationStationShared
{
    public static class ExtensionHelper
    {
        public static string? GetMethodName([CallerMemberName] string? methodName = null) => methodName;
    }
}
