using Lertaro.Core.Services.Installation;

namespace Lertaro.App.Services.Pipe;

internal static class AppPipeNames
{
    public static string ActivationPipeName => Build("Lertaro_App_Pipe", CurrentUserIdentity.SessionHash);

    public static string SearchPipeName => Build("Lertaro_App_Search_Pipe", CurrentUserIdentity.SessionHash);

    internal static string Build(string prefix, string sessionHash) => $"{prefix}_{sessionHash}";
}
