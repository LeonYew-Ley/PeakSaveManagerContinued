using PEAKSaveManager;

internal static class PlayerRestorationTests
{
    internal static void Run()
    {
        VerifyIdentity();
        VerifyLifeTransitions();
        VerifyFailuresStopRestoration();
        VerifyStatusReplay();
        VerifyPetrifyReplay();
        Console.WriteLine("Player restoration verification passed: 512 host/client life-state combinations (each loaded twice), identity ambiguity, failure propagation, 15 status replay scenarios and 101 petrify percentages. Simulated transport only; Unity/Photon gameplay was not exercised.");
    }

    private static void VerifyIdentity()
    {
        Scout host = new("Host", 2);
        Scout teammate = new("Teammate", 7);
        PlayerSnapshot saved = new() { playerName = "teammate", actorNumber = 2 };
        Scout found = PlayerRestoration.FindUniquePlayer(saved, new[] { host, teammate }, p => p.Name, out string reason);
        Check(found == teammate && reason == null, "A reused actor number selected the wrong player.");
        found = PlayerRestoration.FindUniquePlayer(saved, new[] { host }, p => p.Name, out reason);
        Check(found == null && reason != null, "Absent/renamed teammate matched a different actor.");
        found = PlayerRestoration.FindUniquePlayer(saved, new[] { teammate, new Scout("TEAMMATE", 2) }, p => p.Name, out reason);
        Check(found == null && reason.Contains("ambiguous"), "Duplicate connected names must be rejected, even if one actor matches.");
        Check(PlayerRestoration.HasUniqueSavedName(saved, new[] { saved, new PlayerSnapshot { playerName = "Host" } }), "Unique old-format name was rejected.");
        Check(!PlayerRestoration.HasUniqueSavedName(saved, new[] { saved, new PlayerSnapshot { playerName = "Teammate" } }), "Duplicate saved names could restore the wrong person's snapshot.");
        Check(!PlayerRestoration.HasUniqueSavedName(null, new[] { saved }), "Null snapshot must not match.");
        Check(PlayerRestoration.FindUniquePlayer(new PlayerSnapshot(), new[] { host }, p => p.Name, out _) == null, "Nameless snapshot must not match by actor.");
    }

    private static void VerifyLifeTransitions()
    {
        // Include divergent host/owner states: a host that already sees a living
        // scout must still repair a dead owning client. No client mod is modeled.
        for (int savedBits = 0; savedBits < 8; savedBits++)
        for (int hostBits = 0; hostBits < 8; hostBits++)
        for (int ownerBits = 0; ownerBits < 8; ownerBits++)
        {
            CharacterSnapshot snapshot = State(savedBits);
            NativeScout host = new(State(hostBits));
            NativeScout owner = new(State(ownerBits));
            NativeScout[] clients = { host, owner };
            List<Action> reliableQueue = new();
            int reviveCalls = 0;

            for (int load = 0; load < 2; load++)
            {
                List<string> order = new();
                PlayerRestoration.ApplyBeforeWarp(snapshot,
                    (rpc, arguments) =>
                    {
                        order.Add("life");
                        Check(rpc != "RPCA_ReviveAtPosition", "Restore must not use the item-dropping, warping revive RPC.");
                        if (rpc == "ReviveCharacter")
                        {
                            reviveCalls++;
                            Check(arguments.Length == 1 && arguments[0] is false, "Revive penalties must be disabled.");
                        }
                        else
                        {
                            Check(rpc == "RPCA_SetDead" && arguments.Length == 0, "Unexpected native life RPC.");
                        }

                        reliableQueue.Add(() =>
                        {
                            foreach (NativeScout client in clients)
                            {
                                client.ReceiveNativeLifeRpc(rpc, arguments);
                            }
                        });
                    },
                    () =>
                    {
                        order.Add("flags");
                        reliableQueue.Add(() =>
                        {
                            foreach (NativeScout client in clients)
                            {
                                client.Dead = snapshot.dead;
                                client.PassedOut = snapshot.passedOut;
                                client.FullyPassedOut = snapshot.fullyPassedOut;
                            }
                        });
                    },
                    () =>
                    {
                        order.Add("inventory");
                        reliableQueue.Add(() =>
                        {
                            foreach (NativeScout client in clients)
                            {
                                // Replacement, as in SyncInventoryRPC(forceSync).
                                client.Inventory = new[] { "bandage", "backpack", "rope" };
                            }
                        });
                    },
                    () =>
                    {
                        order.Add("snapshot");
                        reliableQueue.Add(() =>
                        {
                            foreach (NativeScout client in clients)
                            {
                                client.Curse = 0.125f;
                                client.Hunger = 0.25f;
                            }
                        });
                    });

                Check(order.SequenceEqual(new[] { "life", "flags", "inventory", "snapshot" }), "Restore steps ran in the wrong order.");
                foreach (Action deliver in reliableQueue)
                {
                    deliver();
                }
                reliableQueue.Clear();

                foreach (NativeScout client in clients)
                {
                    Check(client.Dead == snapshot.dead && client.PassedOut == snapshot.passedOut
                        && client.FullyPassedOut == snapshot.fullyPassedOut, "Host/owner did not restore saved life flags.");
                    Check(client.CollisionEnabled == !snapshot.dead, "Living/dead collision state was not prepared correctly.");
                    if (!snapshot.dead)
                    {
                        Check(client.DeathTimer == 0 && client.FallSeconds == 0, "Revival left fatal timers running.");
                    }
                    Check(client.Inventory.SequenceEqual(new[] { "bandage", "backpack", "rope" }), "Repeated restore lost or duplicated inventory.");
                    Check(client.DroppedItems == 0 && client.Warps == 0, "Life preparation dropped items or started a competing warp.");
                    Check(client.Curse == 0.125f && client.Hunger == 0.25f, "Revive penalties overwrote saved statuses.");
                }
            }

            Check(reviveCalls == (snapshot.dead ? 0 : 2), "A dead save was revived, or living clients were not synchronized on repeated loads.");
        }
    }

