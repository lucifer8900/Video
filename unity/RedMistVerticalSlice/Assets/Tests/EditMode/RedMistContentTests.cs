using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class RedMistContentTests
    {
        [Test]
        public void CatalogHasCompleteDualRouteSlice()
        {
            Assert.AreEqual(14, StoryCatalog.Nodes.Count);
            foreach (StoryNode node in StoryCatalog.Nodes.Values)
            {
                Assert.IsNotEmpty(node.maleText, node.id + " male text");
                Assert.IsNotEmpty(node.femaleText, node.id + " female text");
                Assert.Greater(node.estimatedMinutes, 0, node.id + " duration");
            }
        }

        [Test]
        public void RoutesHaveDistinctAuthoredPerspective()
        {
            int identical = 0;
            foreach (StoryNode node in StoryCatalog.Nodes.Values)
            {
                if (node.maleText == node.femaleText) identical++;
            }
            Assert.Zero(identical, "Every node must preserve the two protagonists' distinct responsibilities and information.");
        }

        [Test]
        public void StateClampPreservesRuntimeBounds()
        {
            GameState state = new GameState
            {
                mana = -5,
                health = 150,
                divineSense = 300,
                exposure = 50,
                dragonHealth = -2
            };
            state.Clamp();
            Assert.AreEqual(0, state.mana);
            Assert.AreEqual(100, state.health);
            Assert.AreEqual(100, state.divineSense);
            Assert.AreEqual(10, state.exposure);
            Assert.AreEqual(0, state.dragonHealth);
        }

        [Test]
        public void CinematicCatalogMapsAuthoredNodeIntrosToCanonicalFiles()
        {
            AssertNodeIntro("prologue", CinematicCatalog.GateArrival, "fmv_gate_arrival.mp4");
            AssertNodeIntro("flight", CinematicCatalog.CelestialFlight, "fmv_celestial_flight.mp4");
            AssertNodeIntro("herb_route", CinematicCatalog.HerbCourtyard, "fmv_herb_courtyard.mp4");
            AssertNodeIntro("underground", CinematicCatalog.SwordVault, "fmv_sword_vault.mp4");

            Assert.IsNull(CinematicCatalog.GetNodeIntroCueId("camp"), "Nodes without an authored FMV must use the still/3D fallback.");
            Assert.IsFalse(CinematicCatalog.TryGetNodeIntro("camp", out _));
            Assert.IsNull(CinematicCatalog.GetNodeIntroCueId("formation"), "The sword vault FMV belongs after a successful formation entry, not before the puzzle.");
        }

        [Test]
        public void StoryNodesUseTheUnifiedImmortalWorldPlates()
        {
            Assert.AreEqual(GeneratedArtCatalog.SanctuaryEntrance, GeneratedArtCatalog.SceneForNode("prologue"));
            Assert.AreEqual(GeneratedArtCatalog.CelestialStormRoute, GeneratedArtCatalog.SceneForNode("flight"));
            Assert.AreEqual(GeneratedArtCatalog.HerbCourtyard, GeneratedArtCatalog.SceneForNode("herb_route"));
            Assert.AreEqual(GeneratedArtCatalog.CelestialFormationHall, GeneratedArtCatalog.SceneForNode("formation"));
            Assert.AreEqual(GeneratedArtCatalog.SwordSealVault, GeneratedArtCatalog.SceneForNode("underground"));
            Assert.AreEqual(GeneratedArtCatalog.AllianceShenGuFirstFrame,
                GeneratedArtCatalog.SceneForNode("alliance", PlayerRoute.ShenYan));
            Assert.AreEqual(GeneratedArtCatalog.AllianceChuPetitionersFirstFrame,
                GeneratedArtCatalog.SceneForNode("alliance", PlayerRoute.ChuMingqi));
        }

        private static void AssertNodeIntro(string nodeId, string expectedCueId, string expectedFileName)
        {
            Assert.AreEqual(expectedCueId, CinematicCatalog.GetNodeIntroCueId(nodeId), nodeId + " cue id");
            Assert.IsTrue(CinematicCatalog.TryGetNodeIntro(nodeId, out CinematicCueDefinition definition), nodeId + " definition");
            Assert.AreEqual(expectedCueId, definition.CueId, nodeId + " definition cue id");
            Assert.AreEqual(expectedFileName, definition.FileName, nodeId + " media file");
        }
    }
}
