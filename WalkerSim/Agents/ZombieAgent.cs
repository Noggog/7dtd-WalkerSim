using System;
using System.Collections;
using UnityEngine;

namespace WalkerSim
{
    class ZombieAgent : IComparer, IEquatable<ZombieAgent>, IDisposable
    {
        public enum State
        {
            Idle,
            Wandering,
            Waiting,
            Investigating,
            Active,
        }
        
        public int id = -1;
        public int classId = -1;
        public State state = State.Idle;
        public Vector3 pos = new Vector3();
        public int health = -1;
        public DateTime ReachedLocationTime = DateTime.MinValue;

        public TimeSpan? RemainingWaitTime
        {
            get
            {
                if (state != State.Waiting) return null;
                return TimeSpan.FromSeconds(Config.Instance.InactiveWaitAtTargetTime) - (DateTime.Now - ReachedLocationTime);
            }
        }
        
        public ZombieInactiveAgent Inactive { get; }
        public ZombieActiveAgent? Active { get; private set; }

        public ZombieAgent()
        {
            Inactive = new ZombieInactiveAgent(this);
        }

        int IComparer.Compare(object a, object b)
        {
            return ((ZombieAgent)a).id - ((ZombieAgent)b).id;
        }

        public bool Equals(ZombieAgent other)
        {
            return id == other.id;
        }

        public ZombieActiveAgent MakeActive()
        {
            if (Active != null) throw new ArgumentException($"Tried to activate an already active zombie: {id}");
            Active = new ZombieActiveAgent(this);
            return Active;
        }

        public void Deactivate()
        {
            Active?.Dispose();
            Active = null;
        }

        public void Dispose()
        {
            Active?.Dispose();
        }
    }
}
