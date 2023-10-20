using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityModManagerNet;

namespace AllSailsAllShipyards
{
    public class ModSettings : UnityModManager.ModSettings, IDrawable
    {
        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
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

    // Represents an array of up to 12 sail prefabs, within a certain category
    class ShipyardSailPage
    {
        public SailCategory category;
        public GameObject[] sails;

        public ShipyardSailPage(SailCategory category, GameObject[] sails)
        {
            this.category = category;
            this.sails = sails;
        }

        public static ShipyardSailPage[] SplitCategoryIntoPages(SailCategory category)
        {
            ShipyardSailPage[] pages;
            // filter for sails in the given category
            var sailsInCategory = Main.completeShipyardList.Where(sail => sail.GetComponent<Sail>().category == category);
            var sailsCount = sailsInCategory.Count();

            // max sails per page = 12
            int pageCount;
            int remainder;
            // number of pages is (# sails / 12 rounded down); if there are any left over, add an extra page for the overflow
            pageCount = Math.DivRem(sailsCount, 12, out remainder);
            if (remainder > 0)
                pageCount++;

            pages = new ShipyardSailPage[pageCount];

            // index represents the start of a set of <= 12 sail prefabs
            for (int index = 0; index < sailsCount; index += 12)
            {
                // skip any sails already grabbed; then take the next 12 (or whatever is left < 12)
                var pageSails = sailsInCategory.Skip(index).Take(12).ToArray();

                var currentPage = new ShipyardSailPage(category, pageSails);
                pages[index / 12] = currentPage;
            }

            return pages;
        }
    }

    class ShipyardSailPageButton : GoPointerButton
    {
        public static SailCategory currentCategory;
        public static int page = 0;
        public static int maxPage = -1;
        public int pageMod;
        public bool darkened = false;

        public void Awake()
        {
            // default material
            this.gameObject.GetComponent<MeshRenderer>().sharedMaterial = ShipyardUI.instance.parchmentMaterial;
        }

        public override void ExtraLateUpdate()
        {
            // Right button
            if (pageMod == 1)
            {
                if (page == (maxPage - 1) && darkened == false)
                {
                    this.gameObject.GetComponent<MeshRenderer>().sharedMaterial = ShipyardUI.instance.darkParchmentMaterial;
                    darkened = true;
                }
                else if (page < (maxPage - 1) && darkened == true)
                {
                    this.gameObject.GetComponent<MeshRenderer>().sharedMaterial = ShipyardUI.instance.parchmentMaterial;
                    darkened = false;
                }
            }
            // Left button
            else if (pageMod == -1)
            {
                if (page == 0 && darkened == false)
                {
                    this.gameObject.GetComponent<MeshRenderer>().sharedMaterial = ShipyardUI.instance.darkParchmentMaterial;
                    darkened = true;
                }
                else if (page > 0 && darkened == true)
                {
                    this.gameObject.GetComponent<MeshRenderer>().sharedMaterial = ShipyardUI.instance.parchmentMaterial;
                    darkened = false;
                }
            }
        }

        public override void OnActivate()
        {
            UISoundPlayer.instance.PlayUISound(UISounds.buttonClick, 1f, 1f);

            // change page and clamp
            page += pageMod;
            if (page >= maxPage)
                page = (maxPage - 1);
            if (page < 0)
                page = 0;

            ShipyardUI.instance.ShowNewSailButtons(currentCategory);
        }
    }

    [HarmonyPatch(typeof(ShipyardUI))]
    class ShipyardUIPatch
    {
        static Dictionary<SailCategory, ShipyardSailPage[]> pagesByCategory;
        static Traverse SailMastCompatible;