    private static void VerifyFailuresStopRestoration()
    {
        List<string> called = new();
        ExpectFailure(() => PlayerRestoration.ApplyBeforeWarp(null,
            (_, _) => called.Add("life"), () => called.Add("flags"),
            () => called.Add("inventory"), () => called.Add("snapshot")));
        Check(called.Count == 0, "Invalid snapshot mutated the player.");

        ExpectFailure(() => PlayerRestoration.ApplyBeforeWarp(State(0),
            (_, _) => throw new InvalidOperationException("transport failure"), () => called.Add("flags"),
            () => called.Add("inventory"), () => called.Add("snapshot")));
        Check(called.Count == 0, "Failed life synchronization continued with inventory.");

        ExpectFailure(() => PlayerRestoration.ApplyBeforeWarp(State(0),
            (_, _) => called.Add("life"), () => called.Add("flags"),
            () => throw new InvalidOperationException("missing backpack prefab"), () => called.Add("snapshot")));
        Check(called.SequenceEqual(new[] { "life", "flags" }), "Inventory failure was swallowed or later restoration continued.");
    }

    private static void VerifyStatusReplay()
    {
        float[] caps = { 1f, 2f, 2f, 0f }; // injury, hunger, curse, derived weight
        bool[] scaled = { true, false, true, false };
        float[] saved = { 0.25f, 0.375f, 0.125f, 0.5f };
        foreach (float multiplier in new[] { 0.5f, 1f, 1.5f, 2f, 3f })
        foreach (float initial in new[] { 0f, 0.5f, 2f })
        {
            PlayerRestoration.BuildStatusReplay(caps, scaled, saved, multiplier, out float[] clear, out float[] replay);
            float[] current = { Math.Min(initial, caps[0]), initial, initial, 0.75f };
            for (int load = 0; load < 2; load++)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    if (caps[i] > 0)
                    {
                        current[i] = Math.Clamp(current[i] + clear[i], 0f, caps[i]);
                        current[i] = Math.Clamp(current[i] + replay[i] * (scaled[i] ? multiplier : 1f), 0f, caps[i]);
                        Check(Math.Abs(current[i] - saved[i]) < 0.00001f, "Native delta replay accumulated or reapplied ascent scaling.");
                    }
                    else
                    {
                        Check(clear[i] == 0 && replay[i] == 0 && current[i] == 0.75f, "Derived weight/physical statuses must not be replayed as damage.");
                    }
                }
            }
        }

        ExpectFailure(() => PlayerRestoration.BuildStatusReplay(caps, scaled, saved, 0f, out _, out _));
    }

    private static CharacterSnapshot State(int bits) => new()
    {
        dead = (bits & 1) != 0,
        passedOut = (bits & 2) != 0,
        fullyPassedOut = (bits & 4) != 0
    };

    private static void VerifyPetrifyReplay()
    {
        Check(PlayerRestoration.GetSavedPetrify(null) == 0, "Omitted saved petrify must clear the current death-causing petrification.");
        for (int percent = 0; percent <= 100; percent++)
        {
            CharacterStatusSnapshot status = new() { statusType = "Petrify", amount = percent * 0.01f };
            Check(PlayerRestoration.GetSavedPetrify(new[] { status }) == percent, "Petrify percentage changed on restore.");
        }
    }

    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected restore failure did not propagate.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Scout(string Name, int Actor);

    // A deliberately small stand-in for the documented native RPC contract,
    // not a Unity or Photon integration test. Interface IL is checked separately.
    private sealed class NativeScout
    {
        internal bool Dead, PassedOut, FullyPassedOut, CollisionEnabled;
        internal float DeathTimer = 1f, FallSeconds = 3f, Curse = 1f, Hunger = 1f;
        internal string[] Inventory = { "old item" };
        internal int DroppedItems { get; private set; }
        internal int Warps { get; private set; }

        internal NativeScout(CharacterSnapshot state)
        {
            Dead = state.dead;
            PassedOut = state.passedOut;
            FullyPassedOut = state.fullyPassedOut;
            CollisionEnabled = !state.dead;
        }

        internal void ReceiveNativeLifeRpc(string rpc, object[] arguments)
        {
            if (rpc == "ReviveCharacter")
            {
                Dead = PassedOut = FullyPassedOut = false;
                CollisionEnabled = true;
                DeathTimer = FallSeconds = 0f;
                if ((bool)arguments[0]) { Curse += 0.5f; Hunger += 0.3f; }
            }
            else if (rpc == "RPCA_SetDead")
            {
                Dead = PassedOut = FullyPassedOut = true;
                CollisionEnabled = false;
                DeathTimer = 1f;
            }
            else if (rpc == "RPCA_ReviveAtPosition")
            {
                DroppedItems += Inventory.Length;
                Inventory = Array.Empty<string>();
                Warps++;
            }
            else throw new InvalidOperationException("Unsupported native RPC: " + rpc);
        }
    }
}
