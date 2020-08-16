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
        public const string Version = "1.2.0";
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

        public static List<string> recentUnhideEvents = new List<string>();
        public static List<HudEvent> hudEventComponents = new List<HudEvent>();

        //We need to handle these differently cause VRChat sucks
        private string[] VRChatNotificationPatch =
        {
            "VoteKickDot",
            "FriendRequestDot",
            "InviteDot",
            "InviteRequestDot"
        };

        private List<MonitoredObject> customHudObjects = new List<MonitoredObject>();
        private float hudCurrentTimeout = 0f;
        private bool shownHud = false;
        private bool enableImmersiveHud = false;
        private bool scannedCustomHud = false;
        private bool notificationIsActive = false;

        //Cached Objects
        private GameObject HudContent;
        private GameObject GestureParent;
        private GameObject NotificationParent;
        private GameObject AFKParent;

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
            MelonPrefs.RegisterBool(settingsCategory, hudStayOnUntilClear, false, "Keep Hud Visible Until Notification Cleared");
            MelonPrefs.RegisterFloat(settingsCategory, hudTimeout, 10f, "Hud Appear Duration");

            //Register our MonoBehavior to let us use OnEnable
            ClassInjector.RegisterTypeInIl2Cpp<HudEvent>();

            harmony = HarmonyInstance.Create("BTKStandaloneIH");
            //Mute/Unmute Hook
            harmony.Patch(typeof(DefaultTalkController).GetMethod("Method_Public_Static_Void_Boolean_0", BindingFlags.Public | BindingFlags.Static), null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnHudUpdateEvent", BindingFlags.Public | BindingFlags.Static)));
            //World join hook to detect for first world join
            foreach (MethodInfo method in typeof(RoomManagerBase).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name.Contains("Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_"))
                    harmony.Patch(method, null, new HarmonyMethod(typeof(BTKSAImmersiveHud).GetMethod("OnWorldJoin", BindingFlags.Static | BindingFlags.Public)));
            }

            HudContent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud");
            GestureParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/GestureToggleParent");
            NotificationParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/NotificationDotParent");
            AFKParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent/Hud/AFK");
        }

        public override void OnModSettingsApplied()
        {
            enableImmersiveHud = MelonPrefs.GetBool(settingsCategory, hudEnable);
            if (enableImmersiveHud)
                hideHud();
            hudCurrentTimeout = 0;

            foreach (HudEvent hudEvent in hudEventComponents)
            {
                hudEvent.enableUntilClear = MelonPrefs.GetBool(settingsCategory, hudStayOnUntilClear);
                hudEvent.OnDisableListeners.Clear();
                if (!hudEvent.vrchatBrokenNotification && MelonPrefs.GetBool(settingsCategory, hudStayOnUntilClear))
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
                instance.OnModSettingsApplied();
            }
        }

        public void postWorldJoinChildScan()
        {
            MelonLogger.Log("Searching for hud elements...");

            int child1 = IterateAndAttactToChildren(NotificationParent);
            int child2 = IterateAndAttactToChildren(AFKParent);
            int child3 = IterateAndAttactToChildren(GestureParent);

            MelonLogger.Log($"Discovered {child1} in NotificationParent, {child2} in AFKParent, and {child3} in GestureParent.");

            OnModSettingsApplied();
        }

        public void showHud()
        {
            hudCurrentTimeout = MelonPrefs.GetFloat(settingsCategory, hudTimeout);
            if (!shownHud)
            {
                HudContent.transform.localScale = new Vector3(1, 1, 1);
                shownHud = true;
            }
        }

        public void hideHud()
        {
            HudContent.transform.localScale = new Vector3(0, 0, 0);
            shownHud = false;
        }

        private int IterateAndAttactToChildren(GameObject parent)
        {
            int childCount = 0;

            for (int i = 0; i < parent.transform.childCount; i++)
            {
                //Scan objects for non standards to attach
                Transform child = parent.transform.GetChild(i);
                childCount++;

                //Patch to monitor the correct component for JoinNotifier
                if (child.name.Equals("NotifyDot-join") || child.name.Equals("NotifyDot-leave"))
                {
                    MonitoredObject newHudItem = new MonitoredObject(child.gameObject, child.gameObject.activeSelf);
                    newHudItem.trackedComponent = child.GetComponent<Image>();
                    customHudObjects.Add(newHudItem);
                }
                else
                {
                    HudEvent hudEvent = child.gameObject.AddComponent<HudEvent>();
                    hudEvent.OnEnableListeners.Add(OnTrackedGameObjectEnable);

                    hudEventComponents.Add(hudEvent);

                    if (VRChatNotificationPatch.Any(x => x.Equals(child.name)))
                    {
                        //Make sure we handle the scuffed VRChat Enable/Disable spam bullshit
                        hudEvent.vrchatBrokenNotification = true;
                    }

                    if (MelonPrefs.GetBool(settingsCategory, hudStayOnUntilClear))
                    {
                        hudEvent.enableUntilClear = true;
                        if(!hudEvent.vrchatBrokenNotification)
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
