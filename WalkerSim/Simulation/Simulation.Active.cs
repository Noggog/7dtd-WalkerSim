using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using UnityEngine;

namespace WalkerSim
{
    partial class Simulation
    {
        List<ZombieAgent> _activeZombies = new();
        Queue<ZombieSpawnRequest> _spawnQueue = new();
        
        public static bool IsSpawnProtected(Vector3 pos)
        {
            var world = GameManager.Instance.World;
            var players = world.Players.list;

            foreach (var ply in players)
            {
                for (int i = 0; i < ply.SpawnPoints.Count; ++i)
                {
                    var spawnPos = ply.SpawnPoints[i].ToVector3();
                    var dist = Vector3.Distance(pos, spawnPos);
                    if (dist <= 50)
                        return true;
                }
            }
            return false;
        }

        public static bool CanZombieSpawnAt(Vector3 pos)
        {
            var world = GameManager.Instance.World;

            if (!world.CanMobsSpawnAtPos(pos))
                return false;

            if (IsSpawnProtected(pos))
                return false;

            return true;
        }

        bool CanSpawnActiveZombie()
        {
            int alive = GameStats.GetInt(EnumGameStats.EnemyCount);
            if (alive + 1 >= MaxSpawnedZombies)
                return false;
            return true;
        }

        bool CreateZombie(ZombieAgent zombie, PlayerZone zone)
        {
            if (!CanSpawnActiveZombie())
            {
                return false;
            }

            var world = GameManager.Instance.World;
            Chunk? chunk = world.GetChunkSync(World.toChunkXZ(Mathf.FloorToInt(zombie.pos.x)), 0, World.toChunkXZ(Mathf.FloorToInt(zombie.pos.z))) as Chunk;
            if (chunk == null)
            {
                Logger.Debug("Chunk not loaded at {0} {1}", zombie.pos, zombie.pos.z);
                return false;
            }

            int height = world.GetTerrainHeight(Mathf.FloorToInt(zombie.pos.x), Mathf.FloorToInt(zombie.pos.z));

            Vector3 spawnPos = new Vector3(zombie.pos.x, height + 1.0f, zombie.pos.z);
            if (!CanZombieSpawnAt(spawnPos))
            {
                Logger.Debug("Unable to spawn zombie at {0}, CanMobsSpawnAtPos failed", spawnPos);
                return false;
            }

            if (zombie.classId == -1)
            {
                zombie.classId = _biomeData.GetZombieClass(world, chunk, (int)spawnPos.x, (int)spawnPos.z, _prng);
                if (zombie.classId == -1)
                {
                    int lastClassId = -1;
                    zombie.classId = EntityGroups.GetRandomFromGroup("ZombiesAll", ref lastClassId);
                    Logger.Debug("Used fallback for zombie class!");
                }
            }

            if (EntityFactory.CreateEntity(zombie.classId, spawnPos) is not EntityZombie zombieEnt)
            {
                Logger.Error("Unable to create zombie entity!, Entity Id: {0}, Pos: {1}", zombie.classId, spawnPos);
                return false;
            }

            zombieEnt.bIsChunkObserver = true;

            // TODO: Figure out a better way to make them walk towards something.
            // Send zombie towards a random position in the zone.
            var targetPos = GetRandomZonePos(zone);
            if (targetPos == null)
            {
                Logger.Error("Had to send zombie to center zone.");
                targetPos = zone.center;
            }
            zombieEnt.SetInvestigatePosition(targetPos.Value, 6000, false);

            // If the zombie was previously damaged take health to this one.
            if (zombie.health != -1)
                zombieEnt.Health = zombie.health;
            else
                zombie.health = zombieEnt.Health;

            zombieEnt.IsHordeZombie = true;
            zombieEnt.IsBloodMoon = _state.IsBloodMoon;

            zombieEnt.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);

            world.SpawnEntityInWorld(zombieEnt);

            var active = zombie.MakeActive();
            active.entityId = zombieEnt.entityId;
            active.currentZone = zone;
            active.lifeTime = world.GetWorldTime();
            active.intendedGoal = targetPos.Value;

            zone.numZombies++;

            Logger.Debug("[{0}] Spawned zombie {1} at {2}", zombie.id, zombieEnt, spawnPos);
            lock (_activeZombies)
            {
                _activeZombies.Add(zombie);
            }

