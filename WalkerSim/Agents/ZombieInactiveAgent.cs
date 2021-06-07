using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WalkerSim
{
    class ZombieInactiveAgent
    {
        public ZombieAgent Parent { get; }
        
        const int MaxVisitedHistory = 5;

        public Vector3 targetPos = new();
        public IZone? target = null;
        public List<IZone> visitedZones = new();
        public float simulationTime = 0.0f;

        public ZombieInactiveAgent(ZombieAgent parent)
        {
            Parent = parent;
        }

        public bool ReachedTarget()
        {
            Vector3 a = new Vector3(Parent.pos.x, 0, Parent.pos.z);
            Vector3 b = new Vector3(targetPos.x, 0, targetPos.z);

            float dist = Vector3.Distance(a, b);
            if (dist <= 2.0f)
                return true;

            return false;
        }

        public void AddVisitedZone(IZone? zone)
        {
            if (zone == null)
                return;

            visitedZones.Add(zone);

            if (visitedZones.Count > MaxVisitedHistory)
                visitedZones.RemoveAt(0);
        }

        public bool HasVisitedZone(IZone zone)
        {
            return visitedZones.Contains(zone);
        }
    }
}