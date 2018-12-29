using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;

// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global

namespace RandomCampaignStart
{
    [HarmonyPatch(typeof(SimGameState), "FirstTimeInitializeDataFromDefs")]
    public static class SimGameState_FirstTimeInitializeDataFromDefs_Patch
    {
        // from https://stackoverflow.com/questions/273313/randomize-a-listt
        private static readonly Random rng = new Random();

        private static void RNGShuffle<T>(this IList<T> list)
        {
            var n = list.Count;
            while (n > 1)
            {
                n--;
                var k = rng.Next(n + 1);
                var value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        private static List<T> GetRandomSubList<T>(List<T> list, int number)
        {
            var subList = new List<T>();

            if (list.Count <= 0 || number <= 0)
                return subList;

            var randomizeMe = new List<T>(list);

            // add enough duplicates of the list to satisfy the number specified
            while (randomizeMe.Count < number)
                randomizeMe.AddRange(list);

            randomizeMe.RNGShuffle();
            for (var i = 0; i < number; i++)
                subList.Add(randomizeMe[i]);

            return subList;
        }

        public static void Postfix(SimGameState __instance)
        {
            var NumberStartingRonin = RandomCampaignStart.Settings.StartingRonin.Count;

            if (NumberStartingRonin + RandomCampaignStart.Settings.NumberRandomRonin + RandomCampaignStart.Settings.NumberProceduralPilots > 0)
            {
                // clear roster
                while (__instance.PilotRoster.Count > 0)
                    __instance.PilotRoster.RemoveAt(0);

                // pilotgenerator seems to give me the same exact results for ronin
                // every time, and can push out duplicates, which is odd?
                // just do our own thing
                var pilots = new List<PilotDef>();

                if (RandomCampaignStart.Settings.StartingRonin != null)
                {
                    foreach (var roninID in RandomCampaignStart.Settings.StartingRonin)
                    {
                        var pilotDef = __instance.DataManager.PilotDefs.Get(roninID);

                        // add directly to roster, don't want to get duplicate ronin from random ronin
                        if (pilotDef != null)
                            __instance.AddPilotToRoster(pilotDef, true, true);
                    }
                }

                pilots.AddRange(GetRandomSubList(__instance.RoninPilots, RandomCampaignStart.Settings.NumberRandomRonin));

                // pilot generator works fine for non-ronin =/
                if (RandomCampaignStart.Settings.NumberProceduralPilots > 0)
                    pilots.AddRange(__instance.PilotGenerator.GeneratePilots(RandomCampaignStart.Settings.NumberProceduralPilots, 1, 0, out _));

                // actually add the pilots to the SimGameState
                foreach (var pilotDef in pilots)
                    __instance.AddPilotToRoster(pilotDef, true, true);
            }

            // mechs
            if (RandomCampaignStart.Settings.NumberLightMechs + RandomCampaignStart.Settings.NumberMediumMechs + RandomCampaignStart.Settings.NumberHeavyMechs + RandomCampaignStart.Settings.NumberAssaultMechs > 0)
            {
                var baySlot = 1;
                var mechIds = new List<string>();

                // clear the initial lance
                for (var i = 1; i < __instance.Constants.Story.StartingLance.Length + 1; i++)
                    __instance.ActiveMechs.Remove(i);

                // remove ancestral mech if specified
                if (RandomCampaignStart.Settings.RemoveAncestralMech)
                {
                    __instance.ActiveMechs.Remove(0);
                    baySlot = 0;
                }

                // add the random mechs to mechIds
                mechIds.AddRange(GetRandomSubList(RandomCampaignStart.Settings.AssaultMechsPossible, RandomCampaignStart.Settings.NumberAssaultMechs));
                mechIds.AddRange(GetRandomSubList(RandomCampaignStart.Settings.HeavyMechsPossible, RandomCampaignStart.Settings.NumberHeavyMechs));
                mechIds.AddRange(GetRandomSubList(RandomCampaignStart.Settings.MediumMechsPossible, RandomCampaignStart.Settings.NumberMediumMechs));
                mechIds.AddRange(GetRandomSubList(RandomCampaignStart.Settings.LightMechsPossible, RandomCampaignStart.Settings.NumberLightMechs));

                // actually add the mechs to the game
                for (var i = 0; i < mechIds.Count; i++)
                {
                    var mechDef = new MechDef(__instance.DataManager.MechDefs.Get(mechIds[i]), __instance.GenerateSimGameUID());
                    __instance.AddMech(baySlot, mechDef, true, true, false);

                    // check to see if we're on the last mechbay and if we have more mechs to add
                    // if so, store the mech at index 5 before next iteration.
                    if (baySlot == 5 && i + 1 < mechIds.Count)
                        __instance.UnreadyMech(5, mechDef);
                    else
                        baySlot++;
                }
            }
        }
    }

    internal class ModSettings
    {
        public List<string> AssaultMechsPossible = new List<string>();
        public List<string> HeavyMechsPossible = new List<string>();
        public List<string> LightMechsPossible = new List<string>();
        public List<string> MediumMechsPossible = new List<string>();

        public int NumberAssaultMechs = 0;
        public int NumberHeavyMechs = 0;
        public int NumberLightMechs = 3;
        public int NumberMediumMechs = 1;

        public List<string> StartingRonin = new List<string>();

        public int NumberProceduralPilots = 0;
        public int NumberRandomRonin = 4;

        public bool RemoveAncestralMech = false;
    }

    public static class RandomCampaignStart
    {
        internal static ModSettings Settings;
        internal static string ModDirectory;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.mpstark.RandomCampaignStart");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            ModDirectory = modDir;

            // read settings
            try
            {
                Settings = JsonConvert.DeserializeObject<ModSettings>(modSettings);
            }
            catch (Exception)
            {
                Settings = new ModSettings();
            }
        }
    }

    public class Logger
    {
        static string filePath = $"{RandomCampaignStart.ModDirectory}/Log.txt";
        public static void LogError(Exception ex)
        {
            using (var writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine("Message :" + ex.Message + "<br/>" + Environment.NewLine + "StackTrace :" + ex.StackTrace +
                    "" + Environment.NewLine + "Date :" + DateTime.Now.ToString());
                writer.WriteLine(Environment.NewLine + "-----------------------------------------------------------------------------" + Environment.NewLine);
            }
        }

        public static void LogLine(String line)
        {
            using (var writer = new StreamWriter(filePath, true))
            {
                writer.WriteLine(line + Environment.NewLine + "Date :" + DateTime.Now.ToString());
                writer.WriteLine(Environment.NewLine + "-----------------------------------------------------------------------------" + Environment.NewLine);
            }
        }
    }


}
