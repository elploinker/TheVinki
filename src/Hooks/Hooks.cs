﻿using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SprayCans;
using Fisobs.Core;
using MoreSlugcats;
using static Vinki.Plugin;
using MonoMod.Utils;
using System.Runtime.CompilerServices;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using SlugBase;

namespace Vinki
{
    public static partial class Hooks
    {
        public static void ApplyInit()
        {
            On.RainWorld.OnModsInit += Extras.WrapInit(LoadResources);
            On.RainWorld.PostModsInit += RainWorld_PostModsInit;
        }

        // Add hooks
        private static void ApplyHooks()
        {
            Content.Register(new SprayCanFisob());

            // Put your custom hooks here!
            ApplyPlayerHooks();
            ApplyPlayerGraphicsHooks();
            ApplyShelterDoorHooks();
            ApplySSOracleHooks();
            ApplyRoomHooks();
            ApplyJollyCoopHooks();
            ApplySaveStateHooks();
            ApplyKarmaLadderScreenHooks();
        }

        // Load any resources, such as sprites or sounds
        private static void LoadResources(RainWorld rainWorld)
        {
            Enums.RegisterValues();
            ApplyHooks();

            SlugBase.SaveData.SlugBaseSaveData progSaveData = SlugBase.SaveData.SaveDataExtension.GetSlugBaseData(rainWorld.progression.miscProgressionData);
            VinkiConfig.ShowVinkiTitleCard.OnChange += () => progSaveData.Set("ShowVinkiTitleCard", VinkiConfig.ShowVinkiTitleCard.Value);

            bool modChanged = false;
            if (rainWorld.options.modLoadOrder.TryGetValue("olaycolay.thevinki", out _) && VinkiConfig.RestoreGraffitiOnUpdate.Value)
            {
                ModManager.Mod vinkiMod = ModManager.InstalledMods.Where(mod => mod.id == "olaycolay.thevinki").FirstOrDefault();
                var saveData = SlugBase.SaveData.SaveDataExtension.GetSlugBaseData(rainWorld.progression.miscProgressionData);
                if (saveData.TryGet("VinkiVersion", out string modVersion))
                {
                    modChanged = vinkiMod.version != modVersion;
                    if (modChanged) Debug.Log("Vinki mod version changed!");
                }
                else
                {
                    Debug.Log("Didn't find saved vinki mod version");
                    modChanged = true;
                }
                Debug.Log("Setting vinki version to " + vinkiMod.version);
                saveData.Set("VinkiVersion", vinkiMod.version);
                rainWorld.progression.SaveProgression(false, true);
            }
            else
            {
                Debug.Log("Can't find vinki mod ID");
            }

            graffitiFolder = AssetManager.ResolveDirectory(graffitiFolder);
            storyGraffitiFolder = AssetManager.ResolveDirectory(storyGraffitiFolder);

            // If the graffiti folder doesn't exist (or is empty), copy it from the mod
            if (!Directory.Exists(graffitiFolder) || !Directory.EnumerateDirectories(graffitiFolder).Any() ||
                !Directory.Exists(graffitiFolder + "/vinki") || !Directory.EnumerateFileSystemEntries(graffitiFolder + "/vinki").Any() ||
                !Directory.Exists(graffitiFolder + "/White") || !Directory.EnumerateFileSystemEntries(graffitiFolder + "/White").Any() || modChanged)
            {
                if (!CopyGraffitiBackup())
                {
                    return;
                }
            }

            // Go through each graffiti image and add it to the list of decals Vinki can place
            LoadGraffiti();

            // Remix menu config
            VinkiConfig.RegisterOI();

            // Get sprite atlases
            Futile.atlasManager.LoadAtlas("atlases/SprayCan");
            Futile.atlasManager.LoadAtlas("atlases/glasses");
            Futile.atlasManager.LoadAtlas("atlases/rainpods");
            Futile.atlasManager.LoadAtlas("atlases/shoes");

            TailTexture = new Texture2D(150, 75, TextureFormat.ARGB32, false);
            var tailTextureFile = AssetManager.ResolveFilePath("textures/VinkiTail.png");
            if (File.Exists(tailTextureFile))
            {
                var rawData = File.ReadAllBytes(tailTextureFile);
                TailTexture.LoadImage(rawData);
            }

            // Populate the colorfulItems List for crafting Spray Cans
            InitColorfulItems();
        }

