using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using System.IO;

namespace WalkerSim
{
    class ZombieSpawnRequest
    {
        public readonly ZombieAgent zombie;
        public readonly PlayerZone zone;

        public ZombieSpawnRequest(ZombieAgent zombie, PlayerZone zone)
        {
            this.zombie = zombie;
            this.zone = zone;
        }
    }

    public partial class Simulation
    {
        const int MaxZombieSpawnsPerTick = 2;
        const ulong MinZombieLifeTime = 60; // 1 in-game minutes.

        static int DayTimeMin = GamePrefs.GetInt(EnumGamePrefs.DayNightLength);
        static int MaxAliveZombies = GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies);
        static int MaxSpawnedZombies = MaxAliveZombies;

        static string ConfigFile = string.Format("{0}/WalkerSim.xml", API.ModPath);
        static string SimulationFile = string.Format("{0}/WalkerSim.bin", GameUtils.GetSaveGameDir());

        static ViewServer _server = new ViewServer();

        State _state = new State();

        Vector3i _worldMins = new Vector3i();
        Vector3i _worldMaxs = new Vector3i();

        DateTime _nextBroadcast = DateTime.Now;
        BiomeData _biomeData = new BiomeData();

        PRNG _prng = new PRNG(0);

        int _nextZombieId = 0;
        int _maxZombies = 0;
        double _accumulator = 0.0;

        DateTime _nextSave = DateTime.Now;

        BackgroundWorker _worker = new BackgroundWorker();
        bool _running = false;

        public Simulation()
        {
            Config.Instance.Load(ConfigFile);

            var world = GameManager.Instance.World;
            world.GetWorldExtent(out _worldMins, out _worldMaxs);

            float lenX = _worldMins.x < 0 ? _worldMaxs.x + Math.Abs(_worldMins.x) : _worldMaxs.x - Math.Abs(_worldMins.x);
            float lenY = _worldMins.z < 0 ? _worldMaxs.z + Math.Abs(_worldMins.z) : _worldMaxs.x - Math.Abs(_worldMins.z);

            float squareKm = (lenX / 1000.0f) * (lenY / 1000.0f);
            float populationSize = squareKm * Config.Instance.PopulationDensity;
            _maxZombies = (int)Math.Floor(populationSize);
            _state.WalkSpeedScale = Config.Instance.WalkSpeedScale;

            MaxSpawnedZombies = MaxAliveZombies - Mathf.RoundToInt(MaxAliveZombies * Config.Instance.ReservedSpawns);

            Logger.Info("Simulation File: {0}", SimulationFile);
            Logger.Info("World X: {0}, World Y: {1} -- {2}, {3}", lenX, lenY, _worldMins, _worldMaxs);
            Logger.Info("Day Time: {0}", DayTimeMin);
            Logger.Info("Max Offline Zombies: {0}", _maxZombies);
            Logger.Info("Max Spawned Zombies: {0}", MaxSpawnedZombies);

#if !DEBUG
            if (Config.Instance.EnableViewServer)
#endif
            {
                Logger.Info("Starting server...");

                _server.OnClientConnected += new ViewServer.OnClientConnectedDelegate(OnClientConnected);
                _state.OnChange += new State.OnChangeDelegate(OnStateChanged);

                if (_server.Start(Config.Instance.ViewServerPort))
                {
                    Logger.Info("ViewServer running at port {0}", Config.Instance.ViewServerPort);
                }
            }

            _biomeData.Init();
            _pois.BuildCache();
            _worldZones.BuildZones(_worldMins, _worldMaxs);

            _worker.WorkerSupportsCancellation = true;
            _worker.DoWork += BackgroundUpdate;
            
            Logger.Info("Initialized");
        }

        void OnClientConnected(ViewServer sender, ViewServer.Client cl)
        {
            SendStaticState(sender, cl);
        }

        void OnStateChanged()
        {
            SendState(_server, null);
        }

        public void SetTimeScale(float scale)
        {
            _state.Timescale = Mathf.Clamp(scale, 0.01f, 100.0f);
            _accumulator = 0;
        }

        public void SetWalkSpeedScale(float scale)
        {
            _state.WalkSpeedScale = Mathf.Clamp(scale, 0.01f, 100.0f);
        }

        public void Start()
        {
            if (_running)
            {
                Logger.Error("Simulation is already running");
                return;
            }

            Logger.Info("Starting worker..");

#if DEBUG
            if (!Config.Instance.Persistent || !Load())
#endif
            {
                Reset();
            }

            _running = true;
            _worker.RunWorkerAsync();
        }

        public void Stop()
        {
            if (!_running)
                return;

            Logger.Info("Stopping worker..");

            _worker.CancelAsync();
            _running = false;
        }

        public void AddPlayer(int entityId)
        {
            _playerZones.AddPlayer(entityId);
        }

        public void RemovePlayer(int entityId)
        {
            _playerZones.RemovePlayer(entityId);
        }

