using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ExitGames.Client.Photon;
using HarmonyLib;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Zorro.Core;

namespace PEAKSaveManager;

public sealed partial class SaveManagerPlugin : IOnEventCallback
{
    // Uncached, targeted, reliable messages. The GUID and protocol version keep
    // unrelated mods using the same application event code out of this handler.
    private const byte PlayerLoadEventCode = 173;
    // Every peer must prepare the saved map before Photon switches scenes.
    private const int PlayerLoadProtocol = 6;
    private const int PlayerLoadRequestLifetimeMs = 30000;
    private readonly Dictionary<string, OwnerLoadReply> ownerLoadReplies = new Dictionary<string, OwnerLoadReply>();
    private readonly HashSet<int> ownerLoadsInProgress = new HashSet<int>();

    private sealed class OwnerLoadReply
    {
        internal int actor;
        internal int viewId;
        internal bool completed;
        internal string error;
    }

    private IEnumerator RestoreOnPlayerOwnerRoutine(Character character, PlayerSnapshot snapshot)
    {
        if (snapshot != null) snapshot = ResolvePlayerLanding(character, snapshot);
        PhotonView view = GetPhotonView(character);
        if (view == null || view.IsMine)
        {
            if (snapshot == null) yield break;
            IEnumerator local = PlayerLoadRestoration.Restore(new UnityPlayerLoadTarget(character, snapshot,
                () => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient));
            try
            {
                while (local.MoveNext()) yield return local.Current;
            }
            finally
            {
                (local as IDisposable)?.Dispose();
            }
            yield break;
        }

        if (!PhotonNetwork.IsMasterClient || view.Owner == null)
            throw new InvalidOperationException("Only the host can request restoration on a player's owner.");

        string requestId = Guid.NewGuid().ToString("N");
        OwnerLoadReply reply = new OwnerLoadReply { actor = view.OwnerActorNr, viewId = view.ViewID };
        ownerLoadReplies.Add(requestId, reply);
        int expiresAt = unchecked(PhotonNetwork.ServerTimestamp + PlayerLoadRequestLifetimeMs);
        try
        {
            SendPlayerLoadEvent(reply.actor, snapshot == null ? "probe" : "restore", requestId, reply.viewId,
                expiresAt, snapshot == null ? "" : JsonConvert.SerializeObject(snapshot));
            float deadline = Time.realtimeSinceStartup + (snapshot == null ? 5f : 30f);
            while (!reply.completed)
            {
                if (character == null || view == null || view.OwnerActorNr != reply.actor || !PhotonNetwork.IsMasterClient)
                    throw new InvalidOperationException("Player left or changed ownership during load.");
                if (Time.realtimeSinceStartup >= deadline)
                    throw new TimeoutException(snapshot == null
                        ? "A restored player needs this save-manager build; owner support check timed out."
                        : "Player owner did not confirm teleport/equipment restoration before timeout.");
                yield return null;
            }
            if (!string.IsNullOrEmpty(reply.error))
                throw new InvalidOperationException("Player owner could not restore the save: " + reply.error);
        }
        finally
        {
            ownerLoadReplies.Remove(requestId);
        }
    }

