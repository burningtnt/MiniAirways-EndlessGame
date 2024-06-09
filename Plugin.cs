using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace EndlessGame
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
        }
    }

    public class Helper
    {
        private static PropertyInfo TOOK_OFF_COUNT;
        private static PropertyInfo LANDED_COUNT;

        public static void Punish()
        {
            if (TOOK_OFF_COUNT == null)
            {
                TOOK_OFF_COUNT = typeof(GameDataWhiteBoard).GetProperty("TookOffCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                LANDED_COUNT = typeof(GameDataWhiteBoard).GetProperty("LandedCount", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

                Debug.Log($"{TOOK_OFF_COUNT == null}, {LANDED_COUNT == null}");
            }

            TOOK_OFF_COUNT.SetValue(null, Math.Max(0, (int)TOOK_OFF_COUNT.GetValue(null) - 2));
            LANDED_COUNT.SetValue(null, Math.Max(0, (int)LANDED_COUNT.GetValue(null) - 2));
        }

        public static void Destroy(Aircraft aircraft)
        {
            if (aircraft == null)
            {
                return;
            }

            // Destroy the Collider.
            Collider2D[] componentsInChildren = aircraft.GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < componentsInChildren.Length; i++)
            {
                componentsInChildren[i].enabled = false;
            }
            componentsInChildren = aircraft.GetComponents<Collider2D>();
            for (int i = 0; i < componentsInChildren.Length; i++)
            {
                componentsInChildren[i].enabled = false;
            }

            aircraft.StartCoroutine(CrashInsGameOverCoroutine(aircraft));
        }

        private static IEnumerator CrashInsGameOverCoroutine(Aircraft aircraft)
        {
            SpriteRenderer component = aircraft.Panel.GetComponent<SpriteRenderer>();
            Sequence sequence = DOTween.Sequence();
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Play().SetUpdate(isIndependentUpdate: true);

            yield return new WaitForSecondsRealtime(1.25f);

            UnityEngine.Object.Destroy(aircraft.gameObject);
        }
    }

    [HarmonyPatch(typeof(LevelManager), "OutOfBoundaryGameOver", new Type[] { typeof(Aircraft) })]
    class AircraftCrashPatcher0
    {
        static bool Prefix(Aircraft aircraft, ref LevelManager __instance)
        {
            Helper.Destroy(aircraft);
            Helper.Punish();

            return false; // ci.cancel()
        }
    }

    [HarmonyPatch(typeof(LevelManager), "CrashGameOver", new Type[] { typeof(Aircraft), typeof(Aircraft) })]
    class AircraftCrashPatcher1
    {
        static bool Prefix(Aircraft aircraft1, Aircraft aircraft2, ref LevelManager __instance)
        {
            Helper.Destroy(aircraft1);
            Helper.Destroy(aircraft2);
            Helper.Punish();

            return false; // ci.cancel()
        }
    }

    [HarmonyPatch(typeof(LevelManager), "RestrictedGameOver", new Type[] { typeof(Aircraft) })]
    class AircraftCrashPatcher2
    {
        static bool Prefix(Aircraft aircraft, ref LevelManager __instance)
        {
            Helper.Destroy(aircraft);
            Helper.Punish();

            return false; // ci.cancel()
        }
    }

    [HarmonyPatch(typeof(LevelManager), "CrowdedGameOver", new Type[] { typeof(TakeoffTaskManager) })]
    class AircraftCrashPatcher3
    {
        static bool Prefix(TakeoffTaskManager takeoffTaskManager, ref LevelManager __instance)
        {
            Helper.Punish();
            return false; // ci.cancel()
        }
    }
}
