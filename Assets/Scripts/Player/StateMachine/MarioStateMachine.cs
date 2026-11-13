/// <summary>
/// Lightweight State Machine for managing Mario's active state transitions cleanly.
/// </summary>
public class MarioStateMachine
{
    public IMarioState CurrentState { get; private set; }

    public void ChangeState(IMarioState nextState)
    {
        if (nextState == null || nextState == CurrentState) return;
        CurrentState?.Exit();
        CurrentState = nextState;
        CurrentState.Enter();
    }

    public void Tick(float deltaTime) => CurrentState?.Tick(deltaTime);
    public void FixedTick(float fixedDeltaTime) => CurrentState?.FixedTick(fixedDeltaTime);
}

