using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;

using System;
using System.Security.Permissions;
using System.Security;
using UnityEngine;
using UnityEngine.AddressableAssets;
using RoR2.Projectile;
using UnityEngine.Networking;
using System.Collections.Generic;
[module: UnverifiableCode]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
namespace RiskySeeker
{
    [BepInPlugin("com.RiskyLives.RiskySeeker", "RiskySeeker", "1.0.2")]
    public class RiskySeeker : BaseUnityPlugin
    {
        public static class ConfigOptions
        {
            public static bool buffSpiritPunch;
            public static bool buffUnseenHand;
            public static bool buffMeditate;
        }

        private void Awake()
        {
            ReadConfig();
            BuffSpiritPunch();
            BuffUnseenHand();
            BuffMeditate();
        }
        private void ReadConfig()
        {
            ConfigOptions.buffSpiritPunch = base.Config.Bind<bool>(new ConfigDefinition("Settings", "Spirit Punch"), true, new ConfigDescription("Increase fire rate.")).Value;
            ConfigOptions.buffUnseenHand = base.Config.Bind<bool>(new ConfigDefinition("Settings", "Unseen Hand"), true, new ConfigDescription("Damage scales with Tranquility stacks.")).Value;
            ConfigOptions.buffMeditate = base.Config.Bind<bool>(new ConfigDefinition("Settings", "Meditate"), true, new ConfigDescription("Stun and increase damage.")).Value;
        }

        private void BuffSpiritPunch()
        {
            if (!ConfigOptions.buffSpiritPunch) return;

            IL.EntityStates.Seeker.SpiritPunch.OnEnter += SpiritPunch_OnEnter;

            GameObject projectilePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Seeker/SpiritPunchFinisherProjectile.prefab").WaitForCompletion();
            ProjectileImpactExplosion pie = projectilePrefab.GetComponent<ProjectileImpactExplosion>();
            pie.blastProcCoefficient = 1f;
        }

        private void SpiritPunch_OnEnter(MonoMod.Cil.ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.After, x => x.MatchLdfld(typeof(EntityStates.Seeker.SpiritPunch), "baseDuration")))
            {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float, EntityStates.Seeker.SpiritPunch, float>>((duration, self) =>
                {
                    return duration * ((self.gauntlet != EntityStates.Seeker.SpiritPunch.Gauntlet.Explode) ? 0.5f : 1f);
                });
            }
            else
            {
                Debug.LogError("RiskySeeker: Spirit Punch IL Hook failed.");
            }
        }

        private void BuffUnseenHand()
        {
            if (!ConfigOptions.buffUnseenHand) return;
            IL.EntityStates.Seeker.UnseenHand.FixedUpdate += UnseenHand_FixedUpdate;
            GameObject projectilePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Seeker/UnseenHandMovingProjectile.prefab").WaitForCompletion();
            UnseenHandHealingProjectile u = projectilePrefab.GetComponent<UnseenHandHealingProjectile>();
            u.chakraIncrease = 0f;
        }

        private void UnseenHand_FixedUpdate(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(MoveType.After, x => x.MatchLdfld(typeof(EntityStates.BaseState), "damageStat")))
            {
                c.Emit(OpCodes.Ldarg_0);
                c.EmitDelegate<Func<float, EntityStates.Seeker.UnseenHand, float>>((damage, self) =>
                {
                    float mult = 1f;
                    if (self.characterBody)
                    {
                        mult += (float) self.characterBody.GetBuffCount(DLC2Content.Buffs.ChakraBuff) / 6f;
                    }
                    return damage * mult;
                });
            }
            else
            {
                Debug.LogError("RiskySeeker: Unseen Hand IL Hook failed.");
            }
        }

        private void BuffMeditate()
        {
            if (!ConfigOptions.buffMeditate) return;
            IL.EntityStates.Seeker.MeditationUI.Update += MeditationUI_Update;
            SetAddressableEntityStateField("RoR2/DLC2/Seeker/EntityStates.Seeker.Meditate2.asset", "damageCoefficient", "9");
            On.RoR2.Language.SetCurrentLanguage += Language_SetCurrentLanguage;
            On.RoR2.SeekerController.CmdTriggerHealPulse += SeekerController_CmdTriggerHealPulse;
        }

        private static GameObject cleanseEffect = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Seeker/SpiritPunchMuzzleFlashVFX.prefab").WaitForCompletion();
        private void SeekerController_CmdTriggerHealPulse(On.RoR2.SeekerController.orig_CmdTriggerHealPulse orig, SeekerController self, float value, Vector3 corePosition, float blastRadius)
        {
            orig(self, value, corePosition, blastRadius);

            if (!NetworkServer.active) return;
            List<ProjectileController> instancesList = InstanceTracker.GetInstancesList<ProjectileController>();
            List<GameObject> toDestroy = new List<GameObject>();
            foreach (ProjectileController pc in instancesList)
            {
                TeamIndex friendlyTeam = self.characterBody.teamComponent ? self.characterBody.teamComponent.teamIndex : TeamIndex.None;
                if (pc.cannotBeDeleted || pc.teamFilter.teamIndex == friendlyTeam || (pc.transform.position - self.transform.position).sqrMagnitude >= blastRadius * blastRadius) continue;
                toDestroy.Add(pc.gameObject);
            }

            GameObject[] toDestroy2 = toDestroy.ToArray();
            for (int i = 0; i < toDestroy2.Length; i++)
            {
                EffectManager.SimpleEffect(cleanseEffect, toDestroy2[i].transform.position, toDestroy2[i].transform.rotation, true);
                UnityEngine.Object.Destroy(toDestroy2[i]);
            }
        }

        private void MeditationUI_Update(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (c.TryGotoNext(x => x.MatchCallvirt<BlastAttack>("Fire")))
            {
                c.EmitDelegate<Func<BlastAttack, BlastAttack>>(blast =>
                {
                    blast.damageType.damageType |= DamageType.Stun1s;
                    return blast;
                });
            }
            else
            {
                Debug.LogError("RiskySeeker: Meditate IL Hook failed.");
            }
        }

        private void Language_SetCurrentLanguage(On.RoR2.Language.orig_SetCurrentLanguage orig, string newCurrentLanguageName)
        {
            orig(newCurrentLanguageName);
            string currentSkillDesc = RoR2.Language.GetString("SEEKER_SPECIAL_DESCRIPTION", newCurrentLanguageName).Replace("450", "900");
            Language.currentLanguage.SetStringByToken("SEEKER_SPECIAL_DESCRIPTION", currentSkillDesc);
        }

        internal static bool SetAddressableEntityStateField(string fullEntityStatePath, string fieldName, string value)
        {
            EntityStateConfiguration esc = Addressables.LoadAssetAsync<EntityStateConfiguration>(fullEntityStatePath).WaitForCompletion();
            for (int i = 0; i < esc.serializedFieldsCollection.serializedFields.Length; i++)
            {
                if (esc.serializedFieldsCollection.serializedFields[i].fieldName == fieldName)
                {
                    esc.serializedFieldsCollection.serializedFields[i].fieldValue.stringValue = value;
                    return true;
                }
            }
            return false;
        }
    }
}

namespace R2API.Utils
{
    [AttributeUsage(AttributeTargets.Assembly)]
    public class ManualNetworkRegistrationAttribute : Attribute
    {
    }
}
