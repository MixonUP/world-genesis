using System;
using System.Diagnostics;
using WorldGenesis.Simulation;

namespace WorldGenesis.Runtime
{
    /// <summary>Fixed simulation ticks with a wall-clock work budget. Overload slows game time, never increases dt.</summary>
    public sealed class SimulationPacer
    {
        public static readonly int[] Speeds = { 1, 2, 5, 10, 20, 50, 100 };
        public const double FrameBudgetMilliseconds = 12;
        private readonly Func<double> milliseconds;
        private double rateWindow, simulatedWindow;
        public double PendingSeconds { get; private set; }
        public double ActualMultiplier { get; private set; }
        public double LimitedRequestedSeconds { get; private set; }
        public double LastWorkMilliseconds { get; private set; }
        public int LastTickCount { get; private set; }
        public bool BudgetLimited { get; private set; }

        public SimulationPacer(Func<double> clockMilliseconds = null)
        {
            milliseconds = clockMilliseconds ?? (() => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
        }

        public void Reset()
        {
            PendingSeconds = rateWindow = simulatedWindow = ActualMultiplier = LimitedRequestedSeconds = LastWorkMilliseconds = 0;
            LastTickCount = 0; BudgetLimited = false;
        }

        public int Advance(double wallSeconds, float multiplier, Action fixedTick)
        {
            if (double.IsNaN(wallSeconds) || double.IsInfinity(wallSeconds) || wallSeconds < 0 || fixedTick == null) throw new ArgumentOutOfRangeException(nameof(wallSeconds));
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1 || multiplier > 100) throw new ArgumentOutOfRangeException(nameof(multiplier));
            double tick = SimulationEngine.TickSeconds;
            PendingSeconds += wallSeconds * multiplier;
            // A stalled desktop must not create minutes of catch-up. No game state is advanced for this unmet demand.
            double maximumPending = Math.Max(tick * 2, multiplier * .25d);
            if (PendingSeconds > maximumPending) { LimitedRequestedSeconds += PendingSeconds - maximumPending; PendingSeconds = maximumPending; }
            double start = milliseconds(); LastTickCount = 0;
            while (PendingSeconds + .000001d >= tick && LastTickCount < 512)
            {
                if (LastTickCount > 0 && milliseconds() - start >= FrameBudgetMilliseconds) break;
                fixedTick(); PendingSeconds = Math.Max(0, PendingSeconds - tick); LastTickCount++;
            }
            LastWorkMilliseconds = Math.Max(0, milliseconds() - start);
            BudgetLimited = PendingSeconds + .000001d >= tick;
            rateWindow += wallSeconds; simulatedWindow += LastTickCount * tick;
            if (rateWindow >= .5d) { ActualMultiplier = simulatedWindow / rateWindow; rateWindow = simulatedWindow = 0; }
            return LastTickCount;
        }
    }
}
