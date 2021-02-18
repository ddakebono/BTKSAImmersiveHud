using Harmony;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public const string Version = "1.3.3";
        public const string DownloadLink = "https://github.com/ddakebono/BTKSAImmersiveHud/releases";
    }

    public class BTKSAImmersiveHud : MelonMod
    {
        public static BTKSAImmersiveHud instance;

        public static string settingsCategory = "BTKSAImmersiveHud";
        public static string hudEnable = "hudEnable";
        public static string hudTimeout = "hudTimeout";
        public static string hudStayOnUntilClear = "hudStayTillClear";

        public HarmonyInstance harmony;

        public static List<HudEvent> hudEventComponents = new List<HudEvent>();

        private List<MonitoredObject> customHudObjects = new List<MonitoredObject>();
        private float hudCurrentTimeout = 0f;
        private bool shownHud = false;
        private bool enableImmersiveHud = false;
        private bool scannedCustomHud = false;
        private bool notificationIsActive = false;
        private bool keepOn = false;

        //Cached Objects
        private GameObject HudContent;
        private GameObject GestureParent;
        private GameObject NotificationParent;
        private GameObject AFKParent;

        public override void VRChat_OnUiManagerInit()
        {
            MelonLogger.Msg("BTK Standalone: Immersive Hud - Starting Up");

            instance = this;

            if (MelonHandler.Mods.Any(x => x.Info.Name.Equals("BTKCompanionLoader", StringComparison.OrdinalIgnoreCase)))
            {
                MelonLogger.Msg("Hold on a sec! Looks like you've got BTKCompanion installed, this mod is built in and not needed!");
                MelonLogger.Error("BTKSAImmersiveHud has not started up! (BTKCompanion Running)");
                return;
            }

            MelonPreferences.CreateCategory(settingsCategory, "Immersive Hud");
            MelonPreferences.CreateEntry<bool>(settingsCategory, hudEnable, true, "Immersive Hud Enable");
            MelonPreferences.CreateEntry<bool>(settingsCategory, hudStayOnUntilClear, false, "Keep Hud Visible Until Notification Cleared");
            MelonPreferences.CreateEntry<float>(settingsCategory, hudTimeout, 10f, "Hud Appear Duration");

            //Register our MonoBehavior to let us use OnEnable
            ClassInjector.RegisterTypeInIl2Cpp<HudEvent>();

            harmony = HarmonyInstance.Create("BTKStandaloneIH");

            //Mute/Unmute Hook
            foreach (MethodInfo method in typeof(DefaultTalkController).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name.Contains("Method_Public_Static_Void_Boolean_") && !method.Name.Contains("PDM"))
                    harmony.Patch(method, null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnHudUpdateEvent", BindingFlags.Public | BindingFlags.Static)));
            }

            //World join hook to detect for first world join
            foreach (MethodInfo method in typeof(RoomManager).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name.Contains("Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_"))
                    harmony.Patch(method, null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnWorldJoin", BindingFlags.Static | BindingFlags.Public)));
            }

            HudContent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud");
            GestureParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/GestureToggleParent");
            NotificationParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/NotificationDotParent");
            AFKParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/AFK");
        }

        public static void OnHudUpdateEvent()
        {
            instance.showHud();
        }

        public override void OnPreferencesSaved()
        {
            enableImmersiveHud = MelonPreferences.GetEntryValue<bool>(settingsCategory, hudEnable);
            if (enableImmersiveHud)
                hideHud();
            hudCurrentTimeout = 0;

            foreach (HudEvent hudEvent in hudEventComponents)
            {
                hudEvent.enableUntilClear = MelonPreferences.GetEntryValue<bool>(settingsCategory, hudStayOnUntilClear);
                hudEvent.OnDisableListeners.Clear();
                if (MelonPreferences.GetEntryValue<bool>(settingsCategory, hudStayOnUntilClear))
                    hudEvent.OnDisableListeners.Add(OnTrackedGameObjectDisable);
            }
        }

        public override void OnUpdate()
        {
            if (enableImmersiveHud)
            {
                foreach (MonitoredObject hudItem in customHudObjects)
                {
                    if (hudItem.CheckState())
                        showHud();
                }

                if (VRCUiManager.prop_VRCUiManager_0.field_Public_Text_0.color.a > 0.02f && !keepOn)
                {
                    showHud();
                    keepOn = true;
                }

                if (VRCUiManager.prop_VRCUiManager_0.field_Public_Text_0.color.a <= 0.02f && keepOn)
                {
                    keepOn = false;
                }

                if (shownHud && hudCurrentTimeout <= 0 && !notificationIsActive)
                {
                    hideHud();
                }
                else
                {
                    hudCurrentTimeout -= Time.deltaTime;
                }
            }
        }

        public static void OnWorldJoin()
        {
            if (!instance.scannedCustomHud)
            {
                //World join start custom hud element scan
                instance.scannedCustomHud = true;
                instance.postWorldJoinChildScan();
                instance.OnPreferencesSaved();
            }
        }

        public void postWorldJoinChildScan()
        {
            MelonLogger.Msg("Searching for hud elements...");

            Log("Scanning NotificationParent", true);
            int child1 = IterateAndAttactToChildren(NotificationParent);
            Log("Scanning AFKParent", true);
            int child2 = IterateAndAttactToChildren(AFKParent);
            Log("Scanning GestureParent", true);
            int child3 = IterateAndAttactToChildren(GestureParent);

            MelonLogger.Msg($"Discovered {child1} in NotificationParent, {child2} in AFKParent, and {child3} in GestureParent.");

            VRCUiManager.prop_VRCUiManager_0.field_Public_Text_0.color = new Color(1, 1, 1, 0);

            OnPreferencesSaved();
        }

        public void showHud()
        {
            hudCurrentTimeout = MelonPreferences.GetEntryValue<float>(settingsCategory, hudTimeout);
            if (!shownHud)
            {
                HudContent.transform.localScale = new Vector3(1, 1, 1);
                shownHud = true;
            }
        }

        public void hideHud()
        {
            if (!keepOn)
            {
                HudContent.transform.localScale = new Vector3(0, 0, 0);
                shownHud = false;
            }
        }

        private int IterateAndAttactToChildren(GameObject parent)
        {
            int childCount = 0;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                //Scan objects for non standards to attach
                Transform child = parent.transform.GetChild(i);
                childCount++;

                Log($"Child: {child.name}", true);

                //Patch to monitor the correct component for JoinNotifier
                if (child.name.Equals("NotifyDot-join") || child.name.Equals("NotifyDot-leave"))
                {
                    MonitoredObject newHudItem = new MonitoredObject(child.gameObject, child.gameObject.activeSelf);
                    newHudItem.trackedComponent = child.GetComponent<Image>();
                    customHudObjects.Add(newHudItem);
                }
                else if (child.name.Equals("NotifyDot-DownloadStatusProgress"))
                {
                    //Patch to keep hud active while WorldPreloader is downloading with it's UI element active
                    HudEvent hudEvent = child.gameObject.AddComponent<HudEvent>();
                    hudEvent.OnEnableListeners.Add(OnTrackedGameObjectEnable);

                    hudEventComponents.Add(hudEvent);

                    //Always set enableUntilClear
                    hudEvent.enableUntilClear = true;
                    hudEvent.OnDisableListeners.Add(OnTrackedGameObjectDisable);
                }
                else
                {
                    HudEvent hudEvent = child.gameObject.AddComponent<HudEvent>();
                    hudEvent.OnEnableListeners.Add(OnTrackedGameObjectEnable);

                    hudEventComponents.Add(hudEvent);

                    if (MelonPreferences.GetEntryValue<bool>(settingsCategory, hudStayOnUntilClear))
                    {
                        hudEvent.enableUntilClear = true;
                        hudEvent.OnDisableListeners.Add(OnTrackedGameObjectDisable);
                    }
                }
            }

            return childCount;
        }

        private void OnTrackedGameObjectEnable(bool enableUntilClear)
        {
            showHud();
            notificationIsActive = enableUntilClear;
        }

        private void OnTrackedGameObjectDisable()
        {
            //Reset hud timer before clearing notificationIsActive
            showHud();
            notificationIsActive = false;
        }

        public static void Log(string log, bool dbg = false)
        {
            if (!MelonDebug.IsEnabled() && dbg)
                return;

            MelonLogger.Msg(log);
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
                if (trackedComponent.enabled != lastKnownState)
                {
                    lastKnownState = trackedComponent.enabled;
                    return true;
                }
            }

            return false;
        }
    }
}
