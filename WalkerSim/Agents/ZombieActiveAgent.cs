using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using UnityEngine;

namespace WalkerSim
{
    class ZombieActiveAgent : IDisposable
    {
        private static readonly PRNG _rng = new();
        
        private readonly IDisposable _disposable;
        public ZombieAgent Parent { get; }
        public int entityId = -1;
        public ulong lifeTime = 0;
        public IZone? currentZone = null;
        public Vector3? intendedGoal;
        public bool reachedInitialTarget;
        

        private BehaviorSubject<State> _state = new(State.InitialWalkIn);
        public State CurrentState => _state.Value;

        public enum State
        {
            InitialWalkIn,
            Distracted,
            Idle,
            WalkOut,
            Staying,
            WantsDespawn,
        }

        public ZombieActiveAgent(ZombieAgent parent)
        {
            Parent = parent;
            _disposable = _state
                .ObserveOn(Scheduler.Default)
                .Select(state =>
                {
                    return state switch
                    {
                        State.InitialWalkIn => InitialInvestigationCheck(),
                        State.Idle => Idle(),
                        State.WalkOut => WalkOut(),
                        State.Distracted => Distracted(),
                        State.Staying => Staying(),
                        State.WantsDespawn => WantsDespawn(),
                        _ => throw new NotImplementedException(),
                    };
                })
                .Switch()
                .Subscribe();
        }

        private IObservable<Unit> InitialInvestigationCheck()
        {
            return Observable.Interval(TimeSpan.FromSeconds(5))
                .ObserveOn(MyScheduler.Instance)
                .Select(_ =>
                {
                    var world = GameManager.Instance.World;
                    if (world.GetEntity(entityId) is not EntityZombie entityZombie) return Unit.Default;

                    if (intendedGoal == null) return Unit.Default;
                    if (CheckIfDistracted(entityZombie, intendedGoal.Value)) return Unit.Default;

                    // If zombie reached its target, send it somewhere
                    var distanceToTarget = Vector3.Distance(entityZombie.position, intendedGoal.Value);
                    if (distanceToTarget <= 25.0f)
                    {
                        Logger.Debug("[{0}] Reached its target at {1}.", Parent.id, entityZombie.InvestigatePosition);
                        entityZombie.ClearInvestigatePosition();
                        reachedInitialTarget = true;
                        _state.OnNext(State.Idle);
                    }
                    else
                    {
                        Logger.Debug("[{0}] [{1}] was {2} away from its target.", Parent.id, _state.Value, distanceToTarget);
                    }

                    return Unit.Default;
                });
        }

        private IObservable<Unit> Idle()
        {
            TimeSpan toWait;
            lock (_rng)
            {
                toWait = TimeSpan.FromSeconds(_rng.Get(Config.Instance.MinIdleSeconds, Config.Instance.MaxIdleSeconds));
            }
            Logger.Debug("[{0}] [{1}] waiting {2} seconds.", Parent.id, _state.Value, toWait.TotalSeconds);

            return Observable.Interval(toWait)
                .Take(1)
                .Select(_ =>
                {
                    _state.OnNext(State.WalkOut);
                    return Unit.Default;
                });
        }

        private async Task SetWalkAwayVector(EntityZombie entityZombie, World world)
        {
            var normalized = Parent.Inactive.targetPos - entityZombie.position;
            normalized.Normalize();
            Vector3 spawnPos = new Vector3();
            Logger.Debug("[{0}] [{1}] Finding walk away vector.  Zombie is at {2}.  Normalized direction is {3}", Parent.id, _state.Value, entityZombie.position, normalized);

            const int Interval = 5;
            const int Max = 60;

            var couldFindAnything = false;
            Vector3? targetPos = null;
            var distance = Interval;
            
            // Dialing up to find max target
            while (distance <= Max)
            {
                var vec = entityZombie.position + (normalized * distance);
                int height = world.GetTerrainHeight((int)vec.x, (int)vec.z);
                if (height == 0) break;
                couldFindAnything = true;
                spawnPos.x = vec.x;
                spawnPos.y = height + 1.0f;
                spawnPos.z = vec.z;
                if (Simulation.CanZombieSpawnAt(spawnPos))
                {
                    targetPos = spawnPos;
                }

                distance += Interval;
            }

            // Couldn't find anything
            if (targetPos == null)
            {
                _state.OnNext(couldFindAnything ? State.Staying : State.WantsDespawn);
                return;
            }

            // Set investigation and dial back until it sticks
            while (distance > 0)
            {
                intendedGoal = targetPos;
                entityZombie.SetInvestigatePosition(
                    intendedGoal.Value,
                    6000,
                    false);

                // Wait until next game frame
                await Task.Delay(400);

                if (entityZombie.HasInvestigatePosition)
                {
#if DEBUG
                    var distanceToTarget = Vector3.Distance(entityZombie.position, intendedGoal.Value);
                    Logger.Debug($"[{Parent.id}] [{_state.Value}] walk away goal set to {distanceToTarget} away from its target");
#endif
                    // Investigation position stuck
                    return;
                }
                
                // Try some more
                distance -= Interval;
            }
            
            Logger.Debug("[{0}] [{1}] Could not find anywhere to walk away to.  Despawning", Parent.id, _state.Value);
            _state.OnNext(State.WantsDespawn);
        }

