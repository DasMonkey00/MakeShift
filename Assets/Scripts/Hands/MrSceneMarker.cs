using UnityEngine;

/// <summary>
/// Marks a scene as passthrough / mixed reality. RoboSpawnRecenter then leaves the player where they physically are
/// (in MR the real room is tied to the tracking space), instead of recentering onto the spawn point.
/// The Passthrough template's builder adds this automatically.
/// </summary>
public class MrSceneMarker : MonoBehaviour { }
