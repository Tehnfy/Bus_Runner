using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RunState
{
    Intro,
    Running,
    Dead,
}

/// <summary>
/// Owns run state, the current checkpoint and respawning. One per level scene.
/// </summary>
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    [Header("Refs")]
    [SerializeField] PlayerController player;

    [Header("Death")]
    [Tooltip("Pause before the player is put back at the last checkpoint.")]
    [SerializeField] float respawnDelay = 0.6f;

    public RunState State { get; private set; } = RunState.Intro;

    /// <summary>Where a death sends the player back to.</summary>
    public Vector3 SpawnPoint { get; private set; }

    readonly List<Checkpoint> reached = new List<Checkpoint>();
    Checkpoint lastCheckpoint;
    float runStartTime;

    void Awake()
    {
        Instance = this;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    void Start()
    {
        SpawnPoint = player != null ? player.transform.position : Vector3.zero;

        // No intro in the scene means nobody would ever call BeginRun, so start
        // straight away rather than sitting on a frozen player.
        if (FindFirstObjectByType<IntroSequence>() == null) BeginRun();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by IntroSequence once the camera has landed on the runner.</summary>
    public void BeginRun()
    {
        if (State == RunState.Running) return;
        State = RunState.Running;
        runStartTime = Time.time;
        if (player != null) player.EnableControl(true);
    }

    /// <summary>Called by Checkpoint when the player passes one.</summary>
    public void ReachCheckpoint(Checkpoint checkpoint)
    {
        if (reached.Contains(checkpoint)) return;
        reached.Add(checkpoint);

        // Only move the spawn forward, never back — order of triggers is not guaranteed.
        if (lastCheckpoint == null || checkpoint.transform.position.x > lastCheckpoint.transform.position.x)
        {
            lastCheckpoint = checkpoint;
            SpawnPoint = checkpoint.SpawnPosition;
            Debug.Log($"[RunManager] Checkpoint '{checkpoint.name}' at x={checkpoint.transform.position.x:F1} — spawn now {SpawnPoint}");
        }
    }

    /// <summary>Called by Obstacle on contact.</summary>
    public void Kill()
    {
        if (State != RunState.Running) return;
        State = RunState.Dead;
        Debug.Log($"[RunManager] Died at x={player.transform.position.x:F1}, respawning at {SpawnPoint}");
        if (player != null) player.EnableControl(false);
        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (player != null)
        {
            player.Teleport(SpawnPoint);
            player.EnableControl(true);
        }
        State = RunState.Running;
    }

    /// <summary>Seconds since the run began — used to sanity-check the 90-120s target.</summary>
    public float RunTime => State == RunState.Intro ? 0f : Time.time - runStartTime;
}