        public void Save()
        {
            if (!Config.Instance.Persistent)
                return;

            try
            {
                using (Stream stream = File.Open(SimulationFile, FileMode.Create))
                {
                    BinaryFormatter formatter = new BinaryFormatter();
                    lock (_inactiveZombies)
                    {
                        formatter.Serialize(stream, Config.Instance);

                        List<ZombieData> data = new List<ZombieData>();
                        foreach (var zombie in _inactiveZombies)
                        {
                            data.Add(new ZombieData
                            {
                                health = zombie.health,
                                x = zombie.pos.x,
                                y = zombie.pos.y,
                                z = zombie.pos.z,
                                targetX = zombie.Inactive.targetPos.x,
                                targetY = zombie.Inactive.targetPos.y,
                                targetZ = zombie.Inactive.targetPos.z,
                                target = zombie.Inactive.target is POIZone,
                            });
                        }
                        formatter.Serialize(stream, data);
                    }
                    Logger.Info("Saved simulation");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unable to save simulation");
                Log.Exception(ex);
            }
        }

        public void CheckAutoSave()
        {
            //Log.Out("[WalkerSim] CheckAutoSave");

            DateTime now = DateTime.Now;
            if (now < _nextSave)
                return;

            Save();
            _nextSave = now.AddMinutes(5);
        }

        public bool Load()
        {
            try
            {
                using (Stream stream = File.Open(SimulationFile, FileMode.Open))
                {
                    BinaryFormatter formatter = new();

                    var config = (Config)formatter.Deserialize(stream);
                    if (!config.Equals(Config.Instance))
                    {
                        Logger.Info("Configuration changed, not loading save.");
                        return false;
                    }

                    lock (_inactiveZombies)
                    {
                        var data = (List<ZombieData>)formatter.Deserialize(stream);
                        if (data.Count > 0)
                        {
                            _inactiveZombies.Clear();
                            foreach (var zombie in data)
                            {
                                var inactiveZombie = new ZombieAgent
                                {
                                    pos = new Vector3(zombie.x, zombie.y, zombie.z),
                                    health = zombie.health,
                                };
                                inactiveZombie.Inactive.target = zombie.target ? _pois.GetRandom(_prng) : null;
                                inactiveZombie.Inactive.targetPos = new Vector3(zombie.targetX, zombie.targetY, zombie.targetZ);
                                _inactiveZombies.Add(inactiveZombie);
                            }
                            Logger.Info("Loaded {0} inactive zombies", _inactiveZombies.Count);
                        }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public void Reset()
        {
            var world = GameManager.Instance.World;

            lock (_spawnQueue)
            {
                _spawnQueue.Clear();
            }

            lock (_inactiveZombies)
            {
                _inactiveZombies.Clear();
            }

            lock (_activeZombies)
            {
                _activeZombies.Clear();
            }

            // Cleanup all zombies.
            var ents = new List<Entity>(world.Entities.list);
            foreach (var ent in ents)
            {
                if (ent.entityType == EntityType.Zombie)
                {
                    world.RemoveEntity(ent.entityId, EnumRemoveEntityReason.Despawned);
                }
            }

            // Populate
            CreateInactiveRoaming();

            _nextSave = DateTime.Now.AddMinutes(5);
        }

        public void Update()
        {
            if (!_running)
                return;

            try
            {
                _state.Update();
                UpdatePlayerZones();
                UpdateActiveZombies();
                ProcessSpawnQueue();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
        }

        public void BackgroundUpdate(object sender, DoWorkEventArgs e)
        {
            Logger.Info("Worker Start");

            MicroStopwatch updateWatch = new MicroStopwatch();
            updateWatch.Start();

            MicroStopwatch frameWatch = new MicroStopwatch();

            double totalElapsed = 0.0;
            double dtAverage = 0.0;
            double nextReport = 10.0;
            float updateRate = 1.0f / (float)Config.Instance.UpdateInterval;

            var worker = (BackgroundWorker)sender;
            while (worker.CancellationPending == false)
            {
#if DEBUG
                bool isPaused = false;
#else
                bool isPaused = !(_playerZones.HasPlayers() || !Config.Instance.PauseWithoutPlayers);
#endif
                if (Config.Instance.PauseDuringBloodmon && _state.IsBloodMoon)
                    isPaused = true;

                double dt = updateWatch.ElapsedMicroseconds / 1000000.0;
                updateWatch.ResetAndRestart();

                totalElapsed += dt;

                if (!isPaused)
                {
                    dtAverage += dt;
                    dtAverage *= 0.5;

                    double dtScaled = dt;
                    dtScaled *= _state.Timescale;
                    _accumulator += dtScaled;
                }
                else
                {
                    dtAverage = 0.0;
                    lock (_worldEvents)
                    {
                        // Don't accumulate world events while paused.
                        _worldEvents.Clear();
                    }
                }

                _server.Update();

                if (_accumulator < updateRate)
                {
                    System.Threading.Thread.Sleep(isPaused ? 100 : 1);
                }
                else
                {
                    frameWatch.ResetAndRestart();

                    try
                    {
                        while (_accumulator >= updateRate)
                        {
                            var world = GameManager.Instance.World;
                            if (world == null)
                            {
                                // Work-around for client only support, some events are skipped like for when the player exits.
                                Logger.Info("World no longer exists, stopping simulation");
                                _worker.CancelAsync();
                                break;
                            }

                            _accumulator -= updateRate;

                            // Prevent long updates in case the timescale is cranked up.
                            if (frameWatch.ElapsedMilliseconds >= 66)
                                break;

                            UpdateInactiveZombies(updateRate);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Exception in worker");
                        Log.Exception(ex);
                    }
                }

                lock (_server)
                {
                    SendPlayerZones(_server, null);
                    SendInactiveZombieList(_server, null);
                    SendActiveZombieList(_server, null);
                }

                if (totalElapsed >= nextReport && !isPaused)
                {
                    double avgFps = 1 / dtAverage;
                    if (avgFps < (1.0f / updateRate))
                    {
                        Logger.Warning("Detected bad performance, FPS Average: {0}", avgFps);
                    }
                    nextReport = totalElapsed + 60.0;
                }
            }

            Logger.Info("Worker Finished");
            _running = false;
        }
    }
}
