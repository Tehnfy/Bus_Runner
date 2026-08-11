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
    [Tooltip("Pause before the player is put back at the last checkpoint. Long enough for the ragdoll " +
             "to actually land and settle — at the old 0.6s the body was still in the air when it was " +
             "snatched back to the checkpoint.")]
    [SerializeField] float respawnDelay = 1.5f;

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
        // Only ever leaves the intro. It used to refuse just the Running state, which meant a call
        // arriving while the runner was dead would hand control back mid-collapse — with the
        // CharacterController switched off under the ragdoll, that is a Move on a disabled controller
        // every frame until the respawn cleans it up.
        if (State != RunState.Intro) return;
        State = RunState.Running;
        runStartTime = Time.time;
        if (player != null) player.EnableControl(true);
    }

    /// <summary>Called by Checkpoint when the player passes one.</summary>
    public void ReachCheckpoint(Checkpoint checkpoint)
    {
        if (reached.Contains(checkpoint)) return;
        reached.Add(checkpoint);

        // Light it. Every checkpoint the player has passed stays lit, including ones that did not move
        // the spawn forward, because what the lamp reports is "you have been here".
        checkpoint.MarkReached();

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

    /// <summary>
    /// Called by PlayerController on a fatal contact. The normal points out of whatever was hit, and
    /// is passed straight through to the ragdoll as the direction to throw the body. Defaulted so a
    /// death with no surface behind it — a script, a future pit — still works.
    /// </summary>
    public void Kill(Vector3 impactNormal = default)
    {
        if (State != RunState.Running) return;
        State = RunState.Dead;
        Debug.Log($"[RunManager] Died at x={player.transform.position.x:F1}, respawning at {SpawnPoint}");
        if (player != null) player.Die(impactNormal);
        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        if (player != null)
        {
            // Off physics, then moved, then given back the controls — see PlayerController.Revive
            // for why that order is not interchangeable.
            player.Revive();
            player.Teleport(SpawnPoint);
            player.EnableControl(true);
        }
        State = RunState.Running;
    }

    /// <summary>Seconds since the run began, frozen at the finish line — used to sanity-check the 90-120s target.</summary>
    public float RunTime =>
        State == RunState.Intro ? 0f : (runEndTime >= 0f ? runEndTime : Time.time) - runStartTime;
}
