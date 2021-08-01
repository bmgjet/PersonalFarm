using System.Collections.Generic;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PersonalFarm", "bmgjet", "1.0.6")]
    class PersonalFarm : RustPlugin
    {
        #region Declarations
        const string perm = "PersonalFarm.use";
        private static PluginConfig config;
        private static SaveData _data;
        #endregion

        #region Configuration
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Item Spacing : ")] public float itemspacing { get; set; }
            [JsonProperty(PropertyName = "Items To Allow Indoors: ")] public Dictionary<string, string> itemlist { get; set; }
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig
            {
                itemspacing = 1.8f, //Foundation = 3x3
                itemlist = new Dictionary<string, string>
                {
                    { "fishtrap.small", "assets/prefabs/deployable/survivalfishtrap/survivalfishtrap.deployed.prefab" },
                    { "water.catcher.small", "assets/prefabs/deployable/water catcher/water_catcher_small.prefab" },
                    { "furnace.large", "assets/prefabs/deployable/furnace.large/furnace.large.prefab" },
                    { "refinery", "assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab" }
                },
            };
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Config.WriteObject(GetDefaultConfig(), true);
            config = Config.ReadObject<PluginConfig>();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        private void WriteSaveData() =>
        Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        class SaveData
        {
            public List<uint> PlacedEntitys = new List<uint>();
        }
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);

            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(Name))
            {
                Interface.Oxide.DataFileSystem.GetDatafile(Name).Save();
            }

            _data = Interface.Oxide.DataFileSystem.ReadObject<SaveData>(Name);
            if (_data == null)
            {
                WriteSaveData();
            }

            config = Config.ReadObject<PluginConfig>();
            if (config == null)
            {
                LoadDefaultConfig();
            }
        }

        private void OnServerSave()
        {
            WriteSaveData();
        }

        private void OnNewSave(string filename)
        {
            _data.PlacedEntitys.Clear();
            WriteSaveData();
        }

        private void OnServerInitialized()
        {
            foreach (var PersonalFarmEntity in BaseNetworkable.serverEntities)
            {
                if (_data.PlacedEntitys.Contains(PersonalFarmEntity.net.ID))
                {
                    if (PersonalFarmEntity.GetComponent<PersonalFarmAddon>() == null)
                    {
                        Puts("Found PersonalFarm Entity " + PersonalFarmEntity.ToString() + " " + PersonalFarmEntity.gameObject.ToBaseEntity().OwnerID.ToString() + " Adding Component");
                        PersonalFarmEntity.gameObject.AddComponent<PersonalFarmAddon>();
                    }
                }
            }
        }

        void Unload()
        {
            WriteSaveData();
            _data.PlacedEntitys = null;
            if (config != null)
                config = null;

            if (_data != null)
                _data = null;
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!permission.UserHasPermission(player.UserIDString, perm) || !input.WasJustPressed(BUTTON.FIRE_PRIMARY) || !player.CanBuild())
            {
                return;
            }
            var heldEntity = player.GetActiveItem();
            if (heldEntity == null)
            {
                return;
            }
            if (heldEntity.skin != 0) //Check skin to make sure its not item used elsewhere
            {
                return;
            }

            //Check if item in list
            foreach (KeyValuePair<string, string> shortname in config.itemlist)
            {
                if (heldEntity.info.shortname.Contains(shortname.Key))
                {
                    if (PlaceItem(player, shortname.Value))
                    {
                        player.inventory.Take(null, heldEntity.info.itemid, 1);
                        return;
                    }
                    else
                    {
                        return;
                    }
                }
            }
        }
        #endregion

        #region Code
        private bool PlaceItem(BasePlayer player, string Selected)
        {
            RaycastHit rhit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out rhit))
            {
                return false;
            }
            var entity = rhit.GetEntity();
            if (entity == null)
            {
                return false;
            }
            //Allow place on floor and foundation
            if (rhit.distance > 5f || (!entity.ShortPrefabName.Contains("floor") && !entity.ShortPrefabName.Contains("foundation")))
            {
                return false;
            }

            if (CanPlace(rhit.point, config.itemspacing))
            {
                Quaternion q = new Quaternion();
                q = player.eyes.rotation;
                q.Set(0, q.y, 0, q.w);
                var newentity = GameManager.server.CreateEntity(Selected, rhit.point, q, true);
                if (newentity == null)
                {
                    return false;
                }
                newentity.transform.position = rhit.point;
                newentity.OwnerID = player.userID;
                newentity.gameObject.AddComponent<PersonalFarmAddon>();
                newentity.Spawn();
                _data.PlacedEntitys.Add(newentity.net.ID);
                return true;
            }
            else
            {
                player.ChatMessage("<color=red>Too close to another item or wall!</color>");
                return false;
            }
        }

        private bool CanPlace(Vector3 pos, float radius)
        {
            //Check if can place with in spacing limits
            var hits = Physics.SphereCastAll(pos, radius, Vector3.up);
            foreach (var hit in hits)
            {
                if (hit.GetEntity() != null)
                {
                    if (hit.GetEntity().ToString().Contains("wall")) //Stop playing too close to a wall.
                    {
                        return false;
                    }
                    foreach (KeyValuePair<string, string> Itemcheck in config.itemlist)
                    {
                        if (hit.GetEntity().ToString().Contains(Itemcheck.Key))
                            return false;
                    }
                }
            }
            return true;
        }
        #endregion

        #region Scripts
        private class PersonalFarmAddon : MonoBehaviour
        {
            private BaseEntity FarmEntity;
            private BaseNetworkable networkid;

            private void Awake()
            {
                FarmEntity = GetComponent<BaseEntity>();
                networkid = GetComponent<BaseNetworkable>();
                InvokeRepeating("CheckGround", 5f, 5f);
            }

            private void CheckGround()
            {
                if (FarmEntity == null || networkid == null) return;
                try
                {
                    RaycastHit rhit;
                    var cast = Physics.Raycast(FarmEntity.transform.position + new Vector3(0, 0.1f, 0), Vector3.down,
                        out rhit, 1f, LayerMask.GetMask("Terrain", "Construction"));
                    var distance = cast ? rhit.distance : 1f;
                    if (distance > 0.2f)
                    {

                        if (_data.PlacedEntitys != null)
                        {
                            if (_data.PlacedEntitys.Contains(networkid.net.ID))
                            {
                                _data.PlacedEntitys.Remove(networkid.net.ID);
                            }
                        }
                        FarmEntity.Kill();
                        CancelInvoke("CheckGround");
                    }
                }
                catch
                {
                    //Try catch for rare null reference error if item is remove halfway though a check. 
                }
            }
            #endregion
        }
    }
}