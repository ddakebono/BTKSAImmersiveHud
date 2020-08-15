using Harmony;
using Il2CppSystem.Threading;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Transmtn.DTO.Notifications;
using UnhollowerBaseLib.Runtime;
using UnhollowerRuntimeLib;
using UnityEngine;
using UnityEngine.UI;

namespace BTKSAImmersiveHud
{
    public static class BuildInfo
    {
        public const string Name = "BTKSAImmersiveHud";
        public const string Author = "DDAkebono#0001";
        public const string Company = "BTK-Development";
        public const string Version = "1.1.0";
        public const string DownloadLink = "https://github.com/ddakebono/BTKSAImmersiveHud/releases";
    }

    public class BTKSAImmersiveHud : MelonMod
    {
        public static BTKSAImmersiveHud instance;

        public static string settingsCategory = "BTKSAImmersiveHud";
        public static string hudEnable = "hudEnable";
        public static string hudTimeout = "hudTimeout";

        public HarmonyInstance harmony;

        public static List<string> recentUnhideEvents = new List<string>();

        private string[] defaultsInNotificationDot =
        {
            "NotificationDot",
            "VoteKickDot",
            "FriendRequestDot",
            "InviteDot",
            "InviteRequestDot"
        };

        private List<MonitoredObject> customHudObjects = new List<MonitoredObject>();
        private GameObject hudContent;
        private float hudCurrentTimeout = 0f;
        private bool shownHud = false;
        private bool enableImmersiveHud = false;
        private bool scannedCustomHud = false;

        public override void VRChat_OnUiManagerInit()
        {
            MelonLogger.Log("BTK Standalone: Immersive Hud - Starting Up");

            instance = this;

            if (Directory.Exists("BTKCompanion"))
            {
                MelonLogger.Log("Woah, hold on a sec, it seems you might be running BTKCompanion, if this is true ImmversiveHud is built into that, and you should not be using this!");
                MelonLogger.Log("If you are not currently using BTKCompanion please remove the BTKCompanion folder from your VRChat installation!");
                MelonLogger.LogError("ImmersiveHud has not started up! (BTKCompanion Exists)");
                return;
            }

            MelonPrefs.RegisterCategory(settingsCategory, "Immersive Hud");
            MelonPrefs.RegisterBool(settingsCategory, hudEnable, true, "Immersive Hud Enable");
            MelonPrefs.RegisterFloat(settingsCategory, hudTimeout, 10f, "Hud Appear Duration");

            //Register our MonoBehavior to let us use OnEnable
            ClassInjector.RegisterTypeInIl2Cpp<HudEvent>();

            harmony = HarmonyInstance.Create("BTKStandaloneIH");
            //Mute/Unmute Hook
            harmony.Patch(typeof(DefaultTalkController).GetMethod("Method_Public_Static_Void_Boolean_0", BindingFlags.Public | BindingFlags.Static), null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnHudUpdateEvent", BindingFlags.Public | BindingFlags.Static)));
            //GestureLock Hook
            harmony.Patch(typeof(HandGestureController).GetMethod("Method_Public_Static_Void_Boolean_0", BindingFlags.Public | BindingFlags.Static), null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnHudUpdateEvent", BindingFlags.Public | BindingFlags.Static)));
            //World join hook to detect for first world join
            foreach (MethodInfo method in typeof(RoomManagerBase).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name.Contains("Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_"))
                    harmony.Patch(method, null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnWorldJoin", BindingFlags.Static | BindingFlags.Public)));
            }

            IEnumerable<MethodInfo> addNotificationMethods = typeof(NotificationManager).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(
                m => m.Name.StartsWith("Method_Public_Void_Notification_Enum")
                     && m.GetParameters().Length == 2
                     && m.GetParameters()[0].ParameterType
                     == typeof(Notification));
            foreach (MethodInfo methodInfo in addNotificationMethods)
                harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnNotification", BindingFlags.Public | BindingFlags.Static)));

            hudContent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud");
            if (hudContent != null)
            {
                MelonLogger.Log("Found HudContent gameobject");
                HudEvent behavior = hudContent.AddComponent<HudEvent>();
                behavior.onEnableListeners.Add(OnMenuEnable);
                OnModSettingsApplied();
            }
        }

        public override void OnModSettingsApplied()
        {
            enableImmersiveHud = MelonPrefs.GetBool(settingsCategory, hudEnable);
            shownHud = !enableImmersiveHud;
            hudContent.SetActive(!enableImmersiveHud);
            hudCurrentTimeout = 0;
        }

        public override void OnUpdate()
        {
            foreach(MonitoredObject hudItem in customHudObjects)
            {
                if (hudItem.CheckState())
                    showHud();
            }

            if (enableImmersiveHud)
            {
                if (!shownHud && hudCurrentTimeout > 0)
                {
                    shownHud = true;
                    hudContent.SetActive(true);
                }

                if (shownHud && hudCurrentTimeout <= 0)
                {
                    shownHud = false;
                    hudContent.SetActive(false);
                }
                else
                {
                    hudCurrentTimeout -= Time.deltaTime;
                }
            }
        }

        public static void OnHudUpdateEvent()
        {
            instance.showHud();
        }

        public static void OnNotification(Notification __0)
        {
            if (!recentUnhideEvents.Contains(__0.id))
            {
                recentUnhideEvents.Add(__0.id);
                instance.showHud();
            }
        }

        public static void OnWorldJoin()
        {
            if (!instance.scannedCustomHud)
            {
                //World join start custom hud element scan
                instance.scannedCustomHud = true;
                instance.postWorldJoinChildScan();
            }
        }

        public void postWorldJoinChildScan()
        {
            MelonLogger.Log("Searching for custom hud elements...");

            GameObject modParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/NotificationDotParent");

            for (int i = 0; i < modParent.transform.childCount; i++)
            {
                //Scan objects for non standards to attach
                Transform child = modParent.transform.GetChild(i);
                if (!defaultsInNotificationDot.Any(x => x.Equals(child.name)))
                {
                    MelonLogger.Log($"Detected Mod Hud Item {child.name}");
                    MonitoredObject newHudItem = new MonitoredObject(child.gameObject, child.gameObject.activeSelf);

                    //Patch to monitor the correct component for JoinNotifier
                    if (child.name.Equals("NotifyDot-join") || child.name.Equals("NotifyDot-leave"))
                    {
                        newHudItem.trackedComponent = child.GetComponent<Image>();
                    }

                    customHudObjects.Add(newHudItem);
                }
            }
        }

        public void showHud()
        {
            if(MelonPrefs.GetBool(settingsCategory, hudEnable))
                hudCurrentTimeout = MelonPrefs.GetFloat(settingsCategory, hudTimeout);
        }

        private void OnMenuEnable()
        {
            if (!shownHud && enableImmersiveHud)
                hudContent.SetActive(false);
        }

    }

    //Monitored GameObject object
    class MonitoredObject
    {
        public GameObject HudItem;
        public bool lastKnownState;
        //Patch to monitor JoinNotifier
        public Image trackedComponent;

        public MonitoredObject(GameObject go, bool lastKnownState)
        {
            this.HudItem = go;
            this.lastKnownState = lastKnownState;
        }

        public bool CheckState()
        {
            if (trackedComponent != null)
            {
                if(trackedComponent.enabled != lastKnownState)
                {
                    lastKnownState = trackedComponent.enabled;
                    return true;
                }
            } 
            else
            {
                if(HudItem.activeSelf != lastKnownState)
                {
                    lastKnownState = HudItem.activeSelf;
                    return true;
                }
            }

            return false;
        }
    }
}