        public static bool CopyGraffitiBackup()
        {
            string backupFolder = AssetManager.ResolveDirectory("decals/GraffitiBackup");
            if (!Directory.Exists(backupFolder))
            {
                Debug.LogError("Could not find Vinki graffiti backup folder in workshop files or local mods!");
                return false;
            }
            Debug.Log("Graffiti folder doesn't exist! Copying from backup folder: " + backupFolder);
            CopyFilesRecursively(backupFolder, backupFolder + "/../VinkiGraffiti");
            graffitiFolder = AssetManager.ResolveDirectory("decals/VinkiGraffiti");
            return true;
        }

        public static void LoadGraffiti()
        {
            graffitiOffsets.Clear();
            graffitis.Clear();
            storyGraffitiRoomPositions.Clear();

            foreach (string parent in Directory.EnumerateDirectories(graffitiFolder))
            {
                foreach (var image in Directory.EnumerateFiles(parent, "*.*", SearchOption.AllDirectories)
                    .Where(s => s.EndsWith(".png")))
                {
                    AddGraffiti(image, new DirectoryInfo(parent).Name);
                }
            }

            // Add the story graffitis
            AddGraffiti("5P", "Story", new("SS_AI", new Vector2(650, 200)));
            AddGraffiti("5P_stretched", "Story", new("SS_AI", new Vector2(520, 400)));
            AddGraffiti("test", "Story", new("SS_D08", new Vector2(470, 300)));
        }

        private static void AddGraffiti(string image, string slugcat, KeyValuePair<string, Vector2>? storyGraffitiRoomPos = null)
        {
            PlacedObject.CustomDecalData decal = new PlacedObject.CustomDecalData(null);
            decal.imageName = "VinkiGraffiti/" + slugcat + "/" + Path.GetFileNameWithoutExtension(image);
            decal.fromDepth = 0.2f;

            if (!graffitis.ContainsKey(slugcat))
            {
                graffitiOffsets[slugcat] = new();
                graffitis[slugcat] = new();
                graffitiAvgColors[slugcat] = new();
            }

            string filePath;
            if (storyGraffitiRoomPos.HasValue)
            {
                filePath = AssetManager.ResolveFilePath(storyGraffitiFolder + '/' + Path.GetFileNameWithoutExtension(image) + ".png");
                decal.imageName = "StorySpoilers/" + Path.GetFileNameWithoutExtension(image);
                storyGraffitiRoomPositions.Add(graffitis["Story"].Count, storyGraffitiRoomPos.Value);
                storyGraffitiCount++;
            }
            else
            {
                filePath = graffitiFolder + "/" + slugcat + "/" + Path.GetFileNameWithoutExtension(image) + ".png";
            }

            // Get the image as a 2d texture so we can resize it to something manageable
            Texture2D img = new Texture2D(2, 2);
            byte[] tmpBytes = File.ReadAllBytes(filePath);
            ImageConversion.LoadImage(img, tmpBytes);

            // Get average color of image (to use for graffiti spray/smoke color)
            graffitiAvgColors[slugcat].Add(AverageColorFromTexture(img));

            // Resize image to look good in game
            if (!storyGraffitiRoomPos.HasValue)
            {
                int[] newSize = ResizeAndKeepAspectRatio(img.width, img.height, 100f * 100f);
                img.Resize(newSize[0], newSize[1]);
            }

            decal.handles[0] = new Vector2(0f, img.height);
            decal.handles[1] = new Vector2(img.width, img.height);
            decal.handles[2] = new Vector2(img.width, 0f);

            float halfWidth = img.width / 2f;
            float halfHeight = img.height / 2f;
            graffitiOffsets[slugcat].Add(new Vector2(-halfWidth, -halfHeight));
            graffitis[slugcat].Add(decal);
        }

