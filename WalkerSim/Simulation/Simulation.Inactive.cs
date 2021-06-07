using System;
using System.Collections.Generic;
using UnityEngine;

namespace WalkerSim
{
    partial class Simulation
    {
        List<ZombieAgent> _inactiveZombies = new();
        Dictionary<Vector2i, int> _zoneCounter = new();
        Queue<ZombieAgent> _inactiveQueue = new();

        void CreateInactiveRoaming()
        {
            int numCreated = 0;
            int maxZombies = _maxZombies;

            lock (_inactiveZombies)
            {
                while (_inactiveZombies.Count < maxZombies)
                {
                    CreateInactiveZombie(true);
                    numCreated++;
                }
            }

            if (numCreated > 0)
            {
                Logger.Info("{0} inactive roaming", numCreated);
            }
        }

        void ProcessWorldEvent(ZombieInactiveAgent zombie, WorldEvent? ev)
        {
            if (ev == null)
                return;

            var dist = Vector3.Distance(zombie.Parent.pos, ev.Pos);
            if (dist <= ev.Radius)
            {
                Vector3 soundDir = new Vector3();
                soundDir.x = _prng.Get(-1.0f, 1.0f);
                soundDir.z = _prng.Get(-1.0f, 1.0f);

                // Pick a random position within 75% of the radius.
                soundDir.Normalize();
                soundDir *= (dist * 0.75f);

                zombie.targetPos = ev.Pos + soundDir;
                zombie.target = _worldZones.FindByPos2D(zombie.targetPos);

                zombie.Parent.state = ZombieAgent.State.Investigating;
            }
        }

        private IZone? GetNextTarget(ZombieInactiveAgent zombie)
        {
            if (_prng.Chance(Config.Instance.POITravellerChance))
            {
                return GetNextPOI(zombie);
            }
            return _worldZones.GetRandom(_prng);
        }

        private Vector3 GetTargetPos(IZone target)
        {
            return target.GetRandomPos(_prng);
        }

        private Vector3 ClampPos(Vector3 pos)
        {
            pos.x = UnityEngine.Mathf.Clamp(pos.x, _worldMins.x, _worldMaxs.x);
            pos.y = UnityEngine.Mathf.Clamp(pos.y, _worldMins.y, _worldMaxs.y);
            pos.z = UnityEngine.Mathf.Clamp(pos.z, _worldMins.z, _worldMaxs.z);
            return pos;
        }

        private Vector3 WrapPos(Vector3 pos)
        {
            pos.x = ((pos.x - _worldMins.x) % (_worldMaxs.x - _worldMins.x)) + _worldMins.x;
            pos.y = ((pos.y - _worldMins.y) % (_worldMaxs.y - _worldMins.y)) + _worldMins.y;
            pos.z = ((pos.z - _worldMins.z) % (_worldMaxs.z - _worldMins.z)) + _worldMins.z;
            return pos;
        }

        private void UpdateTarget(ZombieInactiveAgent zombie)
        {
            switch (zombie.Parent.state)
            {
                case ZombieAgent.State.Investigating:
                case ZombieAgent.State.Wandering:
                case ZombieAgent.State.Active:
                {
                    // If we have an active target wait for arrival.
                    if (!zombie.ReachedTarget())
                        return;

                    zombie.AddVisitedZone(zombie.target);
                    zombie.Parent.state = ZombieAgent.State.Waiting;
                    zombie.Parent.ReachedLocationTime = DateTime.Now;
                    break;
                }
            }

            var remainingWaitTime = zombie.Parent.RemainingWaitTime;
            if (remainingWaitTime != null && remainingWaitTime.Value > TimeSpan.Zero)
            {
                return;
            }

            zombie.Parent.state = ZombieAgent.State.Wandering;

            if (_state.IsBloodMoon)
            {
                zombie.target = _playerZones.GetRandomClosest(zombie.Parent.pos, _prng, 200.0f, null);
                if (zombie.target == null)
                {
                    zombie.target = GetNextTarget(zombie);
                }
            }
            else
            {
                zombie.target = GetNextTarget(zombie);
            }

            if (zombie.target != null)
            {
                zombie.targetPos = GetTargetPos(zombie.target);
                zombie.Parent.state = ZombieAgent.State.Wandering;
            }
            else
            {
                Logger.Warning($"{zombie.Parent.id} had no target!");
            }
        }

        void UpdateApproachTarget(ZombieInactiveAgent zombie, float dt)
        {
#if false
            // Test investigation.
            if (zombie.state != ZombieAgent.State.Investigating)
                return;
#endif
            float speed = _state.ScaledZombieSpeed;
            speed *= dt;

            var distance = Vector3.Distance(zombie.Parent.pos, zombie.targetPos) * 0.75f;

            var t = (zombie.simulationTime + zombie.Parent.id) * 0.2f;
            var offset = new Vector3(Mathf.Cos(t), 0.0f, Mathf.Sin(t));
            offset *= distance;

            // Move towards target.
            zombie.Parent.pos = Vector3.MoveTowards(zombie.Parent.pos, zombie.targetPos + offset, speed);
        }

