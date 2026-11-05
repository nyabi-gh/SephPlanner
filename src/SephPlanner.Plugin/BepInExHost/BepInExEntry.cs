using System.IO;
using BepInEx;
using SephPlanner.Plugin.Host;

namespace SephPlanner.Plugin.BepInExHost
{
    /// <summary>
    /// 플래너를 이 오브젝트에 그대로 붙인다 - BepInEx 의 관리 오브젝트는 장면이 바뀌어도 남는다. GUID 는 설정 파일 이름이라
    /// 바꾸지 않는다(<see cref="PluginIdentity.Guid"/>).
    /// </summary>
    [BepInPlugin(PluginIdentity.Guid, "SephPlanner", "0.6.2")]
    public sealed class BepInExEntry : BaseUnityPlugin
    {
        private void Awake()
        {
            Log.Use((level, message) =>
            {
                switch (level)
                {
                    case LogLevel.Error: Logger.LogError(message); break;
                    case LogLevel.Warning: Logger.LogWarning(message); break;
                    default: Logger.LogInfo(message); break;
                }
            });
            PluginHost.Use(new BepInExSettingStore(Config),
                "BepInEx " + typeof(BaseUnityPlugin).Assembly.GetName().Version,
                Path.Combine("BepInEx", "LogOutput.log"), "");
            gameObject.AddComponent<SephPlannerPlugin>();
        }
    }
}
