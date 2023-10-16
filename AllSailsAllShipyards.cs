using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityModManagerNet;

namespace AllSailsAllShipyards
{
    public class ModSettings : UnityModManager.ModSettings, IDrawable
    {
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            UnityModManager.ModSettings.Save(this, modEntry);
        }

        public void OnChange() { }
    }

    static class Main
    {
        public static ModSettings settings;
        public static UnityModManager.ModEntry.ModLogger logger;
        public static GameObject[] completeShipyardList;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            settings = UnityModManager.ModSettings.Load<ModSettings>(modEntry);
            logger = modEntry.Logger;
            completeShipyardList = null;    // waiting to be filled by PrefabsDirectoryPatch

            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;

            return true;
        }

        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Draw(modEntry);
        }

        static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
        }

        public static string GetRegionNameFromId(int regionId)
        {
            switch (regionId)
            {
                case 0: return "Al'Ankh";
                case 1: return "Emerald Archipelago";
                case 2: return "Aestrin";
                default: return "unknown";
            }
        }
    }

    [HarmonyPatch(typeof(Shipyard))]
    class ShipyardPatch
    {
        [HarmonyPatch("Awake")]
        static void Postfix(ref int ___region, ref GameObject[] ___sailPrefabs)
        {
            Main.logger.Log(string.Format("Patching region {0}...", Main.GetRegionNameFromId(___region)));

            if (Main.completeShipyardList != null)
            {
                ___sailPrefabs = Main.completeShipyardList;
                Main.logger.Log(string.Format("Region {0} successfully patched.", Main.GetRegionNameFromId(___region)));
            }
            else
            {
                Main.logger.Log("Patch failed; complete sail prefabs list is not populated!");
            }
        }
    }

    [HarmonyPatch(typeof(PrefabsDirectory))]
    class PrefabsDirectoryPatch
    {
        [HarmonyPatch("Start")]
        static void Postfix(ref GameObject[] ___sails)
        {
            Main.logger.Log("Beginning patch of PrefabsDirectory...");
            var validSailPrefabs = new List<GameObject>();

            for (int i = 0; i < ___sails.Length; i++)
            {
                // check for non-null value
                if (___sails[i])
                    validSailPrefabs.Add(___sails[i]);
            }

            Main.logger.Log(string.Format("Found {0} valid sail prefabs", validSailPrefabs.Count));
            Main.completeShipyardList = validSailPrefabs.ToArray();
        }
    }
}