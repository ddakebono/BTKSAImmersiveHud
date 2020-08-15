using Harmony;
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

namespace BTKSAImmersiveHud
{
    public static class BuildInfo
    {
        public const string Name = "BTKSAImmersiveHud";
        public const string Author = "DDAkebono#0001";
        public const string Company = "BTK-Development";
        public const string Version = "1.0.0";
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

        private GameObject hudContent;
        private float hudCurrentTimeout = 0f;
        private bool shownHud = false;
        private bool enableImmersiveHud = false;

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
            if (!enableImmersiveHud)
            {
                shownHud = true;
                hudContent.SetActive(true);
            }
        }

        public override void OnUpdate()
        {
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

        public void showHud()
        {
            if(MelonPrefs.GetBool(settingsCategory, hudEnable))
                hudCurrentTimeout = MelonPrefs.GetFloat(settingsCategory, hudTimeout);
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

        private void OnMenuEnable()
        {
            if (!shownHud && enableImmersiveHud)
                hudContent.SetActive(false);
        }

    }
}
