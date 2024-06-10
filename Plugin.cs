using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

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
        private static readonly PropertyInfo TOOK_OFF_COUNT = AccessTools.Property(typeof(GameDataWhiteBoard), "TookOffCount");
        private static readonly PropertyInfo LANDED_COUNT = AccessTools.Property(typeof(GameDataWhiteBoard), "LandedCount");

        private static readonly FieldInfo TAKE_OFF_TASK_CURRENT_TIMER = AccessTools.Field(typeof(TakeoffTask), "currentKnockOutTimer");

        public static float GetTakeOffTaskCurrentTimer(TakeoffTask task)
        {
            return (float)TAKE_OFF_TASK_CURRENT_TIMER.GetValue(task);
        }

        public static void Punish()
        {
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

            aircraft.StartCoroutine(HideCoroutine(aircraft.Panel.GetComponent<SpriteRenderer>(), aircraft.gameObject));
        }

        public static IEnumerator HideCoroutine(SpriteRenderer component, GameObject gameObject)
        {
            Sequence sequence = DOTween.Sequence();
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 1f, 0.25f));
            sequence.Append(DOTweenModuleSprite.DOFade(component, 0f, 0.25f));
            sequence.Play().SetUpdate(isIndependentUpdate: true);

            yield return new WaitForSecondsRealtime(1.25f);

            UnityEngine.Object.Destroy(gameObject);
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
            Helper.Punish();

            return false; // ci.cancel()
        }
    }

    [HarmonyPatch(typeof(LevelManager), "CrowdedGameOver", new Type[] { typeof(TakeoffTaskManager) })]
    class AircraftCrashPatcher3
    {
        static bool Prefix(TakeoffTaskManager takeoffTaskManager, ref LevelManager __instance)
        {
            for (int i = takeoffTaskManager.hangingTakeoffTasks.Count - 1; i >= 0; i--)
            {
                TakeoffTask task = takeoffTaskManager.hangingTakeoffTasks[i];
                if (Helper.GetTakeOffTaskCurrentTimer(task) > task.knockOutTime && !GameOverManager.Instance.GameOverFlag && Time.deltaTime > 0f)
                {
                    task.StopHanging();
                    takeoffTaskManager.hangingTakeoffTasks.RemoveAt(i);

                    task.StartCoroutine(Helper.HideCoroutine(task.Panel.GetComponent<SpriteRenderer>(), task.gameObject));
                }
            }

            TakeoffTaskManager.ReArrangeAircrafts();

            Helper.Punish();
            return false; // ci.cancel()
        }
    }

    [HarmonyPatch(typeof(TakeoffTaskManager), "ReArrangeAircrafts", new Type[] { })]
    class TakeoffTaskManagerPatcher
    {
        static bool Prefix() // The raw implementation of this method is too dirty. Npa, please copy my implementation to your game :)
        {
            TakeoffTaskManager instance = TakeoffTaskManager.Instance;
            int firstEmptyApron = instance.Aprons.Count;

            for (int emptyApronIndex = 0; emptyApronIndex < instance.Aprons.Count; emptyApronIndex++)
            {
                Apron emptyApron = instance.Aprons[emptyApronIndex];
                if (emptyApron.takeoffTask)
                {
                    continue;
                }

                for (int usedApronIndex = emptyApronIndex + 1; usedApronIndex < instance.Aprons.Count; usedApronIndex++)
                {
                    Apron usedApron = instance.Aprons[usedApronIndex];
                    if (!usedApron.takeoffTask)
                    {
                        continue;
                    }

                    Debug.Log($"Moving Scheduled Aircraft from #{usedApronIndex} to {emptyApronIndex}.");

                    usedApron.takeoffTask.apron = emptyApron;
                    usedApron.takeoffTask.gameObject.transform.SetParent(emptyApron.transform);
                    usedApron.takeoffTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);

                    emptyApron.isOccupied = true;
                    emptyApron.takeoffTask = usedApron.takeoffTask;
                    usedApron.isOccupied = false;
                    usedApron.takeoffTask = null;
                    break;
                }

                if (!emptyApron.isOccupied) // This apron is still empty
                {
                    firstEmptyApron = emptyApronIndex;
                    break;
                }
            }

            int emptyApronCount = instance.Aprons.Count - firstEmptyApron;
            Debug.Log($"{emptyApronCount} Count Empty Apron detected from #{firstEmptyApron}.");
            if (instance.hangingTakeoffTasks.Count > 0)
            {
                // Step 1: Move all hanging jobs to empty aprons.
                int scheduleableHangingTaskCount = Math.Min(emptyApronCount, instance.hangingTakeoffTasks.Count);
                Debug.Log($"Moving {scheduleableHangingTaskCount} Count aircrafts from virtual aprons to real ones from {firstEmptyApron}.");
                for (int i = 0; i < scheduleableHangingTaskCount; i++)
                {
                    TakeoffTask hangingTask = instance.hangingTakeoffTasks[i];
                    Apron targetApron = instance.Aprons[firstEmptyApron + i];

                    targetApron.CreateTask(hangingTask);
                    hangingTask.apron = targetApron;
                    hangingTask.StopHanging();

                    hangingTask.gameObject.transform.SetParent(targetApron.transform);
                    hangingTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);
                }

                // Step 2: Forget handled jobs.
                instance.hangingTakeoffTasks.RemoveRange(0, scheduleableHangingTaskCount);

                Debug.Log($"Moving {instance.hangingTakeoffTasks.Count} tasks to virtual aprons.");
                // TODO: Cache this array instance.
                Apron[] virtualAprons = new Apron[] { instance.virtualApron, instance.virtualApron2 };
                // Step 3: Move hanging jobs.
                for (int i = 0; i < instance.hangingTakeoffTasks.Count; i++)
                {
                    TakeoffTask hangingTask = instance.hangingTakeoffTasks[i];
                    Apron virtualApron = virtualAprons[i];

                    virtualApron.isOccupied = true;
                    hangingTask.apron = virtualApron;
                    hangingTask.gameObject.transform.SetParent(virtualApron.transform);
                    hangingTask.gameObject.transform.DOLocalMove(Vector3.zero, 0.5f).SetUpdate(isIndependentUpdate: true);
                }

                // Step 4: Hide emtpy virtual aprons.
                for (int i = instance.hangingTakeoffTasks.Count; i < virtualAprons.Length; i++)
                {
                    Apron virtualApron = virtualAprons[i];
                    virtualApron.isOccupied = false;
                    virtualApron.GetComponentInChildren<Image>().fillAmount = 0f;
                }
            }

            return false;
        }
    }
}
