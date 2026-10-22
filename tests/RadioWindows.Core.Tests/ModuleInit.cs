using System.Runtime.CompilerServices;
using RadioWindows.Core;

namespace RadioWindows.Core.Tests;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Initialize() => Loc.Initialize("ru");
}
