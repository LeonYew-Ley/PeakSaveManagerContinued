using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

namespace PEAKSaveManager;

public sealed partial class SaveManagerPlugin
{
    private readonly HashSet<Luggage> restoredLuggage = new HashSet<Luggage>();
    private readonly LuggageRevisions receivedLuggageRevisions = new LuggageRevisions();
    private long luggageRevision;
    private static readonly FieldInfo SpawnHistoryField = AccessTools.Field(typeof(Peak.SpawnedItemTracker), "_historyFromSave");
    private static readonly FieldInfo SpawnedItemsField = AccessTools.Field(typeof(Peak.SpawnedItemTracker), "_spawnedItems");
    private static readonly MethodInfo SetSpawnHistoryMethod = AccessTools.PropertySetter(typeof(Peak.SpawnedItemTracker), "HasSpawnHistory");

    private sealed class LuggageBatch
    {
        public string scene;
        public long revision;
        public List<LuggageRestoreState> states = new List<LuggageRestoreState>();
    }

    private IEnumerator SyncLuggageClientsRoutine(string kind, string payload)
    {
        if (!PhotonNetwork.InRoom) yield break;
        if (!PhotonNetwork.IsMasterClient) throw new InvalidOperationException("Only the host can restore luggage.");
        List<string> requests = new List<string>();
        int expiresAt = unchecked(PhotonNetwork.ServerTimestamp + PlayerLoadRequestLifetimeMs);
        float deadline = Time.realtimeSinceStartup + (kind == "world-probe" ? 5f : 15f);
        try
        {
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerListOthers)
            {
                string request = Guid.NewGuid().ToString("N");
                ownerLoadReplies.Add(request, new OwnerLoadReply { actor = player.ActorNumber, viewId = 0 });
                requests.Add(request);
                SendPlayerLoadEvent(player.ActorNumber, kind, request, 0, expiresAt, payload);
            }
            while (true)
            {
                bool completed = true;
                foreach (string request in requests)
                {
                    OwnerLoadReply reply = ownerLoadReplies[request];
                    if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient
                        || PhotonNetwork.CurrentRoom.GetPlayer(reply.actor) == null)
                        throw new InvalidOperationException("A player left or the host changed during luggage restoration.");
                    if (reply.completed && !string.IsNullOrEmpty(reply.error))
                        throw new InvalidOperationException("A client could not restore luggage: " + reply.error);
                    completed &= reply.completed;
                }
                if (completed) yield break;
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException(kind == "world-probe"
                        ? "Every player in the room needs this save-manager version for luggage synchronization."
                        : "A client did not confirm luggage restoration.");
                yield return null;
            }
        }
        finally
        {
            foreach (string request in requests) ownerLoadReplies.Remove(request);
        }
    }

    private void HandleLuggageRequest(string kind, string payload, int actor, string request, int expiresAt)
    {
        try
        {
            if (kind == "world-probe")
            {
                ValidateLuggageApi();
                SendPlayerLoadEvent(actor, "reply", request, 0, expiresAt, "");
                return;
            }
            LuggageBatch batch = JsonConvert.DeserializeObject<LuggageBatch>(payload);
            if (batch?.states == null || batch.states.Count > 1024 || batch.revision <= 0
                || batch.scene != SceneManager.GetActiveScene().name)
                throw new InvalidOperationException("Luggage batch belongs to a different or unavailable scene.");
            HashSet<int> views = new HashSet<int>();
            foreach (LuggageRestoreState state in batch.states)
                if (state == null || state.viewId <= 0 || !views.Add(state.viewId) || !LuggageRestoration.IsFinite(state.position))
                    throw new InvalidOperationException("Invalid or duplicate luggage identity.");
            receivedLuggageRevisions.Receive(actor, batch.scene, batch.revision, batch.states);
            StartCoroutine(ReceiveLuggageRoutine(batch, actor, request, expiresAt));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Luggage synchronization failed: " + ex.Message);
            SendPlayerLoadEvent(actor, "reply", request, 0, expiresAt, ex.Message);
        }
    }

    private IEnumerator ReceiveLuggageRoutine(LuggageBatch batch, int actor, string request, int expiresAt)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        string error = null;
        List<Luggage> targets = new List<Luggage>();
        while (true)
        {
            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.MasterClient?.ActorNumber != actor
                    || unchecked(expiresAt - PhotonNetwork.ServerTimestamp) <= 0
                    || batch.scene != SceneManager.GetActiveScene().name)
                    throw new InvalidOperationException("Luggage restoration expired or the host/scene changed.");
                targets.Clear();
                foreach (LuggageRestoreState state in batch.states)
                {
                    if (!receivedLuggageRevisions.IsCurrent(actor, batch.scene, batch.revision, state.viewId))
                    {
                        targets.Add(null);
                        continue;
                    }
                    PhotonView view = PhotonView.Find(state.viewId);
                    Luggage luggage = view != null ? view.GetComponent<Luggage>() : null;
                    if (luggage == null) break;
                    if (!string.Equals(state.type, luggage.GetType().FullName, StringComparison.Ordinal)
                        || !string.Equals(state.name, NormalizeObjectName(luggage.name), StringComparison.Ordinal)
                        || (luggage.transform.position - state.position.ToUnity()).sqrMagnitude > LuggageRestoration.MaxMatchDistanceSquared)
                        throw new InvalidOperationException("A luggage network identity does not match the host.");
                    targets.Add(luggage);
                }
                if (targets.Count == batch.states.Count)
                {
                    // Validate the entire batch before changing any local box.
                    ValidateLuggageApi();
                    for (int i = 0; i < targets.Count; i++)
                        if (targets[i] != null) ApplyRestoredLuggage(targets[i], batch.states[i]);
                    break;
                }
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException("Luggage network objects did not become ready.");
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Logger.LogWarning("Luggage synchronization failed: " + error);
                break;
            }
            yield return null;
        }
        if (PhotonNetwork.InRoom) SendPlayerLoadEvent(actor, "reply", request, 0, expiresAt, error ?? "");
    }

    private IEnumerator RestoreLuggageRoutine(List<ContainerSnapshot> containers, List<LuggageSnapshot> legacy)
    {
        List<LuggageRestoreState> snapshots = new List<LuggageRestoreState>();
        if (containers != null && containers.Count > 0)
        {
            foreach (ContainerSnapshot snapshot in containers)
            {
                if (snapshot == null || !IsLuggageContainerSnapshot(snapshot)) continue;
                if (snapshot.state < 0 || snapshot.state > 1) throw new InvalidOperationException("Invalid saved luggage state.");
                snapshots.Add(new LuggageRestoreState { type = snapshot.containerType, name = NormalizeObjectName(snapshot.objectName),
                    path = snapshot.objectPath, position = snapshot.position, open = snapshot.state == 1,
                    activated = snapshot.activatedObjectActive ?? snapshot.state == 1, spent = snapshot.boolA, revivedPlayers = snapshot.boolB });
            }
        }
        else if (legacy != null)
        {
            foreach (LuggageSnapshot snapshot in legacy)
            {
                if (snapshot == null) continue;
                if (snapshot.state < 0 || snapshot.state > 1) throw new InvalidOperationException("Invalid legacy luggage state.");
                snapshots.Add(new LuggageRestoreState { type = snapshot.isRespawnChest ? typeof(RespawnChest).FullName : null,
                    name = NormalizeObjectName(snapshot.objectName), path = snapshot.objectPath, position = snapshot.position,
                    open = snapshot.state == 1, activated = snapshot.activatedObjectActive ?? snapshot.state == 1,
                    spent = snapshot.respawnChestSpent, revivedPlayers = snapshot.respawnChestRevivedPlayers });
            }
        }
        if (snapshots.Count == 0) yield break;
        if (snapshots.Count > 1024) throw new InvalidOperationException("Too many saved luggage objects.");
        ValidateLuggageApi();
        LuggageBatch batch = new LuggageBatch { scene = SceneManager.GetActiveScene().name, revision = ++luggageRevision };
        List<Luggage> targets = new List<Luggage>();
        float deadline = Time.realtimeSinceStartup + 5f;
        while (true)
        {
            if (batch.scene != SceneManager.GetActiveScene().name || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient))
                throw new InvalidOperationException("The host or scene changed during luggage matching.");
            List<Luggage> remaining = UnityObject.FindObjectsByType<Luggage>(FindObjectsSortMode.None).Where(x => x != null).ToList();
            List<LuggageRestoreState> identities = remaining.Select(CaptureLiveLuggage).ToList();
            targets.Clear();
            batch.states.Clear();
            string unmatched = null;
            foreach (LuggageRestoreState snapshot in snapshots)
            {
                int index = LuggageRestoration.FindMatch(snapshot, identities);
                if (index < 0 || (PhotonNetwork.InRoom && identities[index].viewId <= 0))
                {
                    unmatched = snapshot.name ?? "unnamed";
                    break;
                }
                // Resolve old saves once on the host. Peers use the exact live view.
                LuggageRestoreState state = identities[index];
                state.open = snapshot.open;
                state.activated = snapshot.activated;
                state.spent = snapshot.spent;
                state.revivedPlayers = snapshot.revivedPlayers;
                targets.Add(remaining[index]);
                batch.states.Add(state);
                remaining.RemoveAt(index);
                identities.RemoveAt(index);
            }
            if (unmatched == null) break;
            if (Time.realtimeSinceStartup >= deadline)
                throw new InvalidOperationException($"Saved luggage '{unmatched}' has no unique nearby network object. The map may have changed.");
            yield return null;
        }
        restoredLuggage.RemoveWhere(x => x == null);
        for (int i = 0; i < targets.Count; i++) ApplyRestoredLuggage(targets[i], batch.states[i]);
        IEnumerator sync = SyncLuggageClientsRoutine("luggage", JsonConvert.SerializeObject(batch));
        using (sync as IDisposable) { while (sync.MoveNext()) yield return sync.Current; }
        Logger.LogInfo($"Restored {targets.Count} luggage states and confirmed all current clients.");
    }

    private static LuggageRestoreState CaptureLiveLuggage(Luggage luggage)
    {
        PhotonView view = GetPhotonView(luggage);
        RespawnChest respawn = luggage as RespawnChest;
        return new LuggageRestoreState { viewId = view != null ? view.ViewID : 0, type = luggage.GetType().FullName,
            name = NormalizeObjectName(luggage.name), path = BuildTransformPath(luggage.transform),
            position = new Vector3Snapshot(luggage.transform.position), open = GetLuggageState(luggage) == 1,
            activated = luggage.activateOnOpen != null && luggage.activateOnOpen.activeSelf,
            spent = respawn != null && respawn.IsSpent, revivedPlayers = GetRespawnChestHasRevivedPlayers(respawn) };
    }

    private void ApplyRestoredLuggage(Luggage luggage, LuggageRestoreState state)
    {
        LuggageRestoration.Apply(new UnityLuggageRestoreTarget(luggage), state);
        restoredLuggage.Add(luggage);
    }

    private static void ValidateLuggageApi()
    {
        if (LuggageStateField == null || !LuggageStateField.FieldType.IsEnum
            || SpawnHistoryField == null || SpawnedItemsField == null || SetSpawnHistoryMethod == null)
            throw new InvalidOperationException("The game's luggage restoration API changed.");
    }

    private sealed class UnityLuggageRestoreTarget : ILuggageRestoreTarget
    {
        private readonly Luggage luggage;
        internal UnityLuggageRestoreTarget(Luggage luggage) { this.luggage = luggage; }
        public void CancelOpening() => luggage.StopAllCoroutines();
        public void ResetSpawnHistory()
        {
            Peak.SpawnedItemTracker tracker = luggage.GetComponent<Peak.SpawnedItemTracker>();
            if (tracker == null) return;
            SpawnHistoryField.SetValue(tracker, null);
            (SpawnedItemsField.GetValue(tracker) as IList)?.Clear();
            SetSpawnHistoryMethod.Invoke(tracker, new object[] { false });
        }
        public void SetOpen(bool open) => LuggageStateField.SetValue(luggage, Enum.ToObject(LuggageStateField.FieldType, open ? 1 : 0));
        public void SetVisual(bool open)
        {
            Animator animator = luggage.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
                throw new InvalidOperationException("A restored luggage has no animator.");
            if (open && !animator.HasState(0, Animator.StringToHash("Luggage_Open")))
                throw new InvalidOperationException("The game's luggage opening animation changed.");
            bool fireEvents = animator.fireEvents;
            animator.fireEvents = false;
            try
            {
                animator.Rebind();
                if (open) animator.Play("Luggage_Open", 0, 1f);
                animator.Update(0f);
            }
            finally { animator.fireEvents = fireEvents; }
        }
        public void SetAvailable(bool available)
        {
            while (Luggage.ALL_LUGGAGE.Remove(luggage)) { }
            if (available) Luggage.ALL_LUGGAGE.Add(luggage);
            luggage.HoverExit();
        }
        public void SetActivatedObject(bool active)
        {
            if (luggage.activateOnOpen != null) luggage.activateOnOpen.SetActive(active);
        }
        public void SetRevivalState(bool spent, bool revivedPlayers)
        {
            if (luggage is RespawnChest chest)
            {
                SetRespawnChestState(chest, spent, revivedPlayers);
                if (chest.skeleton != null) chest.skeleton.SetActive(!revivedPlayers);
            }
        }
    }

    [HarmonyPatch(typeof(Luggage), "OnCharacterRegistered")]
    private static class RestoredLuggageJoinPatch
    {
        private static void Postfix(Luggage __instance, Character __0)
        {
            if (Instance == null || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient
                || !Instance.restoredLuggage.Contains(__instance) || !__instance.gameObject.activeInHierarchy) return;
            PhotonView playerView = GetPhotonView(__0);
            if (playerView?.Owner == null || playerView.Owner.IsLocal) return;
            try
            {
                // Fresh live state: the box may have been opened since loading.
                LuggageBatch batch = new LuggageBatch { scene = SceneManager.GetActiveScene().name, revision = ++Instance.luggageRevision };
                batch.states.Add(CaptureLiveLuggage(__instance));
                SendPlayerLoadEvent(playerView.OwnerActorNr, "luggage", Guid.NewGuid().ToString("N"), 0,
                    unchecked(PhotonNetwork.ServerTimestamp + PlayerLoadRequestLifetimeMs), JsonConvert.SerializeObject(batch));
            }
            catch (Exception ex) { Instance.Logger.LogWarning("Could not sync restored luggage to a joining player: " + ex.Message); }
        }
    }
}
