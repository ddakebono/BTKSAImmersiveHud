using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ABI_RC.Core.InteractionSystem;
using ABI_RC.Core.Savior;
using ABI_RC.Core.UI;
using BTKSAImmersiveHud.Config;
using BTKUILib;
using BTKUILib.UIObjects;
using BTKUILib.UIObjects.Components;
using HarmonyLib;
using Semver;
using System.Reflection;
using UnityEngine;

namespace BTKSAImmersiveHud
{
    public static class BuildInfo
    {
        public const string Name = "BTKSAImmersiveHud";
        public const string Author = "DDAkebono#0001";
        public const string Company = "BTK-Development";
        public const string Version = "2.0.4";
        public const string DownloadLink = "https://github.com/ddakebono/BTKSAImmersiveHud/releases";
    }

    public class BTKSAImmersiveHud : MelonMod
    {
        internal static BTKSAImmersiveHud Instance;
        internal static MelonLogger.Instance Logger;

        internal static readonly List<IBTKBaseConfig> BTKConfigs = new();

        internal static BTKBoolConfig IHEnable = new(nameof(BTKSAImmersiveHud), "Enable Immersive Hud", "Enables/Disables Immersive Hud's functionality", false, null, false);
        private BTKBoolConfig _stayOnUntilClear = new(nameof(BTKSAImmersiveHud), "Stay On Until Clear", "Keeps the hud visible until all notifications are cleared", false, null, false);
        internal static BTKBoolConfig IgnoreDesktopReticle = new(nameof(BTKSAImmersiveHud), "Ignore Desktop Reticle", "Keeps the Desktop Reticle visible when hiding the Hud", false, null, false);
        private BTKFloatConfig _timeout = new(nameof(BTKSAImmersiveHud), "Hud Timeout", "How long before the hud is hidden again (In seconds)", 10f, 0f, 60f, null, false);

