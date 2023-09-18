using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PersonalFarm", "bmgjet", "1.0.8")]
    class PersonalFarm : RustPlugin
    {
        #region Declarations
        const string perm = "PersonalFarm.use";
        private static PluginConfig config;
        private static SaveData _data;
        const int layerMask = Rust.Layers.Mask.Terrain | Rust.Layers.Mask.Construction;
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
            public List<ulong> PlacedEntitys = new List<ulong>();
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
                if (_data.PlacedEntitys.Contains(PersonalFarmEntity.net.ID.Value))
                {
                    if (PersonalFarmEntity.GetComponent<PersonalFarmAddon>() == null)
                    {
                        Puts("Found PersonalFarm Entity " + PersonalFarmEntity.ToString() + " " + PersonalFarmEntity.gameObject.ToBaseEntity().OwnerID.ToString() + " Adding Component");
                        PersonalFarmEntity.gameObject.AddComponent<PersonalFarmAddon>();
                    }
                }
            }
            timer.Every(0.1f, LoopPlayers); // might want to change this interval
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

        void LoopPlayers() // removed OnPlayerInput hook as it's very expensive
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (!player.svActiveItemID.IsValid || !permission.UserHasPermission(player.UserIDString, perm) || !player.serverInput.WasJustPressed(BUTTON.FIRE_PRIMARY)) // WasJustPressed might also require IsDown and/or IsDown WasDown instead
                {
                    continue;
                }
                var item = player.GetActiveItem();
                if (item == null)
                {
                    continue;
                }
                if (item.skin != 0) //Check skin to make sure its not item used elsewhere
                {
                    continue;
                }
                //Check if item in list
                foreach (var shortname in config.itemlist)
                {
                    if (item.info.shortname.Contains(shortname.Key))
                    {
                        if (player.CanBuild() && PlaceItem(player, shortname.Value)) // check CanBuild at last possible chance to minimize impact on performance
                        {
                            item.UseItem(1);
                        }
                        break;
                    }
                }
            }
        }
        #endregion

        #region Code
        private bool PlaceItem(BasePlayer player, string Selected)
        {
            if (!Physics.Raycast(player.eyes.HeadRay(), out var rhit) || rhit.GetEntity() is not BaseEntity entity)
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
                Quaternion q = player.eyes.rotation;
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
                _data.PlacedEntitys.Add(newentity.net.ID.Value);
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
                if (hit.GetEntity() is BaseEntity ent)
                {
                    var str = ent.ToString();
                    if (str.Contains("wall")) //Stop playing too close to a wall.
                    {
                        return false;
                    }
                    foreach (KeyValuePair<string, string> Itemcheck in config.itemlist)
                    {
                        if (str.Contains(Itemcheck.Key))
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
            private bool isDestroyed;

            private void Awake()
            {
                FarmEntity = GetComponent<BaseEntity>();
                InvokeRepeating("CheckGround", 5f, 5f);
            }

            private void CheckGround() // fixed NRE and handle destroy
            {
                if (isDestroyed)
                {
                    return;
                }
                if (!FarmEntity.IsValid() || FarmEntity.IsDestroyed)
                {
                    isDestroyed = true;
                    Destroy(this);
                    return;
                }
                var cast = Physics.Raycast(FarmEntity.transform.position + new Vector3(0, 0.1f), Vector3.down, out var rhit, 1f, layerMask);
                var distance = cast ? rhit.distance : 1f;
                if (distance > 0.2f)
                {
                    isDestroyed = true;
                    _data.PlacedEntitys?.Remove(FarmEntity.net.ID.Value);
                    FarmEntity.Kill();
                    Destroy(this);
                }
            }
            #endregion
        }
    }
}