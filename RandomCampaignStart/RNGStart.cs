using System;
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
        
        private static void ReplacePilotStats(PilotDef pilotDef, PilotDef replacementStatPilotDef, SimGameState simGameState)
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
            if (pilotDef.AbilityDefs != null)
                pilotDef.AbilityDefs.Clear();
            pilotDef.ForceRefreshAbilityDefs();
        } 

        public static void Postfix(SimGameState __instance)
        {
            if (RngStart.Settings.NumberRandomRonin + RngStart.Settings.NumberProceduralPilots > 0)
            {
                // clear roster
                while (__instance.PilotRoster.Count > 0)
                    __instance.PilotRoster.RemoveAt(0);
                
                // starting ronin that are always present
                if (RngStart.Settings.StartingRonin != null && RngStart.Settings.StartingRonin.Count > 0)
                {
                    foreach (var roninID in RngStart.Settings.StartingRonin)
                    {
                        var pilotDef = __instance.DataManager.PilotDefs.Get(roninID);

                        if (RngStart.Settings.RerollRoninStats)
                            ReplacePilotStats(pilotDef, __instance.PilotGenerator.GeneratePilots(1, RngStart.Settings.PilotPlanetDifficulty, 0, out _)[0], __instance);
                        
                        if (pilotDef != null)
                            __instance.AddPilotToRoster(pilotDef, true);
                    }
                }

                // random ronin
                if (RngStart.Settings.NumberRandomRonin > 0)
                {
                    var randomRonin = GetRandomSubList(__instance.RoninPilots, RngStart.Settings.NumberRandomRonin);

                    foreach (var pilotDef in randomRonin)
                    {
                        if (RngStart.Settings.RerollRoninStats)
                            ReplacePilotStats(pilotDef, __instance.PilotGenerator.GeneratePilots(1, RngStart.Settings.PilotPlanetDifficulty, 0, out _)[0], __instance);
                        
                        __instance.AddPilotToRoster(pilotDef, true);
                    }
                }

                // random prodedural pilots
                if (RngStart.Settings.NumberProceduralPilots > 0)
                {
                    var randomProcedural = __instance.PilotGenerator.GeneratePilots(RngStart.Settings.NumberProceduralPilots, RngStart.Settings.PilotPlanetDifficulty, 0, out _);

                    foreach (var pilotDef in randomProcedural)
                        __instance.AddPilotToRoster(pilotDef, true);
                }
            }

            // mechs
            if (RngStart.Settings.NumberLightMechs + RngStart.Settings.NumberMediumMechs + RngStart.Settings.NumberHeavyMechs + RngStart.Settings.NumberAssaultMechs > 0)
            {
                var baySlot = 1;
                var mechIds = new List<string>();

                // clear the initial lance
                for (var i = 1; i < __instance.Constants.Story.StartingLance.Length + 1; i++)
                    __instance.ActiveMechs.Remove(i);

                // remove ancestral mech if specified
                if (RngStart.Settings.RemoveAncestralMech)
                {
                    __instance.ActiveMechs.Remove(0);
                    baySlot = 0;
                }

                // add the random mechs to mechIds
                mechIds.AddRange(GetRandomSubList(RngStart.Settings.AssaultMechsPossible, RngStart.Settings.NumberAssaultMechs));
                mechIds.AddRange(GetRandomSubList(RngStart.Settings.HeavyMechsPossible, RngStart.Settings.NumberHeavyMechs));
                mechIds.AddRange(GetRandomSubList(RngStart.Settings.MediumMechsPossible, RngStart.Settings.NumberMediumMechs));
                mechIds.AddRange(GetRandomSubList(RngStart.Settings.LightMechsPossible, RngStart.Settings.NumberLightMechs));

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

        public bool RerollRoninStats = true;
        public int PilotPlanetDifficulty = 1;
        public int NumberProceduralPilots = 0;
        public int NumberRandomRonin = 4;

        public bool RemoveAncestralMech = false;
    }

    public static class RngStart
    {
        internal static ModSettings Settings;

        public static void Init(string modDir, string modSettings)
        {
            var harmony = HarmonyInstance.Create("io.github.mpstark.RandomCampaignStart");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

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
}