        private static FieldInfo _hudShown = typeof(CohtmlHud).GetField("_isHudShown", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo _renderMat = typeof(CohtmlHud).GetField("_renderMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
        private static MethodInfo _btkGetCreatePageAdapter;

        private DateTime _lastEnabled = DateTime.Now;
        private bool _qmReady;
        private bool _hidden;
        private bool _stayVisible;
        private bool _stayVisibleNotifier;
        private bool _lastNotifierState;
        private object _ihCoroutineToken;
        private float _messageTimer;
        private bool _quitting;
        private bool _hasSetupUI;

        public override void OnInitializeMelon()
        {
            Logger = LoggerInstance;

            Logger.Msg("BTK Standalone: Immersive Hud - Starting Up");

            if (RegisteredMelons.Any(x => x.Info.Name.Equals("BTKCompanionLoader", StringComparison.OrdinalIgnoreCase)))
            {
                Logger.Msg("Hold on a sec! Looks like you've got BTKCompanion installed, this mod is built in and not needed!");
                Logger.Error("BTKSAImmersiveHud has not started up! (BTKCompanion Running)");
                return;
            }

            if (!RegisteredMelons.Any(x => x.Info.Name.Equals("BTKUILib") && x.Info.SemanticVersion != null && x.Info.SemanticVersion.CompareTo(new SemVersion(1)) >= 0))
            {
                Logger.Error("BTKUILib was not detected or it outdated! BTKCompanion cannot function without it!");
                Logger.Error("Please download an updated copy for BTKUILib!");
                return;
            }

            Instance = this;

            if (RegisteredMelons.Any(x => x.Info.Name.Equals("BTKUILib") && x.Info.SemanticVersion.CompareByPrecedence(new SemVersion(1, 9)) > 0))
            {
                //We're working with UILib 2.0.0, let's reflect the get create page function
                _btkGetCreatePageAdapter = typeof(Page).GetMethod("GetOrCreatePage", BindingFlags.Public | BindingFlags.Static);
                Logger.Msg($"BTKUILib 2.0.0 detected, attempting to grab GetOrCreatePage function: {_btkGetCreatePageAdapter != null}");
            }

            //Apply patches
            ApplyPatches(typeof(HudPatches));
            
            QuickMenuAPI.OnMenuRegenerate += OnQMMarkAsReady;

            IHEnable.OnConfigUpdated += o =>
            {
                if(o)
                    _ihCoroutineToken = MelonCoroutines.Start(ImmersiveHudCoroutine());
                else
                    MelonCoroutines.Stop(_ihCoroutineToken);
                
                ShowHud();
            };

            IgnoreDesktopReticle.OnConfigUpdated += b =>
            {
                ShowHud();
            };
            
            QuickMenuAPI.OnMenuRegenerate += LateStartup;
        }

        public override void OnApplicationQuit()
        {
            _quitting = true;
        }

        public void HudUpdated()
        {
            ShowHud();
        }

        public void HudUpdatedMessage(float duration, bool resetTimer = false)
        {
            if (resetTimer)
                _messageTimer = 0f;
            
            _messageTimer += duration;
            ShowHud();
        }

        public void HudUpdatedPropSpawn(bool state)
        {
            ShowHud();
            _stayVisible = state;
        }

        public void HudUpdatedNotifier(bool state)
        {
            if(_lastNotifierState == state) return;

            ShowHud();
            
            _stayVisibleNotifier = state;
            _lastNotifierState = state;
        }

        private void ShowHud()
        {
            _lastEnabled = DateTime.Now;
            // _renderMaterial is initialized in Start and is used in ShowHud which is used in RestoreHud
            if (CohtmlHud.Instance != null && GetHudRenderMat() != null)
                CohtmlHud.Instance.RestoreHud();
            _hidden = false;
        }
        
        private void OnQMMarkAsReady(CVR_MenuManager obj)
        {
            _qmReady = true;
        }

        private void ApplyPatches(Type type)
        {
            try
            {
                HarmonyInstance.PatchAll(type);
            }
            catch (Exception e)
            {
                Logger.Error($"Failed while patching {type.Name}!");
                Logger.Error(e);
            }
        }
        
        private IEnumerator ImmersiveHudCoroutine()
        {
            while (!_quitting)
            {
                yield return new WaitForSeconds(.1f);
                
                if(!_qmReady || _hidden || _stayVisible || (_stayVisibleNotifier && _stayOnUntilClear.BoolValue)) continue;

                var currentShowSeconds = DateTime.Now.Subtract(_lastEnabled).TotalSeconds;

                if (currentShowSeconds < _timeout.FloatValue && (_messageTimer == 0f || currentShowSeconds < _messageTimer)) continue;

                _hidden = true;
                _messageTimer = 0f;
                CohtmlHud.Instance.HideHud();
            }
        }
        
        private void LateStartup(CVR_MenuManager obj)
        {
            if(IHEnable.BoolValue)
                _ihCoroutineToken = MelonCoroutines.Start(ImmersiveHudCoroutine());
            
            if(_hasSetupUI) return;
            _hasSetupUI = true;
            
            QuickMenuAPI.PrepareIcon("BTKStandalone", "BTKIcon", Assembly.GetExecutingAssembly().GetManifestResourceStream("BTKSAImmersiveHud.Images.BTKIcon.png"));
            QuickMenuAPI.PrepareIcon("BTKStandalone", "Settings", Assembly.GetExecutingAssembly().GetManifestResourceStream("BTKSAImmersiveHud.Images.Settings.png"));

            Page rootPage;

            if (_btkGetCreatePageAdapter != null)
                rootPage = (Page)_btkGetCreatePageAdapter.Invoke(null, new object[] { "BTKStandalone", "MainPage", true, "BTKIcon", null, false });
            else
                rootPage = new Page("BTKStandalone", "MainPage", true, "BTKIcon");

            rootPage.MenuTitle = "BTK Standalone Mods";
            rootPage.MenuSubtitle = "Toggle and configure your BTK Standalone mods here!";

            var functionToggles = rootPage.AddCategory("Immersive Hud");
            
            var settingsPage = functionToggles.AddPage("IH Settings", "Settings", "Change settings related to Immersive Hud", "BTKStandalone");

            var configCategories = new Dictionary<string, Category>();
            
            foreach (var config in BTKConfigs)
            {
                if (!configCategories.ContainsKey(config.Category)) 
                    configCategories.Add(config.Category, settingsPage.AddCategory(config.Category));

                var cat = configCategories[config.Category];

                switch (config.Type)
                {
                    case { } boolType when boolType == typeof(bool):
                        ToggleButton toggle = null;
                        var boolConfig = (BTKBoolConfig)config;
                        toggle = cat.AddToggle(config.Name, config.Description, boolConfig.BoolValue);
                        toggle.OnValueUpdated += b =>
                        {
                            if (!ConfigDialogs(config))
                                toggle.ToggleValue = boolConfig.BoolValue;

                            boolConfig.BoolValue = b;
                        };
                        break;
                    case {} floatType when floatType == typeof(float):
                        SliderFloat slider = null;
                        var floatConfig = (BTKFloatConfig)config;
                        slider = settingsPage.AddSlider(floatConfig.Name, floatConfig.Description, Convert.ToSingle(floatConfig.FloatValue), floatConfig.MinValue, floatConfig.MaxValue);
                        slider.OnValueUpdated += f =>
                        {
                            if (!ConfigDialogs(config))
                            {
                                slider.SetSliderValue(floatConfig.FloatValue);
                                return;
                            }

                            floatConfig.FloatValue = f;

                        };
                        break;
                }
            }
        }
        
        private bool ConfigDialogs(IBTKBaseConfig config)
        {
            if (config.DialogMessage != null)
            {
                QuickMenuAPI.ShowNotice("Notice", config.DialogMessage);
            }

            return true;
        }

        public static bool GetIsHudShownState()
        {
            return (bool)_hudShown.GetValue(CohtmlHud.Instance);
        }

        public static Material GetHudRenderMat()
        {
            return (Material)_renderMat.GetValue(CohtmlHud.Instance);
        }
    }
    
    [HarmonyPatch(typeof(CohtmlHud))]
    class HudPatches
    {
        [HarmonyPatch(nameof(CohtmlHud.UpdateMicStatus))]
        [HarmonyPatch(nameof(CohtmlHud.DisplayInteractableIndicator))]
        [HarmonyPatch(nameof(CohtmlHud.SetDisplayChain))]
        [HarmonyPostfix]
        static void HudUpdated()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdated();
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            }
        }

