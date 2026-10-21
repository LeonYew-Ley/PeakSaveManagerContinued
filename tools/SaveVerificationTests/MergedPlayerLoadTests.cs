using PEAKSaveManager;

internal static class MergedPlayerLoadTests
{
    internal static void Run()
    {
        for (int savedBits = 0; savedBits < 8; savedBits++)
        for (int hostDead = 0; hostDead < 2; hostDead++)
        for (int ownerDead = 0; ownerDead < 2; ownerDead++)
        {
            CharacterSnapshot saved = new()
            {
                dead = (savedBits & 1) != 0,
                passedOut = (savedBits & 2) != 0,
                fullyPassedOut = (savedBits & 4) != 0
            };
            Target[] clients = { new(saved, hostDead != 0), new(saved, ownerDead != 0) };
            for (int load = 0; load < 2; load++)
            {
                PlayerRestoration.ApplyBeforeWarp(saved,
                    (rpc, arguments) =>
                    {
                        foreach (Target client in clients)
                        {
                            Check(rpc == (saved.dead ? "RPCA_SetDead" : "ReviveCharacter"), "Unexpected life RPC for saved state.");
                            if (!saved.dead) Check(arguments[0] is false, "Revive penalties must be disabled.");
                            client.Dead = saved.dead;
                            client.Collision = !saved.dead;
                        }
                    },
                    () => { foreach (Target client in clients) client.ApplySavedFlags(); },
                    () => { foreach (Target client in clients) client.Inventory = new[] { "backpack", "rope", "bandage" }; },
                    () => { foreach (Target client in clients) client.CheckLifeBeforeWarp(); });

                foreach (Target client in clients)
                {
                    using IDisposable cleanup = (IDisposable)PlayerLoadRestoration.Restore(client);
                    System.Collections.IEnumerator restore = (System.Collections.IEnumerator)cleanup;
                    Check(restore.MoveNext(), "Owner must wait for native warp completion.");
                    Check(client.LifeRestores == load, "Saved life state was finalized before warp completion.");
                    // The real native warp enables collisions even for a dead scout.
                    client.Collision = true;
                    client.IsWarping = false;
                    client.WarpCompleted = client.IsAtDestination = true;
                    Check(!restore.MoveNext(), "Owner did not finish after native completion signals.");
                    client.CheckLifeBeforeWarp();
                    Check(client.Collision == !saved.dead, "Native warp overwrote saved dead/alive collision state.");
                    Check(client.LifeRestores == load + 1 && client.Succeeded, "Owner did not finalize life state before confirming success.");
                    Check(client.Inventory.SequenceEqual(new[] { "backpack", "rope", "bandage" }), "Combined restore duplicated or lost inventory.");
                }
            }
        }
        Console.WriteLine("32 merged life/warp scenarios (each loaded twice) passed using both production coordinators. Native physics and transport were simulated.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Target : IPlayerLoadTarget
    {
        private readonly CharacterSnapshot saved;
        internal bool Dead, PassedOut, FullyPassedOut, Collision;
        internal bool Succeeded;
        internal int LifeRestores;
        internal string[] Inventory = { "old inventory" };
        internal Target(CharacterSnapshot saved, bool dead) { this.saved = saved; Dead = dead; Collision = !dead; }
        public bool IsAvailable => true;
        public float Realtime => 0f;
        public bool IsWarping { get; set; }
        public bool WarpCompleted { get; set; }
        public bool IsAtDestination { get; set; }
        public bool IsEquipmentReady => true;
        internal void ApplySavedFlags() { Dead = saved.dead; PassedOut = saved.passedOut; FullyPassedOut = saved.fullyPassedOut; }
        internal void CheckLifeBeforeWarp() => Check(Dead == saved.dead && PassedOut == saved.passedOut
            && FullyPassedOut == saved.fullyPassedOut, "Saved life flags were lost during combined restoration.");
        public void ClearHeldItem() { }
        public void ApplyLook() { }
        public void BeginWarp() { IsWarping = true; WarpCompleted = IsAtDestination = false; Succeeded = false; }
        public void RestoreLifeState() { ApplySavedFlags(); Collision = !saved.dead; LifeRestores++; }
        public void HaltMotion() { }
        public void EquipSavedSlot() => Check(Collision == !saved.dead, "Equipment ran before saved collision state was restored.");
        public void Finish(bool succeeded) => Succeeded = succeeded;
    }
}
