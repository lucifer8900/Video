using System;
using System.Collections.Generic;

namespace Lingmai.RedMist
{
    public sealed class CinematicCueDefinition
    {
        public string CueId { get; }
        public string FileName { get; }

        public CinematicCueDefinition(string cueId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(cueId)) throw new ArgumentException("A cue id is required.", nameof(cueId));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A media file is required.", nameof(fileName));
            CueId = cueId;
            FileName = fileName;
        }
    }

    /// <summary>
    /// Local-only cinematic registry. Story code refers to stable cue ids while delivery file
    /// names remain centralized here for validation and later replacement.
    /// </summary>
    public static class CinematicCatalog
    {
        public const string GateArrival = "fmv_gate_arrival";
        public const string CelestialFlight = "fmv_celestial_flight";
        public const string HerbCourtyard = "fmv_herb_courtyard";
        public const string SwordVault = "fmv_sword_vault";

        private static readonly Dictionary<string, CinematicCueDefinition> Cues =
            new Dictionary<string, CinematicCueDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { GateArrival, new CinematicCueDefinition(GateArrival, "fmv_gate_arrival.mp4") },
                { CelestialFlight, new CinematicCueDefinition(CelestialFlight, "fmv_celestial_flight.mp4") },
                { HerbCourtyard, new CinematicCueDefinition(HerbCourtyard, "fmv_herb_courtyard.mp4") },
                { SwordVault, new CinematicCueDefinition(SwordVault, "fmv_sword_vault.mp4") }
            };

        private static readonly Dictionary<string, string> NodeIntros =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "prologue", GateArrival },
                { "flight", CelestialFlight },
                { "herb_route", HerbCourtyard },
                // Only a successful or bypassed seal reaches this establishing shot. A failed
                // formation branches directly into the emergency combat path and never shows it.
                { "underground", SwordVault }
            };

        public static bool TryGetCue(string cueId, out CinematicCueDefinition cue)
        {
            cue = null;
            return !string.IsNullOrWhiteSpace(cueId) && Cues.TryGetValue(cueId, out cue);
        }

        public static bool TryGetNodeIntro(string nodeId, out CinematicCueDefinition cue)
        {
            cue = null;
            return !string.IsNullOrWhiteSpace(nodeId) &&
                   NodeIntros.TryGetValue(nodeId, out string cueId) &&
                   TryGetCue(cueId, out cue);
        }

        public static string GetNodeIntroCueId(string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) && NodeIntros.TryGetValue(nodeId, out string cueId)
                ? cueId
                : null;
        }
    }
}
