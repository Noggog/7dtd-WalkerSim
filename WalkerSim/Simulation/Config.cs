using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace WalkerSim
{
    [Serializable]
    public class Config : IEquatable<Config>
    {
        public static Config Instance = new Config();

        public int UpdateInterval { get; private set; } = 60;
        public bool PauseWithoutPlayers { get; private set; } = true;
        public int SpinupTicks { get; private set; } = 10000;
        public bool Persistent { get; private set; } = true;
        public int WorldZoneDivider { get; private set; } = 32;
        public float POITravellerChance { get; private set; } = 0.75f;
        public int PopulationDensity { get; private set; } = 40;
        public bool EnableViewServer { get; private set; }
        public int ViewServerPort { get; private set; } = 13632;
        public float WalkSpeedScale { get; private set; } = 1.0f;
        public float ReservedSpawns { get; private set; } = 0.5f;
        public bool PauseDuringBloodmon { get; private set; } = false;
        public int MinIdleSeconds { get; private set; } = 5;
        public int MaxIdleSeconds { get; private set; } = 25;
        public int InactiveWaitAtTargetTime { get; private set; } = 900;

        public Dictionary<string, float> SoundDistance { get; private set; } = new();

        public Config()
        {
#if DEBUG
            EnableViewServer = true;
#endif
        }

        public bool Equals(Config other)
        {
            // NOTE: Only simulation relevant fields should be tested.
            if (UpdateInterval != other.UpdateInterval)
                return false;
            if (Persistent != other.Persistent)
                return false;
            if (WorldZoneDivider != other.WorldZoneDivider)
                return false;
            if (POITravellerChance != other.POITravellerChance)
                return false;
            if (PopulationDensity != other.PopulationDensity)
                return false;
            if (WalkSpeedScale != other.WalkSpeedScale)
                return false;
            if (SoundDistance != other.SoundDistance)
                return false;
            if (ReservedSpawns != other.ReservedSpawns)
                return false;
            if (PauseDuringBloodmon != other.PauseDuringBloodmon)
                return false;
            if (MinIdleSeconds != other.MinIdleSeconds)
                return false;
            if (MaxIdleSeconds != other.MaxIdleSeconds)
                return false;
            if (InactiveWaitAtTargetTime != other.InactiveWaitAtTargetTime)
                return false;
            return true;
        }

        public bool Load(string configFile)
        {
            try
            {
                Logger.Info("Loading configuration...");

                XmlDocument doc = new XmlDocument();
                doc.Load(configFile);

                XmlNode nodeConfig = doc.DocumentElement;
                if (nodeConfig == null || nodeConfig.Name != "WalkerSim")
                {
                    Logger.Error("Invalid xml configuration format, unable to load config.");
                    return false;
                }
                foreach (XmlNode node in nodeConfig.ChildNodes)
                {
                    if (node.Name == "#comment")
                        continue;
                    ProcessNode(node);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Unable to load configuration: {0}", configFile);
                Log.Exception(ex);
                return false;
            }
            return true;
        }

        bool ToBool(string val)
        {
            var lower = val.ToLower();
            if (lower == "false" || lower == "0")
                return false;
            else if (lower == "true" || lower == "1")
                return true;
            Logger.Error("Invalid configuration parameter for bool: {0}", val);
            return false;
        }

        private void ProcessNode(XmlNode node)
        {
            try
            {
                switch (node.Name)
                {
                    case "UpdateInterval":
                        UpdateInterval = int.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "UpdateInterval", UpdateInterval);
                        break;
                    case "PauseWithoutPlayers":
                        PauseWithoutPlayers = ToBool(node.InnerText);
                        Logger.Info("{0} = {1}", "PauseWithoutPlayers", PauseWithoutPlayers);
                        break;
                    case "PauseDuringBloodmon":
                        PauseDuringBloodmon = ToBool(node.InnerText);
                        Logger.Info("{0} = {1}", "PauseDuringBloodmon", PauseDuringBloodmon);
                        break;
                    case "SpinupTicks":
                        SpinupTicks = int.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "SpinupTicks", SpinupTicks);
                        break;
                    case "Persistent":
                        Persistent = ToBool(node.InnerText);
                        Logger.Info("{0} = {1}", "Persistent", Persistent);
                        break;
                    case "WorldZoneDivider":
                        WorldZoneDivider = int.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "WorldZoneDivider", WorldZoneDivider);
                        break;
                    case "POITravellerChance":
                        POITravellerChance = MathUtils.Clamp(float.Parse(node.InnerText), 0.0f, 1.0f);
                        Logger.Info("{0} = {1}", "POITravellerChance", POITravellerChance);
                        break;
                    case "PopulationDensity":
                        PopulationDensity = int.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "PopulationDensity", PopulationDensity);
                        break;
                    case "WalkSpeedScale":
                        WalkSpeedScale = float.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "WalkSpeedScale", WalkSpeedScale);
                        break;
                    case "ReservedSpawnSlots":
                        ReservedSpawns = MathUtils.Clamp(float.Parse(node.InnerText), 0.0f, 1.0f);
                        Logger.Info("{0} = {1}", "ReservedSpawns", ReservedSpawns);
                        break;
                    case "SoundInfo":
                        ProcessSoundInfo(node);
                        break;
#if !DEBUG
                    case "ViewServer":
                        EnableViewServer = ToBool(node.InnerText);
                        Logger.Info("{0} = {1}", "ViewServer", EnableViewServer);
                        break;
#endif
                    case "ViewServerPort":
                        ViewServerPort = int.Parse(node.InnerText);
                        Logger.Info("{0} = {1}", "ViewServerPort", ViewServerPort);
                        break;
                    case "MinIdleSeconds":
                        MinIdleSeconds = int.Parse(node.InnerText);
                        break;
                    case "MaxIdleSeconds":
                        MaxIdleSeconds = int.Parse(node.InnerText);
                        break;
                    case "InactiveWaitAtTargetTime":
                        InactiveWaitAtTargetTime = int.Parse(node.InnerText);
                        break;
                }
            }
            catch (Exception)
            {
                Logger.Error("Invalid configuration for {0}", node.Name);
            }
        }

        void ProcessSoundInfo(XmlNode node)
        {
            try
            {
                foreach (XmlNode entry in node.ChildNodes)
                {
                    if (entry.Name != "Sound")
                        continue;

                    var audioName = entry.Attributes.GetNamedItem("Source");
                    if (audioName == null)
                        continue;

                    var distance = entry.Attributes.GetNamedItem("Distance");
                    if (distance == null)
                        continue;
                    
                    if (!float.TryParse(distance.Value, out var dist))
                        continue;

                    SoundDistance[audioName.Value] = dist;
                    Logger.Info("Sound: {0}, Distance {1}", audioName.Value, dist);
                }
            }
            catch (Exception)
            {
                Logger.Error("Invalid configuration for {0}", node.Name);
            }
        }
    }
}