        private IObservable<Unit> WalkOut()
        {
            Logger.Debug("[{0}] [{1}] walking away.", Parent.id, _state.Value);
            return Observable.Return(Unit.Default) 
                .ObserveOn(MyScheduler.Instance)
                .SelectTask(async () =>
                {
                    var world = GameManager.Instance.World;
                    if (world.GetEntity(entityId) is EntityZombie entityZombie)
                    {
                        await SetWalkAwayVector(entityZombie, world);
                    }
                })
                .Concat(Observable.Interval(TimeSpan.FromSeconds(5))
                    .ObserveOn(MyScheduler.Instance)
                    .SelectTask(async () =>
                    {
                        var world = GameManager.Instance.World;
                        if (world.GetEntity(entityId) is EntityZombie entityZombie)
                        {
                            if (intendedGoal == null) return;
#if DEBUG
                            var distanceToTarget = Vector3.Distance(entityZombie.position, intendedGoal.Value);
                            Logger.Debug($"[{Parent.id}] [{_state.Value}] was {distanceToTarget} away from its target");
                            Logger.Debug($"[{Parent.id}] [{_state.Value}] has investigation target? {entityZombie.HasInvestigatePosition}.  Investigation target: {entityZombie.InvestigatePosition}");
#endif
                            if (CheckIfDistracted(entityZombie, intendedGoal.Value)) return;

                            if (distanceToTarget <= 20f)
                            {
                                Logger.Debug($"[{Parent.id}] [{_state.Value}] updating walkaway target");
                                await SetWalkAwayVector(entityZombie, world);
                            }
                        }
                    }));
        }

        private IObservable<Unit> Distracted()
        {
            return Observable.Interval(TimeSpan.FromSeconds(5))
                .Select(_ =>
                {
                    var world = GameManager.Instance.World;
                    if (world.GetEntity(entityId) is not EntityZombie entityZombie) return Unit.Default;
                    if (intendedGoal == null) return Unit.Default;

                    // If no longer investigating something else
                    if (!entityZombie.HasInvestigatePosition)
                    {
                        Logger.Debug("[{0}] [{1}] no longer distracted.", Parent.id, _state.Value);
                        // Try to walk to where we had wanted to go
                        entityZombie.SetInvestigatePosition(
                            intendedGoal.Value,
                            6000,
                            false);
                        _state.OnNext(reachedInitialTarget ? State.WalkOut : State.InitialWalkIn);
                    }
                    
                    return Unit.Default;
                });
        }

        private IObservable<Unit> Staying()
        {
            Logger.Debug("[{0}] [{1}] Staying.", Parent.id, _state.Value);
            return Observable.Return(Unit.Default);
        }

        private IObservable<Unit> WantsDespawn()
        {
            Logger.Debug("[{0}] [{1}] Wants despawn.", Parent.id, _state.Value);
            return Observable.Return(Unit.Default);
        }

        private bool CheckIfDistracted(EntityZombie entityZombie, Vector3 intendedGoal)
        {
            if (intendedGoal != entityZombie.InvestigatePosition)
            {
                Logger.Debug("[{0}] [{1}] was distracted.  Goal was {2} but investigate target was {3}", Parent.id, _state.Value, intendedGoal, entityZombie.InvestigatePosition);
                _state.OnNext(State.Distracted);
                return true;
            }

            return false;
        }
        
        public void Dispose()
        {
            _disposable.Dispose();
        }
    }
}