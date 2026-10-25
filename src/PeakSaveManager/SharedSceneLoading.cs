using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PEAKSaveManager;

public sealed partial class SaveManagerPlugin
{
    private const float SceneReadyTimeoutSeconds = 75f;
    private const int ScenePreparationLifetimeMs = 300000;
    private SharedScenePreparation sharedScenePreparation;
    private SceneLoadBarrier sceneLoadBarrier;

    private sealed class ScenePreparationRequest
    {
        public string session;
        public string scene;
        public int seed;
        public int ascent;
        public int[] actors;
    }

    private sealed class SharedScenePreparation
    {
        internal ScenePreparationRequest Request;
        internal SceneLoadParty Party;
        internal Room Room;
        internal string SourceScene;
        internal int ExpiresAt;
        internal int PreviousAscent;
        internal bool Prepared;
        internal bool Armed;
        internal bool Busy;
        internal bool AddedSceneSwitchingStatus;
        internal Exception LoadError;
    }

    private SharedScenePreparation CreateScenePreparation(ScenePreparationRequest request, int hostActor)
    {
        if (!PhotonNetwork.AutomaticallySyncScene)
            throw new InvalidOperationException("Photon automatic scene synchronization is disabled on this client.");
        if (request == null || !Guid.TryParseExact(request.session, "N", out _)
            || (!SavedMapRestoration.TryGetLevelNumber(request.scene, out _) && request.scene != "Airport")
            || request.actors == null || request.actors.Length > 256)
            throw new InvalidOperationException("Invalid shared map preparation request.");
        SharedScenePreparation preparation = new SharedScenePreparation
        {
            Request = request,
            Party = new SceneLoadParty(hostActor, request.actors),
            Room = PhotonNetwork.CurrentRoom,
            SourceScene = SceneManager.GetActiveScene().name,
            ExpiresAt = unchecked(PhotonNetwork.ServerTimestamp + ScenePreparationLifetimeMs),
            PreviousAscent = Ascents.currentAscent
        };
        ValidateScenePreparation(preparation);
        return preparation;
    }

    private void ValidateScenePreparation(SharedScenePreparation preparation)
    {
        if (!PhotonNetwork.InRoom || !ReferenceEquals(preparation.Room, PhotonNetwork.CurrentRoom))
            throw new InvalidOperationException("The room changed during the shared load.");
        preparation.Party.Validate(PhotonNetwork.MasterClient?.ActorNumber ?? 0,
            PhotonNetwork.PlayerList.Select(player => player.ActorNumber));
        if (unchecked(preparation.ExpiresAt - PhotonNetwork.ServerTimestamp) <= 0)
            throw new TimeoutException("Shared map preparation expired. Retry the load.");
        string active = SceneManager.GetActiveScene().name;
        if (active != preparation.SourceScene && (!preparation.Armed || active != preparation.Request.scene))
            throw new InvalidOperationException("The scene changed outside the shared load.");
    }

    private void ValidateSharedLoad()
    {
        if (sharedScenePreparation != null) ValidateScenePreparation(sharedScenePreparation);
    }

    private void UpdateSharedScenePreparation()
    {
        if (sharedScenePreparation == null) return;
        try { ValidateSharedLoad(); }
        catch (Exception ex)
        {
            // The host coroutine must observe the failure before releasing its guard.
            if (isLoading) return;
            Logger.LogWarning("Cancelled shared map preparation: " + ex.Message);
            ClearScenePreparation(false);
        }
    }

    private void ClearScenePreparation(bool succeeded)
    {
        SharedScenePreparation preparation = sharedScenePreparation;
        sharedScenePreparation = null;
        if (preparation == null) return;
        ClearPendingSeedForLoad();
        if (!succeeded && preparation.AddedSceneSwitchingStatus)
            GameHandler.ClearStatus<SceneSwitchingStatus>();
        if (!succeeded && preparation.Armed && !LoadingScreenHandler.loading
            && SceneManager.GetActiveScene().name == preparation.SourceScene)
            Ascents.currentAscent = preparation.PreviousAscent;
    }

