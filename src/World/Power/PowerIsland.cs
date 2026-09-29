using Godot;
using System.Collections.Generic;
using ZombieApocalypse.Core.Data;

namespace ZombieApocalypse.World.Power;

/// <summary>
/// A connected sub-grid: one generator room plus everything still electrically
/// reachable from it. Each island independently balances generation, battery
/// storage and load demand — this is what makes a severed cable matter.
/// </summary>
public class PowerIsland
{
    public int Id;
    public readonly List<PowerGridNode> Nodes = new();

    // ── Last tick results (read by UI / debug) ───────────────────────
    public float GenerationWatts { get; private set; }
    public float DemandWatts { get; private set; }
    public float SuppliedWatts { get; private set; }
    public float StoredWattHours { get; private set; }
    public float StorageCapacityWattHours { get; private set; }
    public int PoweredLoadCount { get; private set; }
    public int ShedLoadCount { get; private set; }
    public bool HasInverter { get; private set; }
    public bool InverterPowered { get; private set; }
    public float DeficitWatts { get; private set; }

    /// <summary>Island has loads but cannot serve all of them.</summary>
    public bool IsOverloaded => DeficitWatts > 0.5f;

    /// <summary>Island has loads but no generation and no stored energy.</summary>
    public bool IsUnpowered => GenerationWatts <= 0.5f && StoredWattHours <= 0.01f && LoadCount > 0;

    public float BatteryPercent => StorageCapacityWattHours > 0.01f
        ? StoredWattHours / StorageCapacityWattHours
        : 0f;

    private int LoadCount
    {
        get
        {
            int n = 0;
            foreach (var node in Nodes)
                if (node.GetLoadWatts() > 0.01f) n++;
            return n;
        }
    }

    public void AddNode(PowerGridNode node)
    {
        Nodes.Add(node);
        node.IslandId = Id;
    }

    /// <summary>
    /// One simulation step. Allocation follows the load-shedding tiers stored on
    /// each node's <see cref="PowerGridNode.Priority"/>: Critical → Defensive → Utility.
    /// </summary>
    public void Tick(float dtSeconds)
    {
        dtSeconds = Mathf.Max(dtSeconds, 0.0001f);
        float hours = dtSeconds / 3600f;

        // ── 1. Survey the island ─────────────────────────────────────
        float generation = 0f;
        float batteryDischargeWatts = 0f;
        float inverterIdleWatts = 0f;

        foreach (var node in Nodes)
        {
            if (node.IsStorage)
            {
                // Batteries are a buffer, never counted as "generation"
                batteryDischargeWatts += node.GetSupplyWatts();
            }
            else
            {
                generation += node.GetSupplyWatts();
            }

            if (node is Storage.Inverter) inverterIdleWatts += node.GetLoadWatts();
            if (node is Storage.Inverter) HasInverter = true;
        }

        GenerationWatts = generation;

        // ── 2. Power the inverter first: it is the AC gate ───────────
        InverterPowered = false;
        if (HasInverter)
        {
            foreach (var node in Nodes)
            {
                if (node is not Storage.Inverter inverter) continue;
                bool ok = generation + batteryDischargeWatts >= inverter.GetLoadWatts();
                inverter.SetPowered(ok);
                InverterPowered |= ok;
            }
        }

        // ── 3. Priority-based load allocation ────────────────────────
        float budget = generation + batteryDischargeWatts;
        float supplied = 0f;
        float demand = inverterIdleWatts;
        PoweredLoadCount = 0;
        ShedLoadCount = 0;

        if (inverterIdleWatts > 0.01f)
        {
            if (InverterPowered)
            {
                budget -= inverterIdleWatts;
                supplied += inverterIdleWatts;
                PoweredLoadCount++;
            }
            else
            {
                ShedLoadCount++;
            }
        }

        var tiers = new[] { PowerPriority.Critical, PowerPriority.Defensive, PowerPriority.Utility };

        foreach (var tier in tiers)
        {
            foreach (var node in Nodes)
            {
                float load = node.GetLoadWatts();
                if (load <= 0.01f) continue;
                if (node.Priority != tier) continue;
                if (node is Storage.Inverter) continue;

                demand += load;

                // AC-only hardware needs a powered inverter inside the island
                if (node.RequiresInverter && !InverterPowered)
                {
                    node.SetPowered(false);
                    ShedLoadCount++;
                    continue;
                }

                if (budget >= load)
                {
                    budget -= load;
                    supplied += load;
                    node.SetPowered(true);
                    PoweredLoadCount++;
                }
                else
                {
                    node.SetPowered(false);
                    ShedLoadCount++;
                }
            }
        }

        DemandWatts = demand;
        SuppliedWatts = supplied;
        DeficitWatts = Mathf.Max(0f, demand - supplied);

        // ── 4. Battery accounting ────────────────────────────────────
        var batteries = new List<Storage.BatteryBank>();
        foreach (var node in Nodes)
            if (node is Storage.BatteryBank bank) batteries.Add(bank);

        if (batteries.Count > 0)
        {
            float drawn = Mathf.Max(0f, supplied - generation);
            float surplus = Mathf.Max(0f, generation - supplied);

            if (drawn > 0.01f) DrainBatteries(batteries, drawn, hours);
            if (surplus > 0.01f) ChargeBatteries(batteries, surplus, hours);
        }

        StoredWattHours = 0f;
        StorageCapacityWattHours = 0f;
        foreach (var bank in batteries)
        {
            StoredWattHours += bank.StoredWattHours;
            StorageCapacityWattHours += bank.StorageCapacityWattHours;
        }
    }

    private static void DrainBatteries(List<Storage.BatteryBank> batteries, float watts, float hours)
    {
        float available = 0f;
        foreach (var b in batteries) available += b.GetSupplyWatts();
        if (available <= 0.01f) return;

        float scale = Mathf.Min(1f, watts / available);
        foreach (var b in batteries)
            b.DrawWattHours(b.GetSupplyWatts() * scale * hours);
    }

    private static void ChargeBatteries(List<Storage.BatteryBank> batteries, float watts, float hours)
    {
        float headroom = 0f;
        foreach (var b in batteries) headroom += b.MaxChargeRateWatts;
        if (headroom <= 0.01f) return;

        float scale = Mathf.Min(1f, watts / headroom);
        foreach (var b in batteries)
            b.AddWattHours(b.MaxChargeRateWatts * scale * hours);
    }
}
