using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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
        public const string Version = "1.3.10";
        public const string DownloadLink = "https://github.com/ddakebono/BTKSAImmersiveHud/releases";
    }

    public class BTKSAImmersiveHud : MelonMod
    {
        public static BTKSAImmersiveHud Instance;
        
        public bool ScannedCustomHud = false;

        private const string SettingsCategory = "BTKSAImmersiveHud";
        private const string HUDEnable = "hudEnable";
        private const string HUDTimeout = "hudTimeout";
        private const string HUDStayOnUntilClear = "hudStayTillClear";

        private static readonly List<HudEvent> HUDEventComponents = new List<HudEvent>();

        private readonly List<MonitoredObject> customHudObjects = new List<MonitoredObject>();
        private float hudCurrentTimeout = 0f;
        private bool shownHud = false;
        private bool enableImmersiveHud = false;

        private bool notificationIsActive = false;
        private bool keepOn = false;
        private int scenesLoaded = 0;

        //Cached Objects
        private GameObject hudContent;
        private GameObject gestureParent;
        private GameObject notificationParent;
        private GameObject afkParent;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (scenesLoaded > 2) return;
            scenesLoaded++;
            if (scenesLoaded == 2)
                UiManagerInit();
        }

        private void UiManagerInit()
        {
            MelonLogger.Msg("BTK Standalone: Immersive Hud - Starting Up");

            Instance = this;

            if (MelonHandler.Mods.Any(x => x.Info.Name.Equals("BTKCompanionLoader", StringComparison.OrdinalIgnoreCase)))
            {
                MelonLogger.Msg("Hold on a sec! Looks like you've got BTKCompanion installed, this mod is built in and not needed!");
                MelonLogger.Error("BTKSAImmersiveHud has not started up! (BTKCompanion Running)");
                return;
            }

            MelonPreferences.CreateCategory(SettingsCategory, "Immersive Hud");
            MelonPreferences.CreateEntry(SettingsCategory, HUDEnable, true, "Immersive Hud Enable");
            MelonPreferences.CreateEntry(SettingsCategory, HUDStayOnUntilClear, false, "Keep Hud Visible Until Notification Cleared");
            MelonPreferences.CreateEntry(SettingsCategory, HUDTimeout, 10f, "Hud Appear Duration");

            //Apply patches
            applyPatches(typeof(RoomManagerPatches));

            //Register our MonoBehavior to let us use OnEnable
            ClassInjector.RegisterTypeInIl2Cpp<HudEvent>();

            USpeaker.field_Private_Static_Action_0 += new Action(OnHudUpdateEvent);

            hudContent = GameObject.Find("/UserInterface/UnscaledUI/HudContent_Old/Hud");
            gestureParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent_Old/Hud/GestureToggleParent");
            notificationParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent_Old/Hud/NotificationDotParent");
            afkParent = GameObject.Find("/UserInterface/UnscaledUI/HudContent_Old/Hud/AFK");
        }
        
        private void applyPatches(Type type)
        {
            try
            {
                HarmonyLib.Harmony.CreateAndPatchAll(type, "BTKHarmonyInstance");
            }
            catch(Exception e)
            {
                Log($"Failed while patching {type.Name}!");
                MelonLogger.Error(e);
            }
        }

        private static void OnHudUpdateEvent()
        {
            Instance.ShowHud();
        }

        public override void OnPreferencesSaved()
        {
            enableImmersiveHud = MelonPreferences.GetEntryValue<bool>(SettingsCategory, HUDEnable);
            if (enableImmersiveHud)
                HideHud();
            hudCurrentTimeout = 0;

            foreach (var hudEvent in HUDEventComponents)
            {
                hudEvent.enableUntilClear = MelonPreferences.GetEntryValue<bool>(SettingsCategory, HUDStayOnUntilClear);
                hudEvent.OnDisableListeners.Clear();
                if (MelonPreferences.GetEntryValue<bool>(SettingsCategory, HUDStayOnUntilClear))
                    hudEvent.OnDisableListeners.Add(OnTrackedGameObjectDisable);
            }
        }

        public override void OnUpdate()
        {
            if (!enableImmersiveHud) return;
            
            foreach (var hudItem in customHudObjects.Where(hudItem => hudItem.CheckState()))
            {
                ShowHud();
            }

            if (VRCUiManager.field_Private_Static_VRCUiManager_0.field_Private_Single_0 > 0f && !keepOn)
            {
                ShowHud();
                keepOn = true;
            }

            if (VRCUiManager.field_Private_Static_VRCUiManager_0.field_Private_Single_0 <= 0f && keepOn)
            {
                keepOn = false;
            }

            if (shownHud && hudCurrentTimeout <= 0 && !notificationIsActive)
            {
                HideHud();
            }
            else
            {
                hudCurrentTimeout -= Time.deltaTime;
            }
        }

        public void PostWorldJoinChildScan()
        {
            MelonLogger.Msg("Searching for hud elements...");

            Log("Scanning NotificationParent", true);
            int child1 = IterateAndAttachToChildren(notificationParent);
            Log("Scanning AFKParent", true);
            int child2 = IterateAndAttachToChildren(afkParent);
            Log("Scanning GestureParent", true);
            int child3 = IterateAndAttachToChildren(gestureParent);

            MelonLogger.Msg($"Discovered {child1} in NotificationParent, {child2} in AFKParent, and {child3} in GestureParent.");

            OnPreferencesSaved();
        }

        private void ShowHud()
        {
            hudCurrentTimeout = MelonPreferences.GetEntryValue<float>(SettingsCategory, HUDTimeout);

            if (!shownHud)
            {
                hudContent.transform.localScale = new Vector3(1, 1, 1);
                shownHud = true;
            }
        }

        private void HideHud()
        {
            if (!keepOn)
            {
                hudContent.transform.localScale = new Vector3(0, 0, 0);
                shownHud = false;
            }
        }

        private int IterateAndAttachToChildren(GameObject parent)
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
                    var gameObject = child.gameObject;
                    MonitoredObject newHudItem = new MonitoredObject(gameObject, gameObject.activeSelf);
                    newHudItem.TrackedComponent = child.GetComponent<Image>();
                    customHudObjects.Add(newHudItem);
                }
                else if (child.name.Equals("NotifyDot-DownloadStatusProgress"))
                {
                    //Patch to keep hud active while WorldPreloader is downloading with it's UI element active
                    HudEvent hudEvent = child.gameObject.AddComponent<HudEvent>();
                    hudEvent.OnEnableListeners.Add(OnTrackedGameObjectEnable);

                    HUDEventComponents.Add(hudEvent);

                    //Always set enableUntilClear
                    hudEvent.enableUntilClear = true;
                    hudEvent.OnDisableListeners.Add(OnTrackedGameObjectDisable);
                }
                else
                {
                    HudEvent hudEvent = child.gameObject.AddComponent<HudEvent>();
                    hudEvent.OnEnableListeners.Add(OnTrackedGameObjectEnable);

                    HUDEventComponents.Add(hudEvent);

                    if (MelonPreferences.GetEntryValue<bool>(SettingsCategory, HUDStayOnUntilClear))
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
            ShowHud();
            notificationIsActive = enableUntilClear;
        }

        private void OnTrackedGameObjectDisable()
        {
            //Reset hud timer before clearing notificationIsActive
            ShowHud();
            notificationIsActive = false;
        }

        private static void Log(string log, bool dbg = false)
        {
            if (!MelonDebug.IsEnabled() && dbg)
                return;

            MelonLogger.Msg(log);
        }

    }
    
    [HarmonyPatch]
    class RoomManagerPatches
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(RoomManager).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(x => x.Name.Contains("Method_Public_Static_Boolean_ApiWorld_ApiWorldInstance_") && !x.Name.Contains("PDM")).Cast<MethodBase>();
        }

        static void Postfix()
        {
            if (!BTKSAImmersiveHud.Instance.ScannedCustomHud)
            {
                //World join start custom hud element scan
                BTKSAImmersiveHud.Instance.ScannedCustomHud = true;
                BTKSAImmersiveHud.Instance.PostWorldJoinChildScan();
                BTKSAImmersiveHud.Instance.OnPreferencesSaved();
            }
        }
    }

    //Monitored GameObject object
    class MonitoredObject
    {
        private GameObject hudItem;
        private bool lastKnownState;
        //Patch to monitor JoinNotifier
        public Image TrackedComponent;

        public MonitoredObject(GameObject go, bool lastKnownState)
        {
            this.hudItem = go;
            this.lastKnownState = lastKnownState;
        }

        public bool CheckState()
        {
            if (TrackedComponent == null) return false;
            if (TrackedComponent.enabled == lastKnownState) return false;
            lastKnownState = TrackedComponent.enabled;
            return true;

        }
    }
}
