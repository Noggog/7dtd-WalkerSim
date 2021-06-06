using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

namespace WalkerSim
{
    public class API : IModApi
    {
        public static string ModPath = Path.Combine(Directory.GetCurrentDirectory(), "Mods", "WalkerSim");

        public static int MaxPlayers = GamePrefs.GetInt(EnumGamePrefs.ServerMaxPlayerCount);

        public static Simulation _sim = null!;
        static DateTime _nextOutputTime = DateTime.Now;
        static MicroStopwatch _stopWatch = new MicroStopwatch();
        public API()
        {
        }

        public void InitMod()
        {
            ModEvents.GameStartDone.RegisterHandler(GameStartDone);
            ModEvents.GameUpdate.RegisterHandler(GameUpdate);
            ModEvents.GameShutdown.RegisterHandler(GameShutdown);
            ModEvents.PlayerSpawnedInWorld.RegisterHandler(PlayerSpawnedInWorld);
            ModEvents.PlayerDisconnected.RegisterHandler(PlayerDisconnected);

            Hooks.Install();
        }

        static void GameStartDone()
        {
            Logger.Info("GameStartDone");
            _stopWatch.ResetAndRestart();

            _sim = new Simulation();
            _sim.Start();
        }

        static void GameUpdate()
        {
            try
            {
                float dt = (float)((double)_stopWatch.ElapsedMicroseconds / 1000000.0);
                _stopWatch.ResetAndRestart();

                MyScheduler.Instance.Execute();
                
                if (_sim != null)
                {
                    _sim.Update();
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error in API.GameUpdate: {0}.", e.Message);
            }
        }

        static void GameShutdown()
        {
            Logger.Info("GameShutdown");
            try
            {
                if (_sim != null)
                {
                    _sim.Stop();
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error in API.GameShutdown: {0}.", e.Message);
            }
        }

        // Helper function for single player games where _cInfo is null.
        static int GetPlayerEntityId(ClientInfo _cInfo)
        {
            if (_cInfo != null)
                return _cInfo.entityId;

            // On a local host this is set to null, grab id from player list.
            var world = GameManager.Instance.World;
            var player = world.Players.list[0];

            return player.entityId;
        }

        static void PlayerSpawnedInWorld(ClientInfo _cInfo, RespawnType _respawnReason, Vector3i _pos)
        {
            try
            {
                Logger.Debug("PlayerSpawnedInWorld \"{0}\", \"{1}\", \"{2}\"", _cInfo, _respawnReason, _pos);
                if (_sim != null)
                {
                    int entityId = GetPlayerEntityId(_cInfo);
                    switch (_respawnReason)
                    {
                        case RespawnType.NewGame:
                        case RespawnType.LoadedGame:
                        case RespawnType.EnterMultiplayer:
                        case RespawnType.JoinMultiplayer:
                            _sim.AddPlayer(entityId);
                            break;

                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error("Error in API.PlayerSpawnedInWorld: {0}.", e.Message);
            }
        }

        static void PlayerDisconnected(ClientInfo _cInfo, bool _bShutdown)
        {
            try
            {
                Logger.Debug("PlayerDisconnected \"{0}\", \"{1}\"", _cInfo, _bShutdown);
                int entityId = GetPlayerEntityId(_cInfo);
                _sim.RemovePlayer(entityId);
            }
            catch (Exception e)
            {
                Logger.Error("Error in API.PlayerDisconnected: {0}.", e.Message);
            }
        }
    }
}