    private static void SendPlayerLoadEvent(int actor, string kind, string requestId, int viewId, int expiresAt, string payload)
    {
        object[] message = { PluginGuid, PlayerLoadProtocol, kind, requestId, viewId, expiresAt, payload };
        if (!PhotonNetwork.RaiseEvent(PlayerLoadEventCode, message,
                new RaiseEventOptions { TargetActors = new[] { actor } }, SendOptions.SendReliable))
            throw new InvalidOperationException("Could not send the player restoration request.");
    }

    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != PlayerLoadEventCode || !(photonEvent.CustomData is object[] message)
            || message.Length != 7 || !(message[0] is string guid) || guid != PluginGuid
            || !(message[1] is int protocol) || protocol != PlayerLoadProtocol
            || !(message[2] is string kind) || !(message[3] is string requestId)
            || !(message[4] is int viewId) || !(message[5] is int expiresAt) || !(message[6] is string payload)) return;

        if (kind == "scene-reply")
        {
            if (viewId == 0 && unchecked(expiresAt - PhotonNetwork.ServerTimestamp) > 0)
                sceneLoadBarrier?.Acknowledge(photonEvent.Sender, requestId, payload);
            return;
        }

        if (kind == "reply")
        {
            if (ownerLoadReplies.TryGetValue(requestId, out OwnerLoadReply reply)
                && reply.actor == photonEvent.Sender && reply.viewId == viewId)
            {
                reply.error = payload;
                reply.completed = true;
            }
            return;
        }

        // World and player changes may only be requested by the current host.
        bool sceneRequest = kind == "scene-check" || kind == "scene-prepare" || kind == "scene-arm"
            || kind == "scene-ready" || kind == "scene-cancel" || kind == "scene-finish";
        // A release can wait behind a slow native scene load. Its session ID
        // still prevents it from releasing a later attempt when finally received.
        bool sceneRelease = kind == "scene-cancel" || kind == "scene-finish";
        if ((!sceneRequest && kind != "probe" && kind != "restore" && kind != "world-probe" && kind != "luggage") || PhotonNetwork.MasterClient == null
            || photonEvent.Sender != PhotonNetwork.MasterClient.ActorNumber
            || (!sceneRelease && unchecked(expiresAt - PhotonNetwork.ServerTimestamp) <= 0)) return;

        if (sceneRequest)
        {
            if (viewId == 0) HandleSharedSceneRequest(kind, payload, photonEvent.Sender, requestId, expiresAt);
            return;
        }

        if (kind == "world-probe" || kind == "luggage")
        {
            if (viewId == 0) HandleLuggageRequest(kind, payload, photonEvent.Sender, requestId, expiresAt);
            return;
        }

        try
        {
            PhotonView view = PhotonView.Find(viewId);
            Character character = view != null && view.IsMine ? view.GetComponent<Character>() : null;
            if (character == null || !character.IsLocal)
                throw new InvalidOperationException("Requested player is not owned by this client.");
            if (kind == "probe")
            {
                SendPlayerLoadEvent(photonEvent.Sender, "reply", requestId, viewId, expiresAt, "");
                return;
            }
            PlayerSnapshot snapshot = JsonConvert.DeserializeObject<PlayerSnapshot>(payload);
            if (snapshot?.position == null || snapshot.rotation == null || snapshot.character?.lookValues == null)
                throw new InvalidOperationException("Player position or view snapshot is missing.");
            if (!ownerLoadsInProgress.Add(viewId))
                throw new InvalidOperationException("A restoration is already running for this player.");
            StartCoroutine(RestoreRequestedPlayerRoutine(character, snapshot, photonEvent.Sender, requestId, viewId, expiresAt));
        }
        catch (Exception ex)
        {
            Logger.LogWarning("Owner restoration request failed: " + ex.Message);
            SendPlayerLoadEvent(photonEvent.Sender, "reply", requestId, viewId, expiresAt, ex.Message);
        }
    }

    private IEnumerator RestoreRequestedPlayerRoutine(Character character, PlayerSnapshot snapshot, int requester,
        string requestId, int viewId, int expiresAt)
    {
        string error = "";
        IEnumerator routine = PlayerLoadRestoration.Restore(new UnityPlayerLoadTarget(character, snapshot,
            () => PhotonNetwork.MasterClient != null && PhotonNetwork.MasterClient.ActorNumber == requester
                && unchecked(expiresAt - PhotonNetwork.ServerTimestamp) > 0));
        try
        {
            // Flatten MoveNext so native coroutine timeouts/errors become a
            // negative reply and reach the host's existing load error handler.
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Logger.LogError("Owner player restoration failed: " + ex);
                    break;
                }
                yield return current;
            }
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
            ownerLoadsInProgress.Remove(viewId);
        }
        if (PhotonNetwork.InRoom)
            SendPlayerLoadEvent(requester, "reply", requestId, viewId, expiresAt, error);
    }

    private sealed class UnityPlayerLoadTarget : IPlayerLoadTarget, IPlayerLandingTarget
    {
        private static readonly FieldInfo WarpRoutineField = AccessTools.Field(typeof(Character), "_warpRoutine");
        private static readonly WarpStatusLock StatusLock = new WarpStatusLock(typeof(Character));
        private static readonly MethodInfo SetWarpingMethod = AccessTools.PropertySetter(typeof(Character), "warping");
        private readonly Character character;
        private readonly PlayerSnapshot snapshot;
        private readonly Func<bool> requestIsCurrent;
        private readonly PhotonView view;
        private readonly byte? selectedSlot;
        private readonly List<KeyValuePair<Collider, bool>> collisionBeforeWarp = new List<KeyValuePair<Collider, bool>>();
        private Coroutine ownedWarp;
        private bool warpCompleted;
        private bool equipmentStarted;

        internal UnityPlayerLoadTarget(Character character, PlayerSnapshot snapshot, Func<bool> requestIsCurrent)
        {
            this.character = character;
            this.snapshot = snapshot;
            this.requestIsCurrent = requestIsCurrent;
            view = GetPhotonView(character);
            selectedSlot = PlayerLoadRestoration.ResolveEquippedSlot(snapshot.inventory,
                character.player?.itemSlots?.Length ?? 0,
                slot => character.player?.GetItemSlot(slot)?.prefab?.itemID);
            if (snapshot.inventory != null && snapshot.inventory.heldItemId != ushort.MaxValue && !selectedSlot.HasValue)
                Instance?.Logger.LogWarning($"Saved held item {snapshot.inventory.heldItemId} is absent from native equippable slots; restoring empty hands.");
        }

        public bool IsAvailable => character != null && character.IsLocal && character.player != null
            && character.data != null && character.refs?.ragdoll != null && character.refs.items != null
            && (view == null || view.IsMine) && requestIsCurrent()
            && (ownedWarp == null || ReferenceEquals(WarpRoutineField.GetValue(character), ownedWarp));
        public float Realtime => Time.realtimeSinceStartup;
        public bool IsWarping => character != null && character.warping;
        public bool WarpCompleted => warpCompleted;
        public bool IsAtDestination => character != null && Vector3.Distance(character.Center, snapshot.position.ToUnity()) <= 0.75f;
        public bool RequiresStableLanding => LandingSelection.NeedsSupport(snapshot.character);
        public bool IsLandingStable
        {
            get
            {
                if (!IsAvailable || !character.data.isGrounded || !IsAtDestination) return false;
                foreach (Bodypart part in character.refs.ragdoll.partList)
                    if (part != null && part.Rig != null && part.Rig.linearVelocity.sqrMagnitude > 2.25f) return false;
                return true;
            }
        }
        public bool IsEquipmentReady
        {
            get
            {
                if (!IsAvailable) return false;
                Item held = character.data.currentItem;
                if (!selectedSlot.HasValue) return held == null;
                if (!equipmentStarted || held == null || held.rig == null || held.holderCharacter != character
                    || held.itemID != snapshot.inventory.heldItemId || !held.gameObject.scene.IsValid()) return false;
                // The native Equip coroutine attaches the joints after several
                // physics ticks. An allocated Item alone is not completion.
                return HasItemJoint(BodypartType.Hand_R, held.rig)
                    && (held.rightHandOnly || HasItemJoint(BodypartType.Hand_L, held.rig));
            }
        }

        private bool HasItemJoint(BodypartType part, Rigidbody itemRig)
        {
            foreach (Bodypart bodypart in character.refs.ragdoll.partList)
            {
                if (bodypart == null || bodypart.partType != part || bodypart.Rig == null) continue;
                foreach (FixedJoint joint in bodypart.Rig.GetComponents<FixedJoint>())
                    if (joint.connectedBody == itemRig) return true;
            }
            return false;
        }

        public void ClearHeldItem()
        {
            CharacterItems items = character.refs.items;
            Item held = character.data.currentItem;
            PhotonView heldView = held != null ? GetPhotonView(held) : null;
            if (held != null && (held.rig == null || !held.gameObject.scene.IsValid()
                || (PhotonNetwork.InRoom && (heldView == null || heldView.ViewID == 0))))
            {
                // Old buggy loads could leave a database asset here. Detach it
                // without invoking item callbacks or trying to destroy View 0.
                items.UnAttachEquippedItem();
            }
            items.currentSelectedSlot = Optionable<byte>.None;
            PhotonView itemsView = GetPhotonView(items);
            if (itemsView != null)
                itemsView.RPC("EquipSlotRpc", RpcTarget.All, -1, -1);
            else
                items.EquipSlotRpc(-1, -1);
        }

        public void ApplyLook()
        {
            character.transform.eulerAngles = snapshot.rotation.ToUnity();
            character.data.lookValues = snapshot.character.lookValues.ToUnity();
            RecalculateLookDirectionsMethod.Invoke(character, null);
        }

        public void BeginWarp()
        {
            if (WarpRoutineField == null || !StatusLock.IsSupported || SetWarpingMethod == null)
                throw new InvalidOperationException("The game's teleport cleanup API changed.");
            foreach (Collider collider in character.refs.ragdoll.GetComponentsInChildren<Collider>(true))
                collisionBeforeWarp.Add(new KeyValuePair<Collider, bool>(collider, collider.enabled));
            character.WarpCompleted += OnWarpCompleted;
            // Execute on the physical owner. Other peers receive its native
            // position synchronization, rather than simulating a second warp.
            character.WarpPlayerRPC(snapshot.position.ToUnity(), false);
            ownedWarp = (Coroutine)WarpRoutineField.GetValue(character);
        }

        private void OnWarpCompleted(Character source)
        {
            if (source == character && ownedWarp != null
                && ReferenceEquals(WarpRoutineField.GetValue(character), ownedWarp)) warpCompleted = true;
        }

        public void RestoreLifeState()
        {
            CharacterSnapshot saved = snapshot.character;
            character.data.dead = saved.dead;
            character.data.passedOut = saved.passedOut;
            character.data.fullyPassedOut = saved.fullyPassedOut;
            character.data.deathTimer = saved.dead ? 1f : 0f;
            character.data.SetPetrify(PlayerRestoration.GetSavedPetrify(saved.statuses));
            character.refs.ragdoll.ToggleCollision(enableCollision: !saved.dead);
            // Replay after the owner's warp status lock has ended. SetStatus,
            // stamina and PushAll are authoritative only on this owning client.
            ApplyCharacterStatuses(character, saved.statuses);
            character.SetExtraStamina(saved.extraStamina);
            character.data.currentStamina = saved.currentStamina;
            character.data.PushAll();
        }

        public void HaltMotion()
        {
            character.refs.ragdoll.HaltBodyVelocity(resetFallDamage: true);
            character.data.avarageVelocity = Vector3.zero;
            character.data.avarageLastFrameVelocity = Vector3.zero;
            character.data.fallSeconds = 0f;
            Item held = character.data.currentItem;
            if (held != null && held.rig != null && !held.rig.isKinematic)
            {
                held.rig.linearVelocity = Vector3.zero;
                held.rig.angularVelocity = Vector3.zero;
            }
        }

        public void EquipSavedSlot()
        {
            if (!selectedSlot.HasValue) return;
            character.refs.items.EquipSlot(Optionable<byte>.Some(selectedSlot.Value));
            character.refs.items.LockFromSwitching(PlayerLoadRestoration.EquipTimeoutSeconds);
            equipmentStarted = true;
        }

        public void Finish(bool succeeded)
        {
            if (character == null) return;
            character.WarpCompleted -= OnWarpCompleted;
            if (equipmentStarted && character.refs?.items != null)
                character.refs.items.lastEquippedSlotTime = Time.time;
            if (!succeeded && ownedWarp != null && character.warping
                && ReferenceEquals(WarpRoutineField.GetValue(character), ownedWarp))
            {
                character.StopCoroutine(ownedWarp);
                SetWarpingMethod.Invoke(character, new object[] { false });
                StatusLock.Release(character);
                foreach (KeyValuePair<Collider, bool> state in collisionBeforeWarp)
                    if (state.Key != null) state.Key.enabled = state.Value;
                if (character.refs?.ragdoll != null) HaltMotion();
            }
        }
    }
}