    private void EndSharedScenePreparation(bool succeeded)
    {
        SharedScenePreparation preparation = sharedScenePreparation;
        if (preparation == null) return;
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient
            && ReferenceEquals(preparation.Room, PhotonNetwork.CurrentRoom))
        {
            foreach (int actor in preparation.Party.Actors.Where(actor => actor != preparation.Party.HostActor))
            {
                try
                {
                    SendPlayerLoadEvent(actor, succeeded ? "scene-finish" : "scene-cancel", Guid.NewGuid().ToString("N"), 0,
                        unchecked(PhotonNetwork.ServerTimestamp + 30000), preparation.Request.session);
                }
                catch (Exception ex) { Logger.LogWarning("Could not release peer map preparation: " + ex.Message); }
            }
        }
        ClearScenePreparation(succeeded);
    }

    private void ArmScenePreparation(SharedScenePreparation preparation)
    {
        ValidateScenePreparation(preparation);
        if (!preparation.Prepared || !Application.CanStreamedLevelBeLoaded(preparation.Request.scene))
            throw new InvalidOperationException("The saved map resource bundle is not ready.");
        if (LoadingScreenHandler.loading)
            throw new InvalidOperationException("Another scene load is already running.");
        preparation.Armed = true;
        preparation.ExpiresAt = unchecked(PhotonNetwork.ServerTimestamp + ScenePreparationLifetimeMs);
        Ascents.currentAscent = preparation.Request.ascent;
        // Arm only after every bundle is ready, so slow preparation cannot expire a seed.
        if (preparation.Request.seed != 0) SetPendingSeedForLoad(preparation.Request.seed);
        else ClearPendingSeedForLoad();
    }

    private IEnumerator PrepareSharedMapRoutine(string scene, SaveMetadata metadata)
    {
        sharedScenePreparation = CreateScenePreparation(new ScenePreparationRequest
        {
            session = Guid.NewGuid().ToString("N"), scene = scene, seed = metadata.levelSeed,
            ascent = metadata.ascent, actors = PhotonNetwork.PlayerList.Select(player => player.ActorNumber).ToArray()
        }, PhotonNetwork.LocalPlayer.ActorNumber);
        // Fail old/missing client builds quickly, before either side loads resources.
        IEnumerator check = SharedScenePhaseRoutine("scene-check", JsonConvert.SerializeObject(sharedScenePreparation.Request), 8f);
        using (check as IDisposable) { while (check.MoveNext()) yield return check.Current; }
        IEnumerator prepare = SharedScenePhaseRoutine("scene-prepare", sharedScenePreparation.Request.session,
            SavedMapRestoration.BundleTimeoutSeconds + 15f, PrepareLocalMapRoutine(sharedScenePreparation));
        using (prepare as IDisposable) { while (prepare.MoveNext()) yield return prepare.Current; }
        IEnumerator arm = SharedScenePhaseRoutine("scene-arm", sharedScenePreparation.Request.session, 10f);
        using (arm as IDisposable) { while (arm.MoveNext()) yield return arm.Current; }
        ArmScenePreparation(sharedScenePreparation);
        Logger.LogInfo($"All {sharedScenePreparation.Party.Actors.Length} players prepared '{scene}'. Starting shared load.");
    }

    private IEnumerator SharedScenePhaseRoutine(string phase, string payload, float timeout, IEnumerator local = null)
    {
        SharedScenePreparation preparation = sharedScenePreparation;
        ValidateScenePreparation(preparation);
        SceneLoadBarrier barrier = new SceneLoadBarrier(preparation.Party, Guid.NewGuid().ToString("N"), Time.realtimeSinceStartup + timeout);
        sceneLoadBarrier = barrier;
        int expiresAt = unchecked(PhotonNetwork.ServerTimestamp + (int)(timeout * 1000f));
        try
        {
            foreach (int actor in barrier.Peers) SendPlayerLoadEvent(actor, phase, barrier.RequestId, 0, expiresAt, payload);
            bool localDone = local == null;
            while (true)
            {
                ValidateScenePreparation(preparation);
                barrier.Check(PhotonNetwork.MasterClient?.ActorNumber ?? 0,
                    PhotonNetwork.PlayerList.Select(player => player.ActorNumber), Time.realtimeSinceStartup, phase, ActorName);
                if (!localDone) localDone = !local.MoveNext();
                if (barrier.IsComplete && localDone) break;
                yield return localDone ? null : local.Current;
            }
            Logger.LogInfo($"Shared load {phase}: all {preparation.Party.Actors.Length} players confirmed.");
        }
        finally
        {
            (local as IDisposable)?.Dispose();
            if (ReferenceEquals(sceneLoadBarrier, barrier)) sceneLoadBarrier = null;
        }
    }

    private static string ActorName(int actor) => PhotonNetwork.CurrentRoom?.GetPlayer(actor)?.NickName ?? $"actor #{actor}";

    private IEnumerator PrepareLocalMapRoutine(SharedScenePreparation preparation)
    {
        IEnumerator load = preparation.Request.scene == "Airport" ? null
            : SavedMapRestoration.Prepare(preparation.Request.scene, new SavedMapLoader());
        using (load as IDisposable)
        {
            while (load != null)
            {
                ValidateScenePreparation(preparation);
                if (!load.MoveNext()) break;
                yield return load.Current;
            }
        }
        if (!Application.CanStreamedLevelBeLoaded(preparation.Request.scene))
            throw new InvalidOperationException("The saved scene is unavailable after resource preparation.");
        preparation.Prepared = true;
    }

    private IEnumerator WaitForSharedSceneReadyRoutine()
    {
        SharedScenePreparation preparation = sharedScenePreparation;
        IEnumerator ready = SharedScenePhaseRoutine("scene-ready", preparation.Request.session, SceneReadyTimeoutSeconds + 5f,
            WaitForLocalSceneReadyRoutine(preparation));
        using (ready as IDisposable) { while (ready.MoveNext()) yield return ready.Current; }
        float deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
        while (true)
        {
            ValidateScenePreparation(preparation);
            bool allReady = preparation.Party.Actors.All(actor => PlayerHandler.GetAllPlayers().Any(player =>
                GetPhotonView(player)?.OwnerActorNr == actor && IsPlayerInSceneReady(player, preparation.Request.scene)));
            if (allReady) yield break;
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException("Not all party characters initialized on the host. Restoration stopped.");
            yield return null;
        }
    }

    private static bool IsPlayerInSceneReady(Player player, string scene) => IsPlayerReadyForRestore(player)
        && player.gameObject.scene.name == scene && player.character.gameObject.scene.name == scene;

    private IEnumerator WaitForLocalSceneReadyRoutine(SharedScenePreparation preparation)
    {
        float deadline = Time.realtimeSinceStartup + SceneReadyTimeoutSeconds;
        while (true)
        {
            ValidateScenePreparation(preparation);
            if (!preparation.Armed) throw new InvalidOperationException("Shared map load was not armed.");
            Character character = Character.localCharacter;
            if (SceneManager.GetActiveScene().name == preparation.Request.scene && !LoadingScreenHandler.loading
                && character != null && character.IsLocal && IsPlayerInSceneReady(character.player, preparation.Request.scene)
                && (preparation.Request.scene == "Airport" || MapHandler.ExistsAndInitialized)) yield break;
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException($"Scene '{preparation.Request.scene}' or local character did not finish loading.");
            yield return null;
        }
    }

    private IEnumerator GuardSharedSceneProcess(IEnumerator process, SharedScenePreparation preparation)
    {
        return SceneLoadBarrier.GuardProcess(process,
            () =>
            {
                if (!ReferenceEquals(sharedScenePreparation, preparation))
                    throw new InvalidOperationException("The shared scene load was cancelled before completion.");
                ValidateScenePreparation(preparation);
            },
            ex =>
            {
                preparation.LoadError = ex;
                Logger.LogError("Shared native scene process stopped: " + ex.Message);
            });
    }

    private void HandleSharedSceneRequest(string kind, string payload, int actor, string request, int expiresAt)
    {
        try
        {
            if (kind == "scene-cancel" || kind == "scene-finish")
            {
                if (sharedScenePreparation?.Request.session == payload) ClearScenePreparation(kind == "scene-finish");
                return;
            }
            if (kind == "scene-check")
            {
                if (payload.Length > 4096 || sharedScenePreparation != null || isLoading || LoadingScreenHandler.loading)
                    throw new InvalidOperationException("Another load is already active or the preparation request is invalid.");
                sharedScenePreparation = CreateScenePreparation(JsonConvert.DeserializeObject<ScenePreparationRequest>(payload), actor);
            }
            SharedScenePreparation preparation = sharedScenePreparation;
            if (preparation == null || (kind != "scene-check" && preparation.Request.session != payload))
                throw new InvalidOperationException("The shared map preparation request is stale or missing.");
            ValidateScenePreparation(preparation);
            if (preparation.Busy) throw new InvalidOperationException("Shared map preparation is already running.");
            if (kind == "scene-prepare" || kind == "scene-ready")
            {
                preparation.Busy = true;
                StartCoroutine(HandleSharedSceneRoutine(preparation, kind, actor, request, expiresAt));
                return;
            }
            if (kind == "scene-arm") ArmScenePreparation(preparation);
            SendPlayerLoadEvent(actor, "scene-reply", request, 0, expiresAt, "");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Shared load {kind} failed: {ex.Message}");
            SendPlayerLoadEvent(actor, "scene-reply", request, 0, expiresAt, ex.Message);
        }
    }

    private IEnumerator HandleSharedSceneRoutine(SharedScenePreparation preparation, string kind, int actor, string request, int expiresAt)
    {
        string error = "";
        IEnumerator routine = kind == "scene-prepare" ? PrepareLocalMapRoutine(preparation) : WaitForLocalSceneReadyRoutine(preparation);
        try
        {
            while (ReferenceEquals(sharedScenePreparation, preparation))
            {
                object current;
                try
                {
                    if (unchecked(expiresAt - PhotonNetwork.ServerTimestamp) <= 0)
                        throw new TimeoutException("The peer scene request expired.");
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Logger.LogWarning($"Shared load {kind} failed: {error}");
                    break;
                }
                yield return current;
            }
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
            preparation.Busy = false;
        }
        if (ReferenceEquals(sharedScenePreparation, preparation) && PhotonNetwork.InRoom)
        {
            Logger.LogInfo($"Shared load {kind} reply: {(error == "" ? "ready" : error)}.");
            SendPlayerLoadEvent(actor, "scene-reply", request, 0, expiresAt, error);
        }
    }
}
