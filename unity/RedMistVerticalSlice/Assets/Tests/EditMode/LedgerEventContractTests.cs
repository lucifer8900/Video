using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerEventContractTests
    {
        [Test]
        public void StrictCodecRoundTripsAllLedgerFieldsDeterministically()
        {
            var source = new LedgerEvent(
                "led.roundtrip", "p.8842", LedgerEventType.ItemGained,
                new[] { "char.hero" }, 3, "chapter.red_mist", 1440,
                "rescue", new[] { "fact.item_found" },
                new Dictionary<string, object>
                {
                    { "quantity", 2 },
                    { "itemRef", "item.spirit_herb" }
                });

            string first = LedgerEventCodec.Encode(source);
            string second = LedgerEventCodec.Encode(source);
            LedgerEvent decoded = LedgerEventCodec.Decode(first);

            Assert.AreEqual(first, second);
            StringAssert.Contains("\"schemaVersion\":\"1.0.0\"", first);
            Assert.Less(first.IndexOf("itemRef", StringComparison.Ordinal),
                first.IndexOf("quantity", StringComparison.Ordinal));
            Assert.AreEqual(source.EntryId, decoded.EntryId);
            Assert.AreEqual(source.PlayerId, decoded.PlayerId);
            Assert.AreEqual(source.Type, decoded.Type);
            CollectionAssert.AreEqual(source.Actors, decoded.Actors);
            Assert.AreEqual(source.Severity, decoded.Severity);
            Assert.AreEqual(source.Chapter, decoded.Chapter);
            Assert.AreEqual(source.WorldClock, decoded.WorldClock);
            Assert.AreEqual(source.SourceNodeId, decoded.SourceNodeId);
            CollectionAssert.AreEqual(source.FactRefs, decoded.FactRefs);
            Assert.AreEqual("item.spirit_herb", decoded.Payload["itemRef"]);
            Assert.AreEqual(2m, decoded.Payload["quantity"]);
        }

        [Test]
        public void AllInitialLedgerTypesHaveStableWireNames()
        {
            string[] expected =
            {
                "debt_incurred", "debt_repaid", "secret_exposed", "promise_made",
                "promise_broken", "npc_rescued", "npc_abandoned", "enemy_spared",
                "item_gained", "quest_expired", "trump_card_revealed"
            };

            var actual = new List<string>();
            foreach (LedgerEventType type in Enum.GetValues(typeof(LedgerEventType)))
            {
                actual.Add(LedgerEventCodec.ToWireType(type));
            }

            CollectionAssert.AreEqual(expected, actual);
        }

        [TestCase("", "p.1")]
        [TestCase("bad id", "p.1")]
        [TestCase("led.1", "")]
        [TestCase("led.1", "player/1")]
        public void InvalidIdempotencyKeyPartsAreRejected(string entryId, string playerId)
        {
            Assert.Throws<ArgumentException>(() => Event(entryId, playerId));
        }

        [TestCase(0)]
        [TestCase(6)]
        public void SeverityOutsideOneThroughFiveIsRejected(int severity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerEvent(
                "led.severity", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero", "npc.enemy" }, severity, "red_mist", 1,
                "node.test", new[] { "fact.enemy_alive" }, EmptyPayload()));
        }

        [Test]
        public void NegativeClockDuplicateRefsAndOversizedCollectionsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerEvent(
                "led.clock", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero" }, 2, "red_mist", -1,
                "node.test", Array.Empty<string>(), EmptyPayload()));
            Assert.Throws<ArgumentException>(() => new LedgerEvent(
                "led.actors", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero", "char.hero" }, 2, "red_mist", 1,
                "node.test", Array.Empty<string>(), EmptyPayload()));
            Assert.Throws<ArgumentException>(() => new LedgerEvent(
                "led.facts", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero" }, 2, "red_mist", 1,
                "node.test", new[] { "fact.a", "fact.a" }, EmptyPayload()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerEvent(
                "led.actor_count", "p.1", LedgerEventType.EnemySpared,
                Repeat("char.actor", 9), 2, "red_mist", 1,
                "node.test", Array.Empty<string>(), EmptyPayload()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerEvent(
                "led.fact_count", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero" }, 2, "red_mist", 1,
                "node.test", Repeat("fact.value", 17), EmptyPayload()));
        }

        [Test]
        public void DuplicateReferenceSemanticsAreOrdinal()
        {
            var item = new LedgerEvent(
                "led.ordinal", "p.1", LedgerEventType.EnemySpared,
                new[] { "char.hero", "CHAR.HERO" }, 2, "red_mist", 1,
                "rescue", new[] { "fact.a", "fact.A" }, EmptyPayload());

            Assert.AreEqual(2, item.Actors.Count);
            Assert.AreEqual(2, item.FactRefs.Count);
        }

        [Test]
        public void PayloadBoundariesAndUnknownJsonPropertiesAreRejected()
        {
            var tooMany = new Dictionary<string, string>();
            for (int index = 0; index < 17; index++) tooMany.Add("key" + index, "value");
            Assert.Throws<ArgumentOutOfRangeException>(() => Event(
                "led.payload_count", "p.1", LedgerEventType.DebtIncurred, 1, tooMany));
            Assert.Throws<ArgumentOutOfRangeException>(() => Event(
                "led.payload_value", "p.1", LedgerEventType.DebtIncurred, 1,
                new Dictionary<string, string> { { "note", new string('x', 513) } }));
            string valid = LedgerEventCodec.Encode(Event());
            string unknown = valid.Insert(valid.Length - 1, ",\"apiKey\":\"must-not-exist\"");
            Assert.Throws<LedgerContractException>(() => LedgerEventCodec.Decode(unknown));
            Assert.Throws<LedgerContractException>(() => LedgerEventCodec.Decode(
                valid.Replace("\"enemy_spared\"", "\"unknown_type\"")));
        }

        [TestCase(LedgerEventType.DebtIncurred, "debtKind", "spared_life")]
        [TestCase(LedgerEventType.DebtRepaid, "debtRef", "led.prior_debt")]
        [TestCase(LedgerEventType.SecretExposed, "secretRef", "secret.hidden_card")]
        [TestCase(LedgerEventType.PromiseMade, "promiseRef", "promise.return_favor")]
        [TestCase(LedgerEventType.PromiseBroken, "promiseRef", "promise.return_favor")]
        [TestCase(LedgerEventType.NpcRescued, "npcRef", "npc.synthetic")]
        [TestCase(LedgerEventType.NpcAbandoned, "npcRef", "npc.synthetic")]
        [TestCase(LedgerEventType.EnemySpared, "npcRef", "npc.synthetic")]
        [TestCase(LedgerEventType.ItemGained, "itemRef", "item.synthetic")]
        [TestCase(LedgerEventType.QuestExpired, "questRef", "quest.synthetic")]
        [TestCase(LedgerEventType.TrumpCardRevealed, "abilityRef", "ability.synthetic")]
        public void ReviewedPayloadWhitelistAcceptsOnlyTypedReferences(
            LedgerEventType type,
            string key,
            string value)
        {
            LedgerEvent item = EventObjects(
                "led.whitelist", type,
                new Dictionary<string, object> { { key, value } });

            Assert.AreEqual(value, item.Payload[key]);
        }

        [Test]
        public void EmptyPayloadIsLegalForEveryEventType()
        {
            foreach (LedgerEventType type in Enum.GetValues(typeof(LedgerEventType)))
                Assert.IsEmpty(EventObjects(
                    "led.empty_" + LedgerEventCodec.ToWireType(type),
                    type,
                    new Dictionary<string, object>()).Payload);
        }

        [Test]
        public void PayloadWhitelistRejectsUnknownNullFreeTextAndInvalidQuantity()
        {
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.unknown", LedgerEventType.SecretExposed,
                new Dictionary<string, object> { { "unknown", "secret.safe" } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.null", LedgerEventType.SecretExposed,
                new Dictionary<string, object> { { "secretRef", null } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.free_text", LedgerEventType.SecretExposed,
                new Dictionary<string, object> { { "secretRef", "raw free text" } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.wrong_type", LedgerEventType.SecretExposed,
                new Dictionary<string, object> { { "npcRef", "npc.synthetic" } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.bad_token", LedgerEventType.DebtIncurred,
                new Dictionary<string, object> { { "debtKind", "Not_A_Token" } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.quantity_zero", LedgerEventType.ItemGained,
                new Dictionary<string, object> { { "quantity", 0 } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.quantity_fraction", LedgerEventType.ItemGained,
                new Dictionary<string, object> { { "quantity", 1.5d } }));
            Assert.Throws<ArgumentException>(() => EventObjects(
                "led.quantity_large", LedgerEventType.ItemGained,
                new Dictionary<string, object> { { "quantity", 1000000 } }));

            LedgerEvent valid = EventObjects(
                "led.quantity_valid", LedgerEventType.ItemGained,
                new Dictionary<string, object>
                {
                    { "itemRef", "item.synthetic" },
                    { "quantity", 999999 }
                });
            Assert.AreEqual(999999, valid.Payload["quantity"]);
        }

        [TestCase("transcript")]
        [TestCase("RawTranscript")]
        [TestCase("audio")]
        [TestCase("audioClip")]
        [TestCase("DEVICE_ID")]
        [TestCase("emailAddress")]
        [TestCase("localPath")]
        [TestCase("displayText")]
        [TestCase("Display_Text")]
        [TestCase("device_info")]
        [TestCase("Voice_Recording")]
        public void PrivacySensitivePayloadKeysAreRejectedCaseInsensitively(string key)
        {
            Assert.Throws<ArgumentException>(() => Event(
                "led.private", "p.1", LedgerEventType.SecretExposed, 1,
                new Dictionary<string, string> { { key, "must-not-persist" } }));
        }

        [Test]
        public void PayloadAndTotalSerializedEventSizesAreIndependentlyBounded()
        {
            string valid = LedgerEventCodec.Encode(Event("led.size"));
            string oversizedPayload = valid.Replace(
                "\"payload\":{}",
                "\"payload\":{" + new string(' ', 4096) + "}");

            Assert.Throws<LedgerContractException>(() => LedgerEventCodec.Decode(oversizedPayload));
            Assert.Throws<LedgerContractException>(() =>
                LedgerEventCodec.Decode(new string(' ', 16385)));
            Assert.AreEqual(4096, LedgerEventCodec.MaximumPayloadBytes);
            Assert.AreEqual(16384, LedgerEventCodec.MaximumSerializedBytes);
        }

        [TestCase("chapter.red_mist", "rescue")]
        [TestCase("red_mist", "prologue")]
        [TestCase("chapter.qifeng_valley", "node.route_select")]
        public void RuntimeChapterAndSourceNodeIdsArePreservedWithoutInventedPrefixes(
            string chapter,
            string sourceNodeId)
        {
            var item = new LedgerEvent(
                "led.runtime_ids", "p.1", LedgerEventType.ItemGained,
                new[] { "char.hero" }, 1, chapter, 1, sourceNodeId,
                Array.Empty<string>(), EmptyPayload());

            LedgerEvent decoded = LedgerEventCodec.Decode(LedgerEventCodec.Encode(item));

            Assert.AreEqual(chapter, decoded.Chapter);
            Assert.AreEqual(sourceNodeId, decoded.SourceNodeId);
        }

        [Test]
        public void StringRepresentationsNeverContainPayloadValues()
        {
            LedgerEvent item = Event(
                "led.safe_log", "p.1", LedgerEventType.SecretExposed, 1,
                new Dictionary<string, string> { { "secretRef", "secret.super-secret-value" } });

            string text = item.ToString();

            StringAssert.DoesNotContain("led.safe_log", text);
            StringAssert.DoesNotContain("p.1", text);
            StringAssert.DoesNotContain("super-secret-value", text);
            StringAssert.DoesNotContain("secretRef", text);
        }

        private static LedgerEvent Event(
            string entryId = "led.test",
            string playerId = "p.1",
            LedgerEventType type = LedgerEventType.EnemySpared,
            long worldClock = 1,
            IReadOnlyDictionary<string, string> payload = null)
        {
            return new LedgerEvent(
                entryId,
                playerId,
                type,
                new[] { "char.hero", "npc.enemy" },
                3,
                "chapter.red_mist",
                worldClock,
                "rescue",
                new[] { "fact.enemy_alive" },
                payload ?? EmptyPayload());
        }

        private static IReadOnlyDictionary<string, string> EmptyPayload()
        {
            return new Dictionary<string, string>();
        }

        private static LedgerEvent EventObjects(
            string entryId,
            LedgerEventType type,
            IReadOnlyDictionary<string, object> payload)
        {
            return new LedgerEvent(
                entryId, "p.1", type, new[] { "char.hero" }, 2,
                "chapter.red_mist", 1, "rescue", Array.Empty<string>(), payload);
        }

        private static string[] Repeat(string prefix, int count)
        {
            var values = new string[count];
            for (int index = 0; index < count; index++) values[index] = prefix + index;
            return values;
        }
    }
}
