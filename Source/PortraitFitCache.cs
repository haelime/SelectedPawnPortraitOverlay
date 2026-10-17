using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

// Measured bounds per pawn. A pawn is measured again only when its appearance signature changes, at most
// one measurement per frame, and a changed measurement eases in instead of snapping.
public static class PortraitFitCache
{
    private const int MaxEntries = 64;
    private const float TransitionSeconds = 0.3f;

    // Each portrait zoom is its own PortraitsCache texture, so the transition moves in a few steps
    // rather than every frame.
    private const int TransitionSteps = 8;

    // Changes smaller than this fraction of the pawn's size keep the current framing.
    private const float ReframeThreshold = 0.03f;

    private sealed class Entry
    {
        public int Signature;
        public PortraitBounds? Target;
        public PortraitBounds? From;
        public float TransitionStart;
        public int LastUsedFrame;
    }

    private static readonly Dictionary<Pawn, Entry> Entries = new();
    private static int lastMeasureFrame = -1;

    // Bounds to frame the pawn with right now, or null when it has no usable measurement.
    public static PortraitBounds? GetBounds(Pawn pawn, PortraitOverlaySettings settings)
    {
        if (pawn == null || settings == null)
        {
            return null;
        }

        var frame = Time.frameCount;
        var signature = ComputeSignature(pawn, settings);
        if (!Entries.TryGetValue(pawn, out var entry))
        {
            if (lastMeasureFrame == frame)
            {
                return null;
            }

            RemoveStaleEntries();
            lastMeasureFrame = frame;
            entry = new Entry { Signature = signature, Target = Measure(pawn, settings) };
            Entries[pawn] = entry;
        }
        else if (entry.Signature != signature && lastMeasureFrame != frame)
        {
            lastMeasureFrame = frame;
            var displayed = Displayed(entry);
            var measured = Measure(pawn, settings);
            entry.Signature = signature;
            if (measured.HasValue && entry.Target.HasValue && displayed.HasValue)
            {
                if (!PortraitBounds.NearlyEqual(entry.Target.Value, measured.Value, ReframeThreshold))
                {
                    entry.From = displayed;
                    entry.Target = measured;
                    entry.TransitionStart = Time.realtimeSinceStartup;
                }
            }
            else
            {
                entry.From = null;
                entry.Target = measured;
            }
        }

        entry.LastUsedFrame = frame;
        return Displayed(entry);
    }

    // Any measurement of the race, for seeding a per-race override from the settings list.
    public static PortraitBounds? FindBoundsForRace(ThingDef race)
    {
        foreach (var pair in Entries)
        {
            if (pair.Key.def == race && pair.Value.Target.HasValue)
            {
                return pair.Value.Target;
            }
        }

        return null;
    }

    public static void Clear()
    {
        Entries.Clear();
    }

    private static PortraitBounds? Measure(Pawn pawn, PortraitOverlaySettings settings)
    {
        if (!settings.LivePortrait)
        {
            return PortraitBoundsMeasurer.Measure(pawn, Rot4.South, settings);
        }

        // A live portrait turns with the pawn. Framing all facings at once keeps the zoom steady while it
        // walks; west is east mirrored and north matches south closely enough.
        var south = PortraitBoundsMeasurer.Measure(pawn, Rot4.South, settings);
        var east = PortraitBoundsMeasurer.Measure(pawn, Rot4.East, settings);
        if (!east.HasValue)
        {
            return south;
        }

        var sides = east.Value.Union(east.Value.MirroredX());
        return south.HasValue ? south.Value.Union(sides) : sides;
    }

    private static PortraitBounds? Displayed(Entry entry)
    {
        if (!entry.From.HasValue || !entry.Target.HasValue)
        {
            return entry.Target;
        }

        var progress = (Time.realtimeSinceStartup - entry.TransitionStart) / TransitionSeconds;
        if (progress >= 1f)
        {
            entry.From = null;
            return entry.Target;
        }

        var step = Mathf.Floor(Mathf.Clamp01(progress) * TransitionSteps) / TransitionSteps;
        return PortraitBounds.Lerp(entry.From.Value, entry.Target.Value, Mathf.SmoothStep(0f, 1f, step));
    }

    // Everything that changes how large the pawn is drawn in its portrait.
    private static int ComputeSignature(Pawn pawn, PortraitOverlaySettings settings)
    {
        unchecked
        {
            var hash = 17;
            hash = Mix(hash, pawn.def);
            hash = Mix(hash, pawn.ageTracker?.CurLifeStageIndex ?? -1);
            hash = Mix(hash, (int)pawn.gender);
            hash = Mix(hash, pawn.story?.bodyType);
            hash = Mix(hash, pawn.story?.headType);
            hash = Mix(hash, pawn.story?.hairDef);
            hash = Mix(hash, pawn.style?.beardDef);
            hash = Mix(hash, pawn.genes?.GenesListForReading?.Count ?? 0);
            hash = Mix(hash, pawn.health?.State == PawnHealthState.Mobile ? 1 : 0);
            hash = Mix(hash, pawn.health?.hediffSet?.HasHead == true ? 1 : 0);
            hash = Mix(hash, settings.RenderHeadgear ? 1 : 0);
            hash = Mix(hash, settings.RenderApparel ? 1 : 0);
            hash = Mix(hash, settings.LivePortrait ? 1 : 0);

            var apparel = pawn.apparel?.WornApparel;
            if (apparel != null)
            {
                for (var i = 0; i < apparel.Count; i++)
                {
                    hash = Mix(hash, apparel[i]?.def);
                }
            }

            return hash;
        }
    }

    private static int Mix(int hash, object value)
    {
        return Mix(hash, value?.GetHashCode() ?? 0);
    }

    private static int Mix(int hash, int value)
    {
        unchecked
        {
            return (hash * 31) + value;
        }
    }

    private static void RemoveStaleEntries()
    {
        List<Pawn> removed = null;
        foreach (var pair in Entries)
        {
            if (pair.Key.Destroyed)
            {
                (removed ??= new List<Pawn>()).Add(pair.Key);
            }
        }

        if (removed != null)
        {
            foreach (var pawn in removed)
            {
                Entries.Remove(pawn);
            }
        }

        while (Entries.Count >= MaxEntries)
        {
            Pawn leastRecent = null;
            var leastRecentFrame = int.MaxValue;
            foreach (var pair in Entries)
            {
                if (pair.Value.LastUsedFrame < leastRecentFrame)
                {
                    leastRecent = pair.Key;
                    leastRecentFrame = pair.Value.LastUsedFrame;
                }
            }

            Entries.Remove(leastRecent);
        }
    }
}
