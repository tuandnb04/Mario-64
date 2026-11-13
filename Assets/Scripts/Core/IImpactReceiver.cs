using UnityEngine;

/// <summary>
/// Polymorphic contract for any object that reacts to thrown/exploding carriable impacts.
/// Eliminates concrete type checks in CarriableObject (Open/Closed Principle).
/// </summary>
public interface IImpactReceiver
{
    void OnImpact(Vector3 impactOrigin, float distanceSq);
}

