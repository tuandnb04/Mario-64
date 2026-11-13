/// <summary>
/// Contract for Mario movement/action states in the State Machine Pattern.
/// </summary>
public interface IMarioState
{
    void Enter();
    void Exit();
    void Tick(float deltaTime);
    void FixedTick(float fixedDeltaTime);
}

