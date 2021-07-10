using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PersonalFarm", "bmgjet", "1.0.3")]
    class PersonalFarm : RustPlugin
    {
        #region Declarations
        const string perm = "PersonalFarm.use";
        public float itemspacing = 1.5f;
        private const string PREFAB_CRATER_OIL = "assets/prefabs/tools/surveycharge/survey_crater_oil.prefab";
        public Dictionary<string, string> PlaceAnywhere = new Dictionary<string, string> { { "fishtrap.small", "assets/prefabs/deployable/survivalfishtrap/survivalfishtrap.deployed.prefab" }, { "water.catcher.small", "assets/prefabs/deployable/water catcher/water_catcher_small.prefab" }, { "furnace.large", "assets/prefabs/deployable/furnace.large/furnace.large.prefab" }, { "refinery", "assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab" } };
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
            if (heldEntity.skin != 0)
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

        //Add back in pumpjacks
        private void OnEntityKill(SurveyCharge surveyCharge)
        {
            if (surveyCharge == null || surveyCharge.net == null) return;
            ModifyResourceDeposit(surveyCharge.transform.position, surveyCharge.OwnerID);
        }

        private void ModifyResourceDeposit(Vector3 checkPosition, ulong playerID)
        {
            NextTick(() =>
            {
                var surveyCraterList = Pool.GetList<SurveyCrater>();
                Vis.Entities(checkPosition, 1f, surveyCraterList, Rust.Layers.Mask.Default);
                foreach (var surveyCrater in surveyCraterList)
                {
                    if (UnityEngine.Random.Range(0f, 100f) < 40)
                    {
                        Vector3 CraterPos = surveyCrater.transform.position;
                        CraterPos.y -= 0.07f;
                        var oilCrater = GameManager.server.CreateEntity(PREFAB_CRATER_OIL, CraterPos) as SurveyCrater;
                        if (oilCrater == null) continue;
                        surveyCrater.Kill();
                        oilCrater.OwnerID = playerID;
                        oilCrater.Spawn();
                        var deposit = ResourceDepositManager.GetOrCreate(oilCrater.transform.position);
                        if (deposit != null)
                        {
                            deposit._resources.Clear();
                            int amount = UnityEngine.Random.Range(50, 500);
                            float workNeeded = 45f / UnityEngine.Random.Range(10, 40);
                            var crudeItemDef = ItemManager.FindItemDefinition("crude.oil");
                            if (crudeItemDef != null)
                            {
                                deposit.Add(crudeItemDef, 1, amount, workNeeded, ResourceDepositManager.ResourceDeposit.surveySpawnType.ITEM, true);
                            }
                        }
                    }
                }
                Pool.FreeList(ref surveyCraterList);
            });
        }
        //
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
                player.ChatMessage("<color=red>Too close to another item or wall!</color>");
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
                    if (hit.GetEntity().ToString().Contains("wall"))
                    {
                        return false;
                    }
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