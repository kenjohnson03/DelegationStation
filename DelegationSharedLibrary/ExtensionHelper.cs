using System.Runtime.CompilerServices;

namespace DelegationSharedLibrary
{
    public static class ExtensionHelper
    {
        public static string GetMethodName([CallerMemberName] string methodName = null) => methodName;
    }
}
