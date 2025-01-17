﻿using EngineLayer;
using NUnit.Framework;
using Proteomics;
using System.Collections.Generic;
using System.Linq;

namespace Test
{
    [TestFixture]
    public static class SeqCoverageTest
    {
        #region Public Methods

        [Test]
        public static void TryFailSequenceCoverage()
        {
            var prot1 = new Protein("MMKMMK", "prot1");

            ModificationMotif.TryGetMotif("M", out ModificationMotif motifM);
            ModificationWithMass mod1 = new ModificationWithMass("mod1", "mt", motifM, TerminusLocalization.NProt, 10);
            ModificationWithMass mod2 = new ModificationWithMass("mod2", "mt", motifM, TerminusLocalization.NPep, 10);
            ModificationWithMass mod3 = new ModificationWithMass("mod3", "mt", motifM, TerminusLocalization.Any, 10);
            ModificationMotif.TryGetMotif("K", out ModificationMotif motifK);
            ModificationWithMass mod4 = new ModificationWithMass("mod4", "mt", motifK, TerminusLocalization.PepC, 10);
            ModificationWithMass mod5 = new ModificationWithMass("mod5", "mt", motifK, TerminusLocalization.ProtC, 10);

            Dictionary<int, ModificationWithMass> modsFor1 = new Dictionary<int, ModificationWithMass>
            {
                {1, mod1},
                {3, mod3},
                {5, mod4},
            };
            Dictionary<int, ModificationWithMass> modsFor2 = new Dictionary<int, ModificationWithMass>
            {
                {1, mod2},
                {5, mod5},
            };
            Dictionary<int, ModificationWithMass> modsFor3 = new Dictionary<int, ModificationWithMass>
            {
                {1, mod1},
                {5, mod3},
                {8, mod5}
            };

            var pwsm1 = new PeptideWithSetModifications(0, prot1, 1, 3, modsFor1);
            var pwsm2 = new PeptideWithSetModifications(0, prot1, 4, 6, modsFor2);
            var pwsm3 = new PeptideWithSetModifications(0, prot1, 1, 6, modsFor3);
            HashSet<PeptideWithSetModifications> peptides = new HashSet<PeptideWithSetModifications>
            {
                pwsm1,
                pwsm2,
                pwsm3,
            };

            Dictionary<CompactPeptideBase, HashSet<PeptideWithSetModifications>> matching = new Dictionary<CompactPeptideBase, HashSet<PeptideWithSetModifications>>
            {
                { pwsm1.CompactPeptide(TerminusType.None), new HashSet<PeptideWithSetModifications>{ pwsm1 } },
                { pwsm2.CompactPeptide(TerminusType.None), new HashSet<PeptideWithSetModifications>{ pwsm2 } },
                { pwsm3.CompactPeptide(TerminusType.None), new HashSet<PeptideWithSetModifications>{ pwsm3 } },
            };

            IScan scan = new ThisTestScan();
            var psm1 = new Psm(pwsm1.CompactPeptide(TerminusType.None), 0, 1, 0, scan);
            psm1.SetFdrValues(0, 0, 0, 0, 0, 0);
            psm1.MatchToProteinLinkedPeptides(matching);
            var psm2 = new Psm(pwsm2.CompactPeptide(TerminusType.None), 0, 1, 0, scan);
            psm2.SetFdrValues(0, 0, 0, 0, 0, 0);
            psm2.MatchToProteinLinkedPeptides(matching);
            var psm3 = new Psm(pwsm3.CompactPeptide(TerminusType.None), 0, 1, 0, scan);
            psm3.SetFdrValues(0, 0, 0, 0, 0, 0);
            psm3.MatchToProteinLinkedPeptides(matching);

            List<Psm> newPsms = new List<Psm>
            {
                psm1,
                psm2,
                psm3,
            };

            ProteinParsimonyEngine ppe = new ProteinParsimonyEngine(matching, true, new List<string>());
            ProteinParsimonyResults fjkd = (ProteinParsimonyResults)ppe.Run();

            ProteinScoringAndFdrEngine psafe = new ProteinScoringAndFdrEngine(fjkd.ProteinGroups, newPsms, true, true, true, new List<string>());

            psafe.Run();

            fjkd.ProteinGroups.First().CalculateSequenceCoverage();

            var firstSequenceCoverageDisplayList = fjkd.ProteinGroups.First().SequenceCoverageDisplayList.First();
            Assert.AreEqual("MMKMMK", firstSequenceCoverageDisplayList);
            var firstSequenceCoverageDisplayListWithMods = fjkd.ProteinGroups.First().SequenceCoverageDisplayListWithMods.First();
            Assert.AreEqual("[mod1]-MM[mod3]KM[mod3]MK-[mod5]", firstSequenceCoverageDisplayListWithMods);

            var firstModInfo = fjkd.ProteinGroups.First().ModsInfo.First();
            Assert.IsTrue(firstModInfo.Contains(@"#aa0[mod1,info:occupancy=1.00(2/2)]"));
            Assert.IsTrue(firstModInfo.Contains(@"#aa2[mod3,info:occupancy=0.50(1/2)]"));
            Assert.IsFalse(firstModInfo.Contains(@"#aa3"));
            Assert.IsTrue(firstModInfo.Contains(@"#aa4[mod3,info:occupancy=0.50(1/2)]"));
            Assert.IsFalse(firstModInfo.Contains(@"#aa5"));
            Assert.IsTrue(firstModInfo.Contains(@"#aa7[mod5,info:occupancy=1.00(2/2)]"));
        }

        #endregion Public Methods
    }
}