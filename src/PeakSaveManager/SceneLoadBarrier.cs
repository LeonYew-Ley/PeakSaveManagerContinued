using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PEAKSaveManager;

internal sealed class SceneLoadParty
{
    internal readonly int HostActor;
    internal readonly int[] Actors;

    internal SceneLoadParty(int hostActor, IEnumerable<int> actors)
    {
        HostActor = hostActor;
        Actors = actors.OrderBy(actor => actor).ToArray();
        if (Actors.Length == 0 || !Actors.Contains(hostActor) || Actors.Any(actor => actor <= 0)
            || Actors.Distinct().Count() != Actors.Length)
            throw new InvalidOperationException("Invalid room membership for a shared load.");
    }

    internal void Validate(int hostActor, IEnumerable<int> actors)
    {
        if (hostActor != HostActor)
            throw new InvalidOperationException("The host changed during the shared load. Retry with the current party.");
        int[] current = actors.OrderBy(actor => actor).ToArray();
        if (!Actors.SequenceEqual(current))
            throw new InvalidOperationException("A player joined or left during the shared load. Retry with the current party.");
    }
}

// A new request ID for each phase prevents late replies from completing a retry.
internal sealed class SceneLoadBarrier
{
    internal readonly SceneLoadParty Party;
    internal readonly string RequestId;
    private readonly float deadline;
    private readonly Dictionary<int, string> replies = new Dictionary<int, string>();
    internal IEnumerable<int> Peers => Party.Actors.Where(actor => actor != Party.HostActor);
    internal bool IsComplete => Peers.All(actor => replies.ContainsKey(actor));

    internal SceneLoadBarrier(SceneLoadParty party, string requestId, float deadline)
    {
        Party = party;
        RequestId = requestId;
        this.deadline = deadline;
    }

    internal void Acknowledge(int actor, string requestId, string error)
    {
        if (requestId == RequestId && Peers.Contains(actor) && !replies.ContainsKey(actor))
            replies.Add(actor, error ?? "");
    }

    internal void Check(int hostActor, IEnumerable<int> actors, float realtime, string phase, Func<int, string> name)
    {
        Party.Validate(hostActor, actors);
        foreach (KeyValuePair<int, string> reply in replies)
            if (!string.IsNullOrEmpty(reply.Value))
                throw new InvalidOperationException($"{name(reply.Key)} failed {phase}: {reply.Value}");
        if (!IsComplete && realtime >= deadline)
            throw new TimeoutException($"Waiting for {string.Join(", ", Peers.Where(actor => !replies.ContainsKey(actor)).Select(name))} during {phase}. All players need this build and must finish loading.");
    }

    internal static IEnumerator GuardProcess(IEnumerator process, Action validate, Action<Exception> failed)
    {
        Stack<IEnumerator> steps = new Stack<IEnumerator>();
        steps.Push(process);
        try
        {
            while (steps.Count > 0)
            {
                object current;
                try
                {
                    validate();
                    IEnumerator step = steps.Peek();
                    if (!step.MoveNext())
                    {
                        steps.Pop();
                        (step as IDisposable)?.Dispose();
                        continue;
                    }
                    current = step.Current;
                    // Native scene processes nest enumerators. Keep cancellation
                    // checks inside that chain, before the eventual LoadLevel.
                    if (current is IEnumerator nested)
                    {
                        steps.Push(nested);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    failed(ex);
                    yield break;
                }
                yield return current;
            }
        }
        finally
        {
            while (steps.Count > 0) (steps.Pop() as IDisposable)?.Dispose();
        }
    }
}
