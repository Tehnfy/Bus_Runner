using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum RunState
{
    Intro,
    Running,
    Dead,
    Finished,
}

/// <summary>
/// Owns run state, the current checkpoint and respawning. One per level scene.
/// </summary>
public class RunManager : MonoBehaviour
{
    public static RunManager Instance { get; private set; }

    [Header("Refs")]
    [SerializeField] PlayerController player;
    [SerializeField] FinishSequence finishSequence;

    [Header("Death")]
    [Tooltip("Pause before the player is put back at the last checkpoint.")]
    [SerializeField] float respawnDelay = 0.6f;

    public RunState State { get; private set; } = RunState.Intro;

    /// <summary>Where a death sends the player back to.</summary>
    public Vector3 SpawnPoint { get; private set; }

    readonly List<Checkpoint> reached = new List<Checkpoint>();
    Checkpoint lastCheckpoint;
    float runStartTime;
    float runEndTime = -1f;

    void Awake()
    {
        Instance = this;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (finishSequence == null) finishSequence = FindFirstObjectByType<FinishSequence>();
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

    /// <summary>Called by FinishLine when the player crosses it.</summary>
    public void Finish()
    {
        if (State != RunState.Running) return;
        State = RunState.Finished;
        runEndTime = Time.time;
        Debug.Log($"[RunManager] Finished at x={player.transform.position.x:F1} after {RunTime:F1}s");

        // Control is not taken away here — the runner has to keep running while the
        // camera pulls back. FinishSequence stops them once the shot is over.
        if (finishSequence != null) finishSequence.Play();
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

    /// <summary>Seconds since the run began, frozen at the finish line — used to sanity-check the 90-120s target.</summary>
    public float RunTime =>
        State == RunState.Intro ? 0f : (runEndTime >= 0f ? runEndTime : Time.time) - runStartTime;
}
