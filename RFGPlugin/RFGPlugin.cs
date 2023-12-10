using BepInEx;
using R2API;
using R2API.Utils;
using RoR2;
using System.Security;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using HarmonyLib;
using System;
using BepInEx.Configuration;

[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RFGPlugin
{
	//This is an example plugin that can be put in BepInEx/plugins/RFGPlugin/RFGPlugin.dll to test out.
    //It's a small plugin that adds a relatively simple item to the game, and gives you that item whenever you press F2.

    //This attribute specifies that we have a dependency on R2API, as we're using it to add our item to the game.
    //You don't need this if you're not using R2API in your plugin, it's just to tell BepInEx to initialize R2API before this plugin so it's safe to use R2API.
    [BepInDependency(R2API.R2API.PluginGUID)]
	
	//This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
	
	//We will be using 2 modules from R2API: ItemAPI to add our item and LanguageAPI to add our language tokens.
    [R2APISubmoduleDependency(nameof(ItemAPI), nameof(LanguageAPI), nameof(RecalculateStatsAPI))]
	
	//This is the main declaration of our plugin class. BepInEx searches for all classes inheriting from BaseUnityPlugin to initialize on startup.
    //BaseUnityPlugin itself inherits from MonoBehaviour, so you can use this as a reference for what you can declare and use in your plugin class: https://docs.unity3d.com/ScriptReference/MonoBehaviour.html
    public class RFGPlugin : BaseUnityPlugin
	{
        //The Plugin GUID should be a unique ID for this plugin, which is human readable (as it is used in places like the config).
        //If we see this PluginGUID as it is on thunderstore, we will deprecate this mod. Change the PluginAuthor and the PluginName !
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "nobleRadical";
        public const string PluginName = "RFGPlugin";
        public const string PluginVersion = "1.3.1";

        //Plugin Info
        public static PluginInfo PInfo { get; private set; }

        //Config
        public static ConfigEntry<float> HealFactor { get; set; }


        //We need our item definition to persist through our functions, and therefore make it a class field.
        private static ItemDef myItemDef;

        //Constants
        
        private Color purple = new Color32(135, 0, 255, 255); //shield color
        /// private const float healFactor = 0.1f; //delay reduction per health point healed


        //The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            //Init our logging class so that we can properly log for debugging
            Log.Init(Logger);

            //asset stuff
            PInfo = Info;

            //config setup
            HealFactor = Config.Bind<float>("base", "Healing Factor", 0.1f, "The amount that shield recharge delay is decreased for every health point healed.");

            //First let's define our item
            myItemDef = ScriptableObject.CreateInstance<ItemDef>();

            // Language Tokens, check AddTokens() below.
            myItemDef.name = "RFG";
            myItemDef.nameToken = "RFG_NAME";
            myItemDef.pickupToken = "RFG_PICKUP";
            myItemDef.descriptionToken = "RFG_DESC";
            myItemDef.loreToken = "RFG_LORE";

            //The tier determines what rarity the item is:
            //Tier1=white, Tier2=green, Tier3=red, Lunar=Lunar, Boss=yellow,
            //and finally NoTier is generally used for helper items, like the tonic affliction
            myItemDef.deprecatedTier = ItemTier.VoidTier1;
            /// myItemDef.tier = DLC1Content.Items.BearVoid.tier;
            myItemDef.tags = new ItemTag[1] { ItemTag.Utility };

            Assets.Init();
            //You can create your own icons and prefabs through assetbundles
            Log.LogInfo(string.Join(", ", Assets.mainBundle.GetAllAssetNames()));
            myItemDef.pickupIconSprite = Assets.mainBundle.LoadAsset<Sprite>("Assets/texVoidShieldIconSmol.png");
            //myItemDef.pickupIconSprite = Addressables.LoadAssetAsync<Sprite>("RoR2/Base/PersonalShield/texPersonalShieldIcon.png").WaitForCompletion();
            myItemDef.pickupModelPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/PersonalShield/PickupShieldGenerator.prefab").WaitForCompletion();

            //Can remove determines if a shrine of order, or a printer can take this item, generally true, except for NoTier items.
            myItemDef.canRemove = true;

            //Hidden means that there will be no pickup notification,
            //and it won't appear in the inventory at the top of the screen.
            //This is useful for certain noTier helper items, such as the DrizzlePlayerHelper.
            myItemDef.hidden = false;
			
            //Now let's turn the tokens we made into actual strings for the game:
            AddTokens();

            //You can add your own display rules here, where the first argument passed are the default display rules: the ones used when no specific display rules for a character are found.
            //For this example, we are omitting them, as they are quite a pain to set up without tools like ItemDisplayPlacementHelper
            var displayRules = new ItemDisplayRuleDict(null);

            //Then finally add it to R2API
            ItemAPI.Add(new CustomItem(myItemDef, displayRules));

            //make it void.
            On.RoR2.Items.ContagiousItemManager.Init += (orig) =>
            {
                ItemDef.Pair pair = new ItemDef.Pair
                {
                    itemDef1 = RoR2Content.Items.PersonalShield,
                    itemDef2 = myItemDef
                };
                ItemCatalog.itemRelationships[DLC1Content.ItemRelationshipTypes.ContagiousItem] = ItemCatalog.itemRelationships[DLC1Content.ItemRelationshipTypes.ContagiousItem].AddToArray(pair);
                orig();
            };


            //But now we have defined an item, but it doesn't do anything yet. So we'll need to define that ourselves.
            //The void giveth, and the void taketh away.
            RecalculateStatsAPI.GetStatCoefficients += (self, args) =>
            {
                if (self.inventory != null)
                {
                    //define inputs
                    int RFGcount = self.inventory.GetItemCount(myItemDef.itemIndex);
                    float health = self.maxHealth;
                    float shield = self.maxShield;
                    //define outputs
                    float healthToSubtract = 0;
                    float shieldToAdd = 0;
                    //logic
                    // add (8% per item) of health as shields.
                    shieldToAdd = (0.08f * RFGcount) * health;
                    // remove (4% per item) of health.
                    healthToSubtract = (0.04f * RFGcount) * health;

                    if (health - healthToSubtract < 1) // on the other hand, RFG shouldn't kill the player.
                    {
                        healthToSubtract = health - 1;
                    } // if it would, we'll just add shields instead. (This might be later changed for balance reasons.)

                    //do operation
                    args.baseHealthAdd += -(healthToSubtract);
                    args.baseShieldAdd += shieldToAdd;
                }
            };
            //decrease shield delay on heal; check for inventory and characterBody
            On.RoR2.HealthComponent.Heal += (orig, self, amt, proc, nonRegen) =>
            {
                if (self.body != null)
                {
                    CharacterBody character = self.GetComponentInParent<CharacterBody>();
                    if (character.inventory != null)
                    {
                        int RFGcount = character.inventory.GetItemCount(myItemDef.itemIndex);
                        bool hasTranscendence = character.inventory.GetItemCount(RoR2Content.Items.ShieldOnly.itemIndex) > 0;
                        if (RFGcount > 0 & nonRegen)
                        {
                            character.outOfDangerStopwatch += HealFactor.Value * amt;
                        }
                        if (hasTranscendence & character.outOfDanger)
                        {
                            self.CallCmdRechargeShieldFull();
                        }
                    }
                }
                orig(self, amt, proc, nonRegen);
                return 0;
            };

            //make the shield purple lol
            On.RoR2.UI.HealthBar.UpdateBarInfos += HealthBar_UpdateBarInfos;
            On.RoR2.UI.HealthBar.OnEnable += HealthBar_OnEnable;
            On.RoR2.UI.HealthBar.OnInventoryChanged += HealthBar_OnInventoryChanged;
            /// On.RoR2.CharacterSpawnCard.Spawn += CharacterSpawnCard_Spawn;


            // This line of log will appear in the bepinex console when the Awake method is done.
            Log.LogInfo(nameof(Awake) + " done.");
        }


        private void HealthBar_OnInventoryChanged(On.RoR2.UI.HealthBar.orig_OnInventoryChanged orig, RoR2.UI.HealthBar self)
        {
            orig(self);
            int RFGcount = self.source.body.inventory.GetItemCount(myItemDef);
            if (RFGcount > 0)
            {
                self.barInfoCollection.shieldBarInfo.color = purple;
            }
        }
        private void HealthBar_OnEnable(On.RoR2.UI.HealthBar.orig_OnEnable orig, RoR2.UI.HealthBar self)
        {
            orig(self);
            int RFGcount = self.source.body.inventory.GetItemCount(myItemDef);
            if (RFGcount > 0)
            {
                self.barInfoCollection.shieldBarInfo.color = purple;
            }
        }

        private void HealthBar_UpdateBarInfos(On.RoR2.UI.HealthBar.orig_UpdateBarInfos orig, RoR2.UI.HealthBar self)
        {
            bool purpleShield = self.barInfoCollection.shieldBarInfo.color == purple ? true : false;
            orig(self);
            if (purpleShield)
            {
                self.barInfoCollection.shieldBarInfo.color = purple;
            }
        }


        //This function adds the tokens from the item using LanguageAPI, the comments in here are a style guide, but is very opiniated. Make your own judgements!
        private void AddTokens()
        {
            //The Name should be self explanatory
            LanguageAPI.Add("RFG_NAME", "Resonance Field Generator");

            //The Pickup is the short text that appears when you first pick this up. This text should be short and to the point, numbers are generally ommited.
            LanguageAPI.Add("RFG_PICKUP", "Trade health for a <style=cIsHealing>Regenerating Shield</style>. Recharge faster by healing. <style=cIsVoid>Corrupts all Personal Shield Generators.</style>");

            //The Description is where you put the actual numbers and give an advanced description.
            LanguageAPI.Add("RFG_DESC", $"Trade <style=cIsHealing>4%</style> <style=cStack>(+4% per stack)</style> health for <style=cIsHealing>8%</style> <style=cStack>(+8% per stack)</style> <style=cIsHealing>Regenerating Shield</style>. Each health point healed <style=cArtifact>Decreases recharge delay</style> by {HealFactor.Value} seconds. If Transcendence is active, healing while recharging shield fully recharges it. <style=cIsVoid>Corrupts all Personal Shield Generators.</style>");
            
            //The Lore is, well, flavor. You can write pretty much whatever you want here.
            LanguageAPI.Add("RFG_LORE", @"It's interesting to see how the void...

innovates...

on human designs.

-Lost Journal, recovered from Petrichor V");
        }
    }
    public static class Assets
    {
        //The mod's AssetBundle
        public static AssetBundle mainBundle;
        //A constant of the AssetBundle's name.
        public const string bundleName = "shieldbundle";

        //The direct path to your AssetBundle
        public static string AssetBundlePath
        {
            get
            {
                //This returns the path to your assetbundle assuming said bundle is on the same folder as your DLL. If you have your bundle in a folder, you can uncomment the statement below this one.
                return System.IO.Path.Combine(RFGPlugin.PInfo.Location, "..", bundleName);
            }
        }

        public static void Init()
        {
            //Loads the assetBundle from the Path, and stores it in the static field.
            mainBundle = AssetBundle.LoadFromFile(AssetBundlePath);
            Log.LogInfo("RFG Assets Initialized.");
        }
    }
}
