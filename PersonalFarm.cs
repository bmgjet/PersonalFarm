using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PersonalFarm", "bmgjet", "1.0.2")]
    class PersonalFarm : RustPlugin
    {
        #region Declarations
        const string perm = "PersonalFarm.use";
        public float itemspacing = 1f;
        public Dictionary<string, string> PlaceAnywhere = new Dictionary<string, string> { { "furnace.large", "assets/prefabs/deployable/furnace.large/furnace.large.prefab" }, { "refinery", "assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab" } };
        #endregion

        #region Hooks
        void Init()
        {
            permission.RegisterPermission(perm, this);
        }

        private void OnServerInitialized()
        {
            foreach (var PersonalFarmEntity in GameObject.FindObjectsOfType<BaseEntity>())
            {
                if (PersonalFarmEntity.name == "PersonalFarm")
                {
                    if (PersonalFarmEntity.GetComponent<PersonalFarmAddon>() == null)
                    {
                        Puts("Found PersonalFarm Entity " + PersonalFarmEntity.ToString() + " " + PersonalFarmEntity.OwnerID.ToString() + " Adding Component");
                        PersonalFarmEntity.gameObject.AddComponent<PersonalFarmAddon>();
                    }
                }
            }
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
            foreach (KeyValuePair<string, string> shortname in PlaceAnywhere)
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
            if (rhit.distance > 5f || (!entity.ShortPrefabName.Contains("floor") && !entity.ShortPrefabName.Contains("foundation")))
            {
                return false;
            }
            var newentity = GameManager.server.CreateEntity(Selected);
            if (newentity == null)
            {
                return false;
            }
            if (CanPlace(rhit.point, itemspacing))
            {
                newentity.transform.position = rhit.point;
                newentity.OwnerID = player.userID;
                newentity.name = "PersonalFarm";
                newentity.gameObject.AddComponent<PersonalFarmAddon>();
                newentity.Spawn();
                
                return true;
            }
            else
            {
                player.ChatMessage("<color=red>Too close to another item</color>");
                return false;
            }
        }

        private bool CanPlace(Vector3 pos, float radius)
        {
            var hits = Physics.SphereCastAll(pos, radius, Vector3.up);
            foreach (var hit in hits)
            {
                if (hit.GetEntity() != null)
                {
                    foreach (KeyValuePair<string, string> Itemcheck in PlaceAnywhere)
                    {
                        if (hit.GetEntity().ToString().Contains(Itemcheck.Key))
                            return false;
                    }
                }
            }
            return true;
        }

        #region Scripts
        private class PersonalFarmAddon : MonoBehaviour
        {
            private BaseEntity FarmEntity;

            private void Awake()
            {
                FarmEntity = GetComponent<BaseEntity>();
                InvokeRepeating("CheckGround", 5f, 5f);
            }

            private void CheckGround()
            {
                RaycastHit rhit;
                var cast = Physics.Raycast(FarmEntity.transform.position + new Vector3(0, 0.1f, 0), Vector3.down,
                    out rhit, 4f, LayerMask.GetMask("Terrain", "Construction"));
                var distance = cast ? rhit.distance : 3f;
                if (distance > 0.2f) { GroundMissing(); }
            }

            private void GroundMissing()
            {
                this.DoDestroy();
            }

            public void DoDestroy()
            {
                var entity = FarmEntity;
                try { entity.Kill(); } catch { }
            }
        }
        #endregion
    }
}