        public static bool IsPostInit;
        private static void RainWorld_PostModsInit(On.RainWorld.orig_PostModsInit orig, RainWorld self)
        {
            orig(self);
            try
            {
                if (IsPostInit) return;
                IsPostInit = true;

                // Putting this hook here ensures that SlugBase's BuildScene hook goes first
                On.Menu.MenuScene.BuildScene += MenuScene_BuildScene;
                On.ProcessManager.PostSwitchMainProcess += ProcessManager_PostSwitchMainProcess;

                if (SlugBase.SaveData.SaveDataExtension.GetSlugBaseData(self.progression.miscProgressionData).TryGet("ShowVinkiTitleCard", out bool value) == false || value)
                {
                    Debug.Log("Enabled vinki title card: " + value ?? "null");
                    IL.Menu.IntroRoll.ctor += IntroRoll_ctor;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void InitColorfulItems()
        {
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.AttachedBee, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.BlinkingFlower, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.BubbleGrass, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.DangleFruit, 1);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.DataPearl, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.EggBugEgg, 1);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.FirecrackerPlant, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.FlareBomb, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.FlyLure, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.KarmaFlower, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.Lantern, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.Mushroom, 1);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.NeedleEgg, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.OverseerCarcass, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.PuffBall, 1);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.ScavengerBomb, 3);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.SlimeMold, 2);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.SporePlant, 1);
            colorfulItems.Add(AbstractPhysicalObject.AbstractObjectType.WaterNut, 2);

            if (!ModManager.MSC)
            {
                return;
            }

            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.DandelionPeach, 2);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.FireEgg, 2);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.Germinator, 2);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.GlowWeed, 2);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.GooieDuck, 2);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.LillyPuck, 1);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.Seed, 1);
            colorfulItems.Add(MoreSlugcatsEnums.AbstractObjectType.SingularityBomb, 9001);
        }

        private static void MenuScene_BuildScene(On.Menu.MenuScene.orig_BuildScene orig, Menu.MenuScene self)
        {
            orig(self);

            if (self.sceneID == null)
            {
                return;
            }

            if (self.sceneID.ToString() == "Slugcat_Vinki")
            {
                // Find the graffiti layers of the slugcat select scene
                List<Menu.MenuDepthIllustration> menuGraffitis = new List<Menu.MenuDepthIllustration>();
                foreach (var image in self.depthIllustrations.Where(f => Path.GetFileNameWithoutExtension(f.fileName).StartsWith("Graffiti - ")))
                {
                    menuGraffitis.Add(image);
                }

                // Randomize which graffiti shows
                int randGraffiti = UnityEngine.Random.Range(0, menuGraffitis.Count);
                string fileName = "Graffiti - " + randGraffiti.ToString();

                // Show the random graffiti and hide the rest
                foreach (var image in menuGraffitis)
                {
                    string imageName = Path.GetFileNameWithoutExtension(image.fileName);
                    image.alpha = (imageName == fileName) ? 1f : 0f;
                }
            }
            else if (self.sceneID.ToString() == "Sleep_Vinki")
            {
                // Find the item layers of the slugcat select scene
                List<Menu.MenuDepthIllustration> sleepItems = new List<Menu.MenuDepthIllustration>();
                foreach (Menu.MenuDepthIllustration image in self.depthIllustrations.Where(f => Path.GetFileNameWithoutExtension(f.fileName).StartsWith("Item - ")))
                {
                    image.alpha = 0f;
                    string imageName = Path.GetFileNameWithoutExtension(image.fileName);
                    imageName = imageName.Substring(imageName.IndexOf('-') + 2);

                    // Show the item layers that are in the shelter
                    foreach (string item in shelterItems)
                    {
                        if (imageName == item)
                        {
                            image.alpha = 1f;
                        }
                    }
                }

                shelterItems.Clear();

                // Find the graffiti layers of the slugcat select scene
                List<Menu.MenuDepthIllustration> menuGraffitis = new List<Menu.MenuDepthIllustration>();
                foreach (var image in self.depthIllustrations.Where(f => Path.GetFileNameWithoutExtension(f.fileName).StartsWith("Graffiti - ")))
                {
                    menuGraffitis.Add(image);
                }

                // Randomize which graffiti shows
                int randGraffiti = UnityEngine.Random.Range(0, menuGraffitis.Count - 1);
                string fileName = "Graffiti - " + randGraffiti.ToString();

                // Show the random graffiti and hide the rest
                foreach (var image in menuGraffitis)
                {
                    string imageName = Path.GetFileNameWithoutExtension(image.fileName);
                    //Debug.Log("Graffiti: Checking if " + imageName + " matches " + fileName + "\t" + (imageName == fileName));
                    image.alpha = (imageName == fileName) ? 1f : 0f;
                }

                // Find the doodle layers of the slugcat select scene
                List<Menu.MenuDepthIllustration> menuDoodles = new List<Menu.MenuDepthIllustration>();
                foreach (var image in self.depthIllustrations.Where(f => Path.GetFileNameWithoutExtension(f.fileName).StartsWith("Doodle - ")))
                {
                    menuDoodles.Add(image);
                }

                // Randomize which doodle shows
                int randDoodles = UnityEngine.Random.Range(0, menuDoodles.Count);
                fileName = "Doodle - " + randDoodles.ToString();

                // Show the random doodle and hide the rest
                foreach (var image in menuDoodles)
                {
                    string imageName = Path.GetFileNameWithoutExtension(image.fileName);
                    //Debug.Log("Doodle: Checking if " + imageName + " matches " + fileName);
                    image.alpha = (imageName == fileName) ? 1f : 0f;
                }
            }
        }

        private static int[] ResizeAndKeepAspectRatio(float original_width, float original_height, float target_area)
        {
            float new_width = Mathf.Sqrt((original_width / original_height) * target_area);
            float new_height = target_area / new_width;

            int w = Mathf.RoundToInt(new_width); // round to the nearest integer
            int h = Mathf.RoundToInt(new_height - (w - new_width)); // adjust the rounded width with height 

            return new int[] { w, h };
        }

        public static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(targetPath);

            //Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            //Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }

        private static Color AverageColorFromTexture(Texture2D tex)
        {
            Color[] texColors = tex.GetPixels();
            int total = texColors.Length;

            float r = 0f;
            float g = 0f;
            float b = 0f;

            for (int i = 0; i < texColors.Length; i++)
            {
                if (texColors[i].a <= 0.1f)
                {
                    total--;
                    continue;
                }

                r += texColors[i].r;
                g += texColors[i].g;
                b += texColors[i].b;
            }

            r /= total;
            g /= total;
            b /= total;

            if (r + g + b < float.Epsilon)
            {
                return Color.white;
            }
            return new Color(r, g, b, 1f);
        }

        private static void ProcessManager_PostSwitchMainProcess(On.ProcessManager.orig_PostSwitchMainProcess orig, ProcessManager self, ProcessManager.ProcessID ID)
        {
            if (ID == Enums.GraffitiQuest)
            {
                
            }
            orig(self, ID);
        }

        private static void IntroRoll_ctor(ILContext il)
        {
            var cursor = new ILCursor(il);

            if (cursor.TryGotoNext(i => i.MatchLdstr("Intro_Roll_C_"))
                && cursor.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt<string>(nameof(string.Concat))))
            {
                cursor.Emit(OpCodes.Ldloc_3);
                cursor.EmitDelegate<Func<string, string[], string>>((titleImage, oldTitleImages) =>
                {
                    titleImage = (UnityEngine.Random.value < 0.5f) ? "intro_roll_vinki_0" : "intro_roll_vinki_1";

                    return titleImage;
                });
            }
        }
    }
}