            return true;
        }

        void RequestActiveZombie(ZombieAgent zombie, PlayerZone zone)
        {
            zombie.state = ZombieAgent.State.Active;

            ZombieSpawnRequest spawn = new(zombie, zone);
            lock (_spawnQueue)
            {
                _spawnQueue.Enqueue(spawn);
            }
        }

        void ProcessSpawnQueue()
        {
            for (int i = 0; i < MaxZombieSpawnsPerTick; i++)
            {
                ZombieSpawnRequest zombieSpawn;
                lock (_spawnQueue)
                {
                    if (_spawnQueue.Count == 0)
                        break;
                    zombieSpawn = _spawnQueue.Dequeue();
                }
                if (!CreateZombie(zombieSpawn.zombie, zombieSpawn.zone))
                {
                    // Failed to spawn zombie, keep population size.
                    RespawnInactiveZombie(zombieSpawn.zombie);
                }
            }
        }

        int MaxZombiesPerZone()
        {
            return MaxSpawnedZombies / Math.Max(1, ConnectionManager.Instance.Clients.Count);
        }

        bool UpdateActiveZombie(ZombieActiveAgent zombie)
        {
            var world = GameManager.Instance.World;
            int maxPerZone = MaxZombiesPerZone();

            bool removeZombie = false;

            var worldTime = world.GetWorldTime();
            var timeAlive = worldTime - zombie.lifeTime;

            if (zombie.currentZone is PlayerZone currentZone)
            {
                currentZone.numZombies--;
                if (currentZone.numZombies < 0)
                    currentZone.numZombies = 0;
            }
            zombie.currentZone = null;

            if (world.GetEntity(zombie.entityId) is not EntityZombie ent)
            {
                Logger.Debug("[{0}] Failed to get zombie with entity id {0}", zombie.entityId);
                removeZombie = true;
                RespawnInactiveZombie(zombie.Parent);
            }
            else
            {
                zombie.Parent.pos = ent.GetPosition();
                zombie.Parent.health = ent.Health;

                if (ent.IsDead())
                {
                    removeZombie = true;
                    RespawnInactiveZombie(zombie.Parent);
                }
                else
                {
                    List<PlayerZone> zones = _playerZones.FindAllByPos2D(ent.GetPosition());
                    if ((zones.Count == 0 || zombie.CurrentState == ZombieActiveAgent.State.WantsDespawn)
                        && timeAlive >= MinZombieLifeTime)
                    {
                        Logger.Debug("[{0}] Zombie {1} out of range, turning inactive", zombie.Parent.id, ent);
                        removeZombie = true;

                        world.RemoveEntity(zombie.entityId, EnumRemoveEntityReason.Despawned);

                        zombie.entityId = -1;
                        zombie.currentZone = null;

                        TurnZombieInactive(zombie.Parent);
                    }
                    else
                    {
                        foreach (var zone in zones)
                        {
                            if (zone.numZombies + 1 < maxPerZone)
                            {
                                zone.numZombies++;
                                zombie.currentZone = zone;
                                // If the zombie is inside a player zone make sure we renew the life time.
                                zombie.lifeTime = worldTime;
                                break;
                            }
                        }
                    }
                }
            }

            return removeZombie;
        }

        // This function is called only from the main thread.
        // This functions checks about every active zombie if they are too far
        // away from the player if that is the case they will be despawned and
        // put back into the simulation at the current coordinates.
        // NOTE: A call must only be made from the main thread.
        void UpdateActiveZombies()
        {
            lock (_activeZombies)
            {
                for (int i = 0; i < _activeZombies.Count; i++)
                {
                    var zombie = _activeZombies[i];

                    bool removeZombie;
                    if (zombie.Active == null)
                    {
                        Logger.Warning($"Tried to update ");
                        removeZombie = true;
                    }
                    else
                    {
                        removeZombie = UpdateActiveZombie(zombie.Active);
                    }

                    if (removeZombie)
                    {
                        zombie.Deactivate();
                        _activeZombies.RemoveAt(i);
                        if (_activeZombies.Count == 0)
                            break;

                        i--;
                    }
                }
            }
        }
    }
}