        [HarmonyPatch(nameof(CohtmlHud.ViewDropText), typeof(string), typeof(string))]
        [HarmonyPatch(nameof(CohtmlHud.ViewDropText), typeof(string), typeof(string), typeof(string))]
        [HarmonyPostfix]
        static void HudUpdatedMessageShort()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedMessage(4f);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            } 
        }
        
        [HarmonyPatch(nameof(CohtmlHud.ViewDropTextImmediate))]
        [HarmonyPostfix]
        static void HudUpdatedMessageShortReset()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedMessage(4f, true);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            } 
        }
        
        [HarmonyPatch(nameof(CohtmlHud.ViewDropTextLong))]
        [HarmonyPostfix]
        static void HudUpdatedMessageLong()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedMessage(7f);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            } 
        }
        
        [HarmonyPatch(nameof(CohtmlHud.ViewDropTextLonger))]
        [HarmonyPostfix]
        static void HudUpdatedMessageLonger()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedMessage(10f);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            } 
        }

        [HarmonyPatch(nameof(CohtmlHud.SelectPropToSpawn))]
        [HarmonyPostfix]
        static void HudUpdatedPropSpawn()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedPropSpawn(true);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            }
        }

        [HarmonyPatch(nameof(CohtmlHud.ClearPropToSpawn))]
        [HarmonyPostfix]
        static void HudUpdatedPropSpawnHide()
        {
            if (BTKSAImmersiveHud.Instance == null || !BTKSAImmersiveHud.IHEnable.BoolValue) return;
            try
            {
                BTKSAImmersiveHud.Instance.HudUpdatedPropSpawn(false);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            }
        }

        [HarmonyPatch("UpdateNotifierStatus")]
        [HarmonyPostfix]
        static void NotifierUpdate()
        {
            try
            {
                if (ViewManager.Instance.FriendRequests.Count > 0 && MetaPort.Instance.settings.GetSettingsBool("HUDCustomizationFriendRequests", false))
                {
                    BTKSAImmersiveHud.Instance.HudUpdatedNotifier(true);
                    return;
                }
                 
                if ((ViewManager.Instance.Invites.Count > 0 || ViewManager.Instance.InviteRequests.Count > 0) && MetaPort.Instance.settings.GetSettingsBool("HUDCustomizationInvites", false))
                {
                    BTKSAImmersiveHud.Instance.HudUpdatedNotifier(true);
                    return;
                }
                
                if (ViewManager.Instance.allRunningVotes.Count > 0 && MetaPort.Instance.settings.GetSettingsBool("HUDCustomizationVotes", false))
                {
                    BTKSAImmersiveHud.Instance.HudUpdatedNotifier(true);
                    return;
                }
                
                BTKSAImmersiveHud.Instance.HudUpdatedNotifier(false);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            }
        }

        [HarmonyPatch(nameof(CohtmlHud.Update))]
        [HarmonyPostfix]
        static void UpdateDesktopReticle(CohtmlHud __instance)
        {
            try {
                var showReticleMenuClosed = BTKSAImmersiveHud.GetIsHudShownState() || BTKSAImmersiveHud.IgnoreDesktopReticle.BoolValue;
                var showReticle = ViewManager.Instance.isGameMenuOpen() ? !Cursor.visible : showReticleMenuClosed;
                __instance.desktopPointer.SetActive(showReticle);
            }
            catch (Exception e)
            {
                BTKSAImmersiveHud.Logger.Error(e);
            }
        }
    }
}
