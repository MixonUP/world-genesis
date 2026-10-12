using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WorldGenesis.Runtime;
using WorldGenesis.Simulation;

namespace WorldGenesis.Tests
{
    public sealed class CitizenWardrobeTests
    {
        private static UnitData Citizen(int race, int appearance, UnitProfession profession)
        {
            return new UnitData { Id = 91, Species = (SpeciesType)race,
                Sex = appearance == 1 || appearance == 3 ? Sex.Female : Sex.Male,
                Age = appearance >= 2 ? 10 : 32, Profession = profession,
                Name = "Wardrobe fixture", Health = 100, MaxHealth = 100 };
        }

        private static Color32[] Layer(SpeciesType race, WardrobeAppearance appearance, UnitProfession profession,
            WardrobeFacing facing, int frame = 0, bool front = true, bool ruler = false)
        {
            var result = new Color32[CitizenWardrobe.Size * CitizenWardrobe.Size];
            CitizenWardrobe.Paint(result, race, appearance, profession, facing, frame, front, ruler);
            return result;
        }

        private static string Silhouette(Color32[] front, Color32[] back)
        {
            var chars = new char[front.Length];
            for (int i = 0; i < chars.Length; i++) chars[i] = front[i].a > 0 || back[i].a > 0 ? '1' : '0';
            return new string(chars);
        }

        [Test]
        public void AllSixteenRaceAndDemographicKeysMatchSavedIdentity()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
            {
                var unit = Citizen(race, appearance, UnitProfession.Farmer);
                WardrobeKey key = CitizenWardrobe.Resolve(unit);
                Assert.That(key.IsValid, Is.True);
                Assert.That(key.Race, Is.EqualTo((SpeciesType)race));
                Assert.That(key.Appearance, Is.EqualTo((WardrobeAppearance)appearance));
                Assert.That(key.Costume, Is.EqualTo(appearance < 2 ? UnitProfession.Farmer : UnitProfession.None));
            }
        }

        [Test]
        public void FullProfessionMatrixHasFiftySixCivilianSixtyFourChildAndEightGuardCases()
        {
            int adult = 0, child = 0, guard = 0;
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
            for (int profession = 0; profession < 8; profession++)
            {
                WardrobeKey key = CitizenWardrobe.Resolve(Citizen(race, appearance, (UnitProfession)profession));
                if (appearance >= 2) { child++; Assert.That(key.Costume, Is.EqualTo(UnitProfession.None)); }
                else if (profession == 3) { guard++; Assert.That(key.Costume, Is.EqualTo(UnitProfession.None)); }
                else { adult++; Assert.That(key.Costume, Is.EqualTo((UnitProfession)profession)); }
            }
            Assert.That(adult, Is.EqualTo(56)); Assert.That(child, Is.EqualTo(64)); Assert.That(guard, Is.EqualTo(8));
        }

        [Test]
        public void SixteenYearBoundaryChangesAppearanceWithoutInventingProfession()
        {
            foreach (float age in new[] { 0f, 11.999f, 12f, 15.999f, 16f, 60f })
            for (int race = 0; race < 4; race++) for (int sex = 0; sex < 2; sex++)
            {
                var unit = Citizen(race, sex == 0 ? 1 : 0, UnitProfession.Carpenter);
                unit.Age = age; unit.Sex = (Sex)sex;
                WardrobeKey key = CitizenWardrobe.Resolve(unit);
                Assert.That((int)key.Appearance >= 2, Is.EqualTo(age < 16));
                Assert.That(key.Costume, Is.EqualTo(age < 16 ? UnitProfession.None : UnitProfession.Carpenter));
                Assert.That(unit.Profession, Is.EqualTo(UnitProfession.Carpenter));
            }
        }

