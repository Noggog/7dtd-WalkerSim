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
        public Vector3 intendedGoal;
        public bool reachedInitialTarget;

        private BehaviorSubject<State> _state = new(State.InitialWalkIn);

        enum State
        {
            InitialWalkIn,
            Distracted,
            Idle,
            WalkOut,
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

                    if (CheckIfDistracted(entityZombie)) return Unit.Default;

                    // If zombie reached its target, send it somewhere
                    var distanceToTarget = Vector3.Distance(entityZombie.position, entityZombie.InvestigatePosition);
                    if (distanceToTarget <= 20.0f)
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

        private IObservable<Unit> WalkOut()
        {
            Logger.Debug("[{0}] [{1}] walking away.", Parent.id, _state.Value);
            return Observable.Return(Unit.Default) 
                .ObserveOn(MyScheduler.Instance)
                .Select(_ =>
                {
                    var world = GameManager.Instance.World;
                    if (world.GetEntity(entityId) is EntityZombie entityZombie)
                    {
                        Logger.Debug("[{0}] [{1}] walking away.", Parent.id, _state.Value);
                        intendedGoal = Parent.Inactive.targetPos;
                        entityZombie.SetInvestigatePosition(
                            intendedGoal,
                            6000,
                            false);
                        Logger.Debug("[{0}] [{1}] has investigation target? {2}.  Investigation target: {3}", Parent.id, _state.Value, entityZombie.HasInvestigatePosition, entityZombie.InvestigatePosition);
                    }
                    
                    return Unit.Default;
                })
                .Concat(Observable.Interval(TimeSpan.FromSeconds(5))
                    .ObserveOn(MyScheduler.Instance)
                    .Select(_ =>
                    {
                        var world = GameManager.Instance.World;
                        if (world.GetEntity(entityId) is EntityZombie entityZombie)
                        {
#if DEBUG
                            var distanceToTarget = Vector3.Distance(entityZombie.position, entityZombie.InvestigatePosition);
                            Logger.Debug("[{0}] [{1}] was {2} away from its target", Parent.id, _state.Value, distanceToTarget);
#endif
                            CheckIfDistracted(entityZombie);
                        }

                        return Unit.Default;
                    }));
        }

        private IObservable<Unit> Distracted()
        {
            return Observable.Interval(TimeSpan.FromSeconds(5))
                .Select(_ =>
                {
                    var world = GameManager.Instance.World;
                    if (world.GetEntity(entityId) is not EntityZombie entityZombie) return Unit.Default;

                    // If no longer investigating something else
                    if (!entityZombie.HasInvestigatePosition)
                    {
                        Logger.Debug("[{0}] [{1}] no longer distracted.", Parent.id, _state.Value);
                        // Try to walk to where we had wanted to go
                        entityZombie.SetInvestigatePosition(
                            intendedGoal,
                            6000,
                            false);
                        _state.OnNext(reachedInitialTarget ? State.WalkOut : State.InitialWalkIn);
                    }
                    
                    return Unit.Default;
                });
        }

        private bool CheckIfDistracted(EntityZombie entityZombie)
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