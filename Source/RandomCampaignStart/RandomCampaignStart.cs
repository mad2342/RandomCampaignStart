using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using System.Linq;

namespace RandomCampaignStart
{
    public static class RandomCampaignStart
    {
        internal static string LogPath;
        internal static string ModDirectory;
        internal static Settings Settings;
        // BEN: DebugLevel (0: nothing, 1: error, 2: debug, 3: info)
        internal static int DebugLevel = 2;

        public static void Init(string directory, string settings)
        {
            ModDirectory = directory;
            LogPath = Path.Combine(ModDirectory, "RandomCampaignStart.log");

            Logger.Initialize(LogPath, DebugLevel, ModDirectory, nameof(RandomCampaignStart));

            try
            {
                Settings = JsonConvert.DeserializeObject<Settings>(settings);
            }
            catch (Exception e)
            {
                Settings = new Settings();
                Logger.Error(e);
            }

            // Harmony calls need to go last here because their Prepare() methods directly check Settings...
            HarmonyInstance harmony = HarmonyInstance.Create("de.mad.RandomCampaignStart");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }



    [HarmonyPatch(typeof(SimGameState), "FirstTimeInitializeDataFromDefs")]
    public static class SimGameState_FirstTimeInitializeDataFromDefs_Patch
    {
        private static readonly Random RNG = new Random();

        private static void RNGShuffle<T>(this IList<T> list)
        {
            var n = list.Count;
            while (n > 1)
            {
                n--;
                var k = RNG.Next(n + 1);
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

        private static void ReplacePilotStats(PilotDef pilotDef, PilotDef replacementStatPilotDef)
        {
            // set all stats to the subPilot stats
            pilotDef.AddBaseSkill(SkillType.Gunnery, replacementStatPilotDef.BaseGunnery - pilotDef.BaseGunnery);
            pilotDef.AddBaseSkill(SkillType.Piloting, replacementStatPilotDef.BasePiloting - pilotDef.BasePiloting);
            pilotDef.AddBaseSkill(SkillType.Guts, replacementStatPilotDef.BaseGuts - pilotDef.BaseGuts);
            pilotDef.AddBaseSkill(SkillType.Tactics, replacementStatPilotDef.BaseTactics - pilotDef.BaseTactics);

            pilotDef.ResetBonusStats();
            pilotDef.AddSkill(SkillType.Gunnery, replacementStatPilotDef.BonusGunnery);
            pilotDef.AddSkill(SkillType.Piloting, replacementStatPilotDef.BonusPiloting);
            pilotDef.AddSkill(SkillType.Guts, replacementStatPilotDef.BonusGuts);
            pilotDef.AddSkill(SkillType.Tactics, replacementStatPilotDef.BonusTactics);

            // set exp to replacementStatPilotDef
            pilotDef.SetSpentExperience(replacementStatPilotDef.ExperienceSpent);
            pilotDef.SetUnspentExperience(replacementStatPilotDef.ExperienceUnspent);

            // copy abilities
            pilotDef.abilityDefNames.Clear();
            pilotDef.abilityDefNames.AddRange(replacementStatPilotDef.abilityDefNames);
            pilotDef.AbilityDefs?.Clear();
            pilotDef.ForceRefreshAbilityDefs();
        }

        public static void Postfix(SimGameState __instance)
        {
            Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] Called");

            if (RandomCampaignStart.Settings.StartingRonin.Count + RandomCampaignStart.Settings.NumberRandomRonin + RandomCampaignStart.Settings.NumberProceduralPilots > 0)
            {
                Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] Randomizing Pilots");

                // clear roster
                while (__instance.PilotRoster.Count > 0)
                    __instance.PilotRoster.RemoveAt(0);

                // starting ronin that are always present
                if (RandomCampaignStart.Settings.StartingRonin != null && RandomCampaignStart.Settings.StartingRonin.Count > 0)
                {
                    foreach (var roninID in RandomCampaignStart.Settings.StartingRonin)
                    {
                        Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] startingRoninID: " + roninID);
                        var pilotDef = __instance.DataManager.PilotDefs.Get(roninID);

                        if (RandomCampaignStart.Settings.RerollRoninStats)
                            ReplacePilotStats(pilotDef, __instance.PilotGenerator.GeneratePilots(1, RandomCampaignStart.Settings.PilotPlanetDifficulty, 0, out _)[0]);

                        if (pilotDef != null)
                            __instance.AddPilotToRoster(pilotDef, true, true);
                    }
                }

                // random ronin
                if (RandomCampaignStart.Settings.NumberRandomRonin > 0)
                {
                    //var randomRonin = GetRandomSubList(__instance.RoninPilots, RandomCampaignStart.Settings.NumberRandomRonin);
                    var randomRonin = GetRandomSubList(__instance.RoninPilots.Where(x => !RandomCampaignStart.Settings.StartingRonin.Contains(x.Description.Id)).ToList(), RandomCampaignStart.Settings.NumberRandomRonin);

                    foreach (var pilotDef in randomRonin)
                    {
                        Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] randomRoninID: " + pilotDef.Description.Id);
                        if (RandomCampaignStart.Settings.RerollRoninStats)
                            ReplacePilotStats(pilotDef, __instance.PilotGenerator.GeneratePilots(1, RandomCampaignStart.Settings.PilotPlanetDifficulty, 0, out _)[0]);

                        __instance.AddPilotToRoster(pilotDef, true, true);
                    }
                }

                // random procedural pilots
                if (RandomCampaignStart.Settings.NumberProceduralPilots > 0)
                {
                    var randomProcedural = __instance.PilotGenerator.GeneratePilots(RandomCampaignStart.Settings.NumberProceduralPilots, RandomCampaignStart.Settings.PilotPlanetDifficulty, 0, out _);

                    foreach (var pilotDef in randomProcedural)
                    {
                        Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] randomProceduralName: " + pilotDef.Description.Name);
                        __instance.AddPilotToRoster(pilotDef, true);
                    }
                }
            }

            // mechs
            if (RandomCampaignStart.Settings.NumberLightMechs + RandomCampaignStart.Settings.NumberMediumMechs + RandomCampaignStart.Settings.NumberHeavyMechs + RandomCampaignStart.Settings.NumberAssaultMechs > 0)
            {
                Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] Randomizing Mechs");

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
                    Logger.Debug("[SimGameState_FirstTimeInitializeDataFromDefs_POSTFIX] mechDefID: " + mechDef.Description.Id);

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



    [HarmonyPatch(typeof(SimGameState), "GetUnusedRonin")]
    public static class SimGameState_GetUnusedRonin_Patch
    {
        public static void Postfix(ref SimGameState __instance, ref PilotDef __result)
        {
            try
            {
                if (__result != null)
                {
                    string selectedRoninId = __result.Description.Id; Logger.Debug("[SimGameState_GetUnusedRonin_POSTFIX] selectedRoninId: " + selectedRoninId);

                    if (RandomCampaignStart.Settings.BlacklistedRonins.Contains(selectedRoninId))
                    {
                        Logger.Debug("[SimGameState_GetUnusedRonin_POSTFIX] selectedRoninId: " + selectedRoninId + " is blacklisted!");
                        __result = null;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
    }



    internal class Settings
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
        public List<string> BlacklistedRonins = new List<string>();

        public bool RerollRoninStats = true;
        public int PilotPlanetDifficulty = 1;
        public int NumberProceduralPilots = 0;
        public int NumberRandomRonin = 4;

        public bool RemoveAncestralMech = false;
    }
}