        [HarmonyPatch("ShowUI")]
        static void Postfix(ref GameObject[] ___addSailButtons)
        {
            // Initialize button for incrementing page

            var rightButton = new GameObject("sail page right");
            var rightButton_MeshFilter = rightButton.AddComponent<MeshFilter>();
            rightButton_MeshFilter.sharedMesh = ___addSailButtons[0].GetComponent<MeshFilter>().sharedMesh;
            rightButton.AddComponent<MeshRenderer>();
            rightButton.AddComponent<BoxCollider>();
            var rightButton_Script = rightButton.AddComponent<ShipyardSailPageButton>();
            rightButton_Script.pageMod = 1;

            rightButton.transform.parent = ___addSailButtons[0].transform.parent.transform;
            rightButton.layer = 5;
            var anchorPosition = ___addSailButtons[0].transform.localPosition;
            rightButton.transform.localPosition = new Vector3(anchorPosition.x + 5f, anchorPosition.y - 0.85f, anchorPosition.z);
            rightButton.transform.localRotation = Quaternion.identity;
            rightButton.transform.localScale = Vector3.one * 0.8f;

            var rightButtonText = new GameObject("text");
            var rightButtonText_TextMesh = rightButtonText.AddComponent<TextMesh>();
            rightButtonText_TextMesh.font = ___addSailButtons[0].GetComponentInChildren<TextMesh>().font;
            rightButtonText_TextMesh.fontSize = 50;
            rightButtonText_TextMesh.color = Color.black;
            rightButtonText_TextMesh.alignment = TextAlignment.Center;
            rightButtonText_TextMesh.anchor = TextAnchor.MiddleCenter;
            rightButtonText_TextMesh.text = ">";
            var rightButtonText_MeshRenderer = rightButtonText.GetComponent<MeshRenderer>();
            rightButtonText_MeshRenderer.sharedMaterial = ___addSailButtons[0].transform.GetChild(0).gameObject.GetComponent<MeshRenderer>().sharedMaterial;

            rightButtonText.transform.parent = rightButton.transform;
            rightButtonText.layer = 5;
            rightButtonText.transform.localPosition = new Vector3(0.05f, -0.05f, 0f);
            rightButtonText.transform.localRotation = Quaternion.identity;
            rightButtonText.transform.localScale = new Vector3(0.2f, 0.2f, 1f);


            // Initialize button for decrementing page

            var leftButton = new GameObject("sail page left");
            var leftButton_MeshFilter = leftButton.AddComponent<MeshFilter>();
            leftButton_MeshFilter.sharedMesh = ___addSailButtons[0].GetComponent<MeshFilter>().sharedMesh;
            leftButton.AddComponent<MeshRenderer>();
            leftButton.AddComponent<BoxCollider>();
            var leftButton_Script = leftButton.AddComponent<ShipyardSailPageButton>();
            leftButton_Script.pageMod = -1;

            leftButton.transform.parent = ___addSailButtons[0].transform.parent.transform;
            leftButton.layer = 5;
            leftButton.transform.localPosition = new Vector3(anchorPosition.x + 4f, anchorPosition.y - 0.85f, anchorPosition.z);
            leftButton.transform.localRotation = Quaternion.identity;
            leftButton.transform.localScale = Vector3.one * 0.8f;

            var leftButtonText = new GameObject("text");
            var leftButtonText_TextMesh = leftButtonText.AddComponent<TextMesh>();
            leftButtonText_TextMesh.font = ___addSailButtons[0].GetComponentInChildren<TextMesh>().font;
            leftButtonText_TextMesh.fontSize = 50;
            leftButtonText_TextMesh.color = Color.black;
            leftButtonText_TextMesh.alignment = TextAlignment.Center;
            leftButtonText_TextMesh.anchor = TextAnchor.MiddleCenter;
            leftButtonText_TextMesh.text = "<";
            var leftButtonText_MeshRenderer = leftButtonText.GetComponent<MeshRenderer>();
            leftButtonText_MeshRenderer.sharedMaterial = ___addSailButtons[0].transform.GetChild(0).gameObject.GetComponent<MeshRenderer>().sharedMaterial;

            leftButtonText.transform.parent = leftButton.transform;
            leftButtonText.layer = 5;
            leftButtonText.transform.localPosition = new Vector3(-0.05f, -0.05f, 0f);
            leftButtonText.transform.localRotation = Quaternion.identity;
            leftButtonText.transform.localScale = new Vector3(0.2f, 0.2f, 1f);


            // Create pages from complete sail lists

            // array of pages for each category
            pagesByCategory = new Dictionary<SailCategory, ShipyardSailPage[]>(Enum.GetValues(typeof(SailCategory)).Length);

            foreach (SailCategory category in Enum.GetValues(typeof(SailCategory)))
            {
                var pages = ShipyardSailPage.SplitCategoryIntoPages(category);
                pagesByCategory.Add(category, pages);
            }


            // Bind private method for sail page rendering
            SailMastCompatible = Traverse.Create(ShipyardUI.instance).Method("SailMastCompatible", new Type[] {typeof(GameObject)});
        }

        [HarmonyPatch("ShowNewSailButtons")]
        static bool Prefix(ref SailCategory category, ref GameObject ___addSailMenu, ref GameObject[] ___addSailButtons)
        {
            ___addSailMenu.SetActive(true);

            // Update buttons category, so it can refresh using this method when necessary
            // TODO: review; can't I just replace this method entirely, and call one from the buttons class
            //  which keeps track of its own current category?
            if (ShipyardSailPageButton.currentCategory != category)
            {
                ShipyardSailPageButton.currentCategory = category;
                // reset page when navigating to new category
                ShipyardSailPageButton.page = 0;
            }

            // Get all sail pages in this category
            var sailPages = pagesByCategory[category];

            // Update buttons with max pages
            ShipyardSailPageButton.maxPage = sailPages.Length;

            // Render current page
            //var currentPage = sailPages[ShipyardSailPageButton.page];
            // TODO: I don't think the custom class is necessary. An array of sail game objects should suffice.
            GameObject[] sails = { };
            if (sailPages.Length > 0)
                sails = sailPages[ShipyardSailPageButton.page].sails;

            for (int buttonIndex = 0; buttonIndex < 12; buttonIndex++)
            {
                if (buttonIndex < sails.Length)
                {
                    ___addSailButtons[buttonIndex].SetActive(true);
                    ___addSailButtons[buttonIndex].GetComponent<ShipyardButton>().RegisterPrefab(sails[buttonIndex]);

                    bool compatible = SailMastCompatible.GetValue<bool>(sails[buttonIndex]);
                    if (compatible)
                    {
                        ___addSailButtons[buttonIndex].GetComponent<Renderer>().sharedMaterial = ShipyardUI.instance.parchmentMaterial;
                        ___addSailButtons[buttonIndex].GetComponent<Collider>().enabled = true;
                    }
                    else
                    {
                        ___addSailButtons[buttonIndex].GetComponent<Renderer>().sharedMaterial = ShipyardUI.instance.darkParchmentMaterial;
                        ___addSailButtons[buttonIndex].GetComponent<Collider>().enabled = false;
                    }
                }
                else
                {
                    ___addSailButtons[buttonIndex].SetActive(false);
                }
            }

            // skip original method
            return false;
        }
    }
}