        [Test]
        public void EveryAdultCostumeHasASeparateOutlineInEveryFacing()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 2; appearance++)
            for (int facing = 0; facing < 4; facing++)
            {
                var silhouettes = new HashSet<string>();
                for (int profession = 0; profession < 8; profession++)
                {
                    if (profession == 3) continue;
                    var front = Layer((SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)profession, (WardrobeFacing)facing);
                    var back = Layer((SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)profession, (WardrobeFacing)facing, front: false);
                    Assert.That(silhouettes.Add(Silhouette(front, back)), Is.True,
                        "Same outline: race=" + race + " appearance=" + appearance + " facing=" + facing + " profession=" + profession);
                }
            }
        }

        [Test]
        public void ChildrenIgnoreAllProfessionsAndOfficeDecoration()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 2; appearance < 4; appearance++)
            for (int facing = 0; facing < 4; facing++) for (int profession = 0; profession < 8; profession++)
            {
                var baseline = Layer((SpeciesType)race, (WardrobeAppearance)appearance, UnitProfession.None, (WardrobeFacing)facing);
                var actual = Layer((SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)profession, (WardrobeFacing)facing, ruler: true);
                CollectionAssert.AreEqual(baseline, actual);
                for (int p = 0; p < actual.Length; p++) Assert.That(actual[p].a, Is.Zero, "Original child clothes must remain untouched.");
                var back = Layer((SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)profession, (WardrobeFacing)facing, front: false, ruler: true);
                for (int p = 0; p < back.Length; p++) Assert.That(back[p].a, Is.Zero);
            }
        }

        [Test]
        public void GuardAlwaysPaintsSafeCivilianFallback()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 2; appearance++)
            for (int facing = 0; facing < 4; facing++) for (int layer = 0; layer < 2; layer++)
                CollectionAssert.AreEqual(
                    Layer((SpeciesType)race, (WardrobeAppearance)appearance, UnitProfession.None, (WardrobeFacing)facing, front: layer == 1),
                    Layer((SpeciesType)race, (WardrobeAppearance)appearance, UnitProfession.Guard, (WardrobeFacing)facing, front: layer == 1));
        }

        [Test]
        public void UnassignedAndReservedGuardNeverPaintOverOriginalOutfits()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
            for (int facing = 0; facing < 4; facing++) for (int frame = 0; frame < 4; frame++)
            for (int layer = 0; layer < 2; layer++) foreach (var profession in new[] { UnitProfession.None, UnitProfession.Guard })
            {
                var pixels = Layer((SpeciesType)race, (WardrobeAppearance)appearance, profession, (WardrobeFacing)facing, frame, layer == 1, true);
                for (int p = 0; p < pixels.Length; p++) Assert.That(pixels[p].a, Is.Zero);
            }
        }

        [Test]
        public void TaskAndEquipmentDoNotIssueOrChangeClothingState()
        {
            var unit = Citizen(2, 1, UnitProfession.Hunter);
            WardrobeKey initial = CitizenWardrobe.Resolve(unit);
            for (int task = 0; task <= (int)UnitTask.BuildTransport; task++)
            {
                unit.Task = (UnitTask)task;
                unit.HuntingToolLevel = task % 2; unit.BasketLevel = task % 4;
                unit.EquipmentOrder = task % 7; unit.HasWagon = task % 2 == 0;
                unit.CargoKind = ItemType.IronOre; unit.CargoAmount = .5f;
                string before = JsonUtility.ToJson(unit);
                WardrobeKey result = CitizenWardrobe.Resolve(unit);
                CitizenWardrobe.Paint(new Color32[1024], result.Race, result.Appearance, result.Costume, WardrobeFacing.South, 2, true);
                Assert.That(result.Costume, Is.EqualTo(initial.Costume));
                Assert.That(result.Appearance, Is.EqualTo(initial.Appearance));
                Assert.That(JsonUtility.ToJson(unit), Is.EqualTo(before), "Presentation changed simulation state.");
            }
        }

        [Test]
        public void RepeatedPaintIsDeterministicAndReplacesDirtyDestination()
        {
            var reused = new Color32[1024];
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
            for (int facing = 0; facing < 4; facing++) for (int frame = 0; frame < 4; frame++)
            {
                for (int p = 0; p < reused.Length; p++) reused[p] = new Color32(255, 0, 255, 255);
                var first = Layer((SpeciesType)race, (WardrobeAppearance)appearance, UnitProfession.Merchant, (WardrobeFacing)facing, frame);
                CitizenWardrobe.Paint(reused, (SpeciesType)race, (WardrobeAppearance)appearance, UnitProfession.Merchant, (WardrobeFacing)facing, frame, true);
                CollectionAssert.AreEqual(first, reused);
            }
        }

        [Test]
        public void FaceAndEarBandAndFootPivotStayFreeOfFrontClothing()
        {
            for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 4; appearance++)
            for (int profession = 0; profession < 8; profession++) for (int facing = 0; facing < 4; facing++)
            {
                var pixels = Layer((SpeciesType)race, (WardrobeAppearance)appearance, (UnitProfession)profession, (WardrobeFacing)facing);
                int headStart = appearance >= 2 ? 15 : 20, headEnd = appearance >= 2 ? 25 : 26;
                for (int y = headStart; y <= headEnd; y++) for (int x = 0; x < 32; x++) Assert.That(pixels[y * 32 + x].a, Is.Zero);
                for (int y = 0; y <= 2; y++) for (int x = 0; x < 32; x++) Assert.That(pixels[y * 32 + x].a, Is.Zero);
            }
        }

        [Test]
        public void HeadwearTouchesTheActualHeadAndIgnoresStrayPixels()
        {
            for (int headTop = 23; headTop <= 29; headTop++)
            {
                var body = new Color32[1024];
                var opaque = new Color32(120, 90, 70, 255);
                for (int y = 17; y <= headTop; y++) for (int x = 13; x <= 19; x++) body[y * 32 + x] = opaque;
                body[31 * 32 + 16] = opaque; // Single hair cannot anchor the whole hat.
                for (int x = 1; x <= 6; x++) body[30 * 32 + x] = opaque; // Raised hand outside the head band.
                var original = (Color32[])body.Clone();
                int anchor = CitizenWardrobe.FindHeadTop(body);
                Assert.That(anchor, Is.EqualTo(headTop));
                CollectionAssert.AreEqual(original, body, "Anchor measurement modified the source body.");
                for (int race = 0; race < 4; race++) for (int appearance = 0; appearance < 2; appearance++)
                for (int facing = 0; facing < 4; facing++)
                foreach (var profession in new[] { UnitProfession.Farmer, UnitProfession.Herder })
                {
                    var layer = new Color32[1024];
                    CitizenWardrobe.Paint(layer, (SpeciesType)race, (WardrobeAppearance)appearance,
                        profession, (WardrobeFacing)facing, 0, true, false, anchor);
                    Assert.That(layer[(headTop - 2) * 32 + 16].a, Is.EqualTo(255), "Hat brim must overlap the upper hair.");
                    Assert.That(body[(headTop - 2) * 32 + 16].a, Is.EqualTo(255));
                    for (int y = 20; y < headTop - 2; y++) for (int x = 0; x < 32; x++)
                        Assert.That(layer[y * 32 + x].a, Is.Zero, "Attached hat must leave the lower face free.");
                }
            }
            Assert.That(CitizenWardrobe.FindHeadTop(new Color32[1024]), Is.EqualTo(-1));
            Assert.Throws<ArgumentNullException>(() => CitizenWardrobe.FindHeadTop(null));
            Assert.Throws<ArgumentException>(() => CitizenWardrobe.FindHeadTop(new Color32[1]));
        }

        [Test]
        public void InvalidInputCannotBecomeAVisibleCitizenOrCorruptASmallBuffer()
        {
            Assert.That(CitizenWardrobe.Resolve(null).IsValid, Is.False);
            var animal = Citizen(4, 0, UnitProfession.Farmer);
            Assert.That(CitizenWardrobe.Resolve(animal).IsValid, Is.False);
            var invalid = Layer(SpeciesType.Wolf, WardrobeAppearance.AdultMale, UnitProfession.Farmer, WardrobeFacing.South);
            for (int p = 0; p < invalid.Length; p++) Assert.That(invalid[p].a, Is.Zero);
            CollectionAssert.AreEqual(
                Layer(SpeciesType.Elf, WardrobeAppearance.AdultMale, UnitProfession.None, WardrobeFacing.North),
                Layer(SpeciesType.Elf, WardrobeAppearance.AdultMale, (UnitProfession)99, WardrobeFacing.North));
            Assert.Throws<ArgumentException>(() => CitizenWardrobe.Paint(new Color32[1], SpeciesType.Human, WardrobeAppearance.Boy, UnitProfession.None, WardrobeFacing.South, 0, true));
            Assert.Throws<ArgumentNullException>(() => CitizenWardrobe.Paint(null, SpeciesType.Human, WardrobeAppearance.Boy, UnitProfession.None, WardrobeFacing.South, 0, true));
        }
    }
}