        void UpdateInactiveZombie(ZombieInactiveAgent zombie, float dt, WorldEvent? ev)
        {
            zombie.simulationTime += dt;

            ProcessWorldEvent(zombie, ev);
            UpdateTarget(zombie);
            UpdateApproachTarget(zombie, dt);
        }

        void UpdateInactiveZombies(float dt)
        {
            // Repopulate
            lock (_inactiveZombies)
            {
                lock (_inactiveQueue)
                {
                    while (_inactiveQueue.Count > 0)
                    {
                        var zombie = _inactiveQueue.Dequeue();
                        _inactiveZombies.Add(zombie);
                    }
                }
            }

            // Simulate
            int activatedZombies = 0;
            int maxUpdates = _maxZombies;
            int maxPerZone = MaxZombiesPerZone();

            WorldEvent? ev = null;
            lock (_worldEvents)
            {
                if (_worldEvents.Count > 0)
                {
                    ev = _worldEvents.Dequeue();
                }
            }

            for (int i = 0; ; i++)
            {
                lock (_inactiveZombies)
                {
                    if (i >= _inactiveZombies.Count)
                        break;

                    var world = GameManager.Instance.World;
                    if (world == null)
                    {
                        Logger.Info("World no longer exists, bailing");
                        break;
                    }

                    bool removeZombie = false;
                    bool activatedZombie = false;

                    ZombieAgent zombie = _inactiveZombies[i];

                    UpdateInactiveZombie(zombie.Inactive, dt, ev);

                    if (!CanSpawnActiveZombie())
                        continue;

                    List<PlayerZone> zones = _playerZones.FindAllByPos2D(zombie.pos);
                    if (zones.Count <= 0)
                        continue;

                    foreach (var zone in zones)
                    {
                        var player = world.GetEntity(zone.entityId) as EntityPlayer;

                        // Use players spawn border.
                        if (zone.IsInside2D(zombie.pos))
                        {
                            if (!zone.InsideSpawnArea2D(zombie.pos))
                            {
                                removeZombie = true;
                                continue;
                            }
                        }
                        else
                        {
                            continue;
                        }

                        if (zone.numZombies >= maxPerZone)
                        {
                            Logger.Debug("Zone {0} is full: {1} / {2}", zombie.pos, zone.numZombies, maxPerZone);
                            continue;
                        }

                        RequestActiveZombie(zombie, zone);
                        activatedZombie = true;
                        activatedZombies++;
                        break;
                    }

                    // Zombie inside one or more zones will be always removed.
                    if (activatedZombie)
                        removeZombie = true;

                    if (removeZombie)
                    {
                        zombie.Dispose();
                        _inactiveZombies.RemoveAt(i);
                        i--;

                        // If the zombie was not activated begin a new cycle.
                        if (!activatedZombie)
                        {
                            RespawnInactiveZombie(zombie);
                        }

                        // NOTE: This should never happen.
                        if (_inactiveZombies.Count == 0)
                        {
                            Logger.Error("Population is empty, this should not happen.");
                            break;
                        }
                    }
                }
            }

            if (activatedZombies > 0)
            {
                Logger.Debug("Activated {0} zombies", activatedZombies);
            }
        }

        ZombieAgent CreateInactiveZombie(bool initial)
        {
            ZombieAgent zombie = new ZombieAgent();
            zombie.id = _nextZombieId++;

            if (initial)
            {
                var poiChance = Config.Instance.POITravellerChance;

                var poi = _prng.Chance(poiChance) ? _pois.GetRandom(_prng) : null;
                if (poi != null)
                {
                    // To be not literally inside the POI we add a random radius.
                    var spawnRadius = 256.0f;
                    var randOffset = new Vector3(
                        _prng.Get(-spawnRadius, spawnRadius),
                        0.0f,
                        _prng.Get(-spawnRadius, spawnRadius));
                    zombie.pos = poi.GetRandomPos(_prng) + randOffset;
                    zombie.pos = WrapPos(zombie.pos);
                }
                else
                {
                    // Use a random world zone for the rest.
                    var zone = _worldZones.GetRandom(_prng);
                    if (zone != null)
                    {
                        zombie.pos = zone.GetRandomPos(_prng);
                    }
                    else
                    {
                        zombie.pos = GetRandomPos();
                    }
                }
            }
            else
            {
                // New zombies start at the border.
                zombie.pos = GetRandomBorderPoint();
            }

            zombie.state = ZombieAgent.State.Idle;

            _inactiveZombies.Add(zombie);

            return zombie;
        }

        void TurnZombieInactive(ZombieAgent zombie)
        {
            zombie.state = ZombieAgent.State.Idle;
            lock (_inactiveQueue)
            {
                _inactiveQueue.Enqueue(zombie);
            }
        }

        void RespawnInactiveZombie(ZombieAgent zombie)
        {
            zombie.pos = GetRandomBorderPoint();
            TurnZombieInactive(zombie);
        }
    }
}
