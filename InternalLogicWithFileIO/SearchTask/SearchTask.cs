using InternalLogicEngineLayer;
using IO.MzML;
using IO.Thermo;
using MassSpectrometry;
using OldInternalLogic;
using Spectra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InternalLogicTaskLayer
{
    public class SearchTask : MyTaskEngine
    {

        #region Public Constructors

        public SearchTask(IEnumerable<ModList> modList, IEnumerable<SearchMode> inputSearchModes)
        {
            // Set default values here:
            classicSearch = true;
            doParsimony = false;
            searchDecoy = true;
            maxMissedCleavages = 2;
            protease = ProteaseDictionary.Instance["trypsin (no proline rule)"];
            maxModificationIsoforms = 4096;
            initiatorMethionineBehavior = InitiatorMethionineBehavior.Variable;
            productMassTolerance = new Tolerance(ToleranceUnit.Absolute, 0.01);
            bIons = true;
            yIons = true;
            listOfModListsForSearch = new List<ModListForSearchTask>();
            foreach (var uu in modList)
                listOfModListsForSearch.Add(new ModListForSearchTask(uu));
            listOfModListsForSearch[0].Fixed = true;
            listOfModListsForSearch[1].Variable = true;
            listOfModListsForSearch[2].Localize = true;

            searchModes = new List<SearchModeFoSearch>();
            foreach (var uu in inputSearchModes)
                searchModes.Add(new SearchModeFoSearch(uu));
            searchModes[0].Use = true;
            this.taskType = MyTaskEnum.Search;
        }

        #endregion Public Constructors

        #region Public Properties

        public bool classicSearch { get; set; }
        public bool doParsimony { get; set; }
        public List<ModListForSearchTask> listOfModListsForSearch { get; set; }
        public bool searchDecoy { get; set; }
        public List<SearchModeFoSearch> searchModes { get; set; }

        #endregion Public Properties

        #region Protected Methods

        protected override string GetSpecificTaskInfo()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("classicSearch: " + classicSearch);
            sb.AppendLine("doParsimony: " + doParsimony);
            sb.AppendLine("Fixed mod lists: " + string.Join(",", listOfModListsForSearch.Where(b => b.Fixed).Select(b => b.FileName)));
            sb.AppendLine("Variable mod lists: " + string.Join(",", listOfModListsForSearch.Where(b => b.Variable).Select(b => b.FileName)));
            sb.AppendLine("Localized mod lists: " + string.Join(",", listOfModListsForSearch.Where(b => b.Localize).Select(b => b.FileName)));
            sb.AppendLine("searchDecoy: " + searchDecoy);
            sb.AppendLine("searchModes: ");
            sb.Append(string.Join(Environment.NewLine, searchModes.Where(b => b.Use).Select(b => "\t" + b.sm)));
            return sb.ToString();
        }

        protected override void ValidateParams()
        {
            foreach (var huh in listOfModListsForSearch)
            {
                if (huh.Fixed && huh.Localize)
                    throw new EngineValidationException("Not allowed to set same modifications to both fixed and localize");
                if (huh.Fixed && huh.Variable)
                    throw new EngineValidationException("Not allowed to set same modifications to both fixed and variable");
                if (huh.Localize && huh.Variable)
                    throw new EngineValidationException("Not allowed to set same modifications to both localize and variable");
            }
        }

        protected override MyResults RunSpecific()
        {
            Dictionary<CompactPeptide, HashSet<PeptideWithSetModifications>> compactPeptideToProteinPeptideMatching = new Dictionary<CompactPeptide, HashSet<PeptideWithSetModifications>>();

            Dictionary<CompactPeptide, PeptideWithSetModifications> fullSequenceToProteinSingleMatch = new Dictionary<CompactPeptide, PeptideWithSetModifications>();

            status("Loading modifications...");
            List<MorpheusModification> variableModifications = listOfModListsForSearch.Where(b => b.Variable).SelectMany(b => b.getMods()).ToList();
            List<MorpheusModification> fixedModifications = listOfModListsForSearch.Where(b => b.Fixed).SelectMany(b => b.getMods()).ToList();
            List<MorpheusModification> localizeableModifications = listOfModListsForSearch.Where(b => b.Localize).SelectMany(b => b.getMods()).ToList();
            Dictionary<string, List<MorpheusModification>> identifiedModsInXML;
            HashSet<string> unidentifiedModStrings;
            GenerateModsFromStrings(xmlDbFilenameList, localizeableModifications, out identifiedModsInXML, out unidentifiedModStrings);

            List<SearchMode> searchModesS = searchModes.Where(b => b.Use).Select(b => b.sm).ToList();

            List<ParentSpectrumMatch>[] allPsms = new List<ParentSpectrumMatch>[searchModesS.Count];
            for (int j = 0; j < searchModesS.Count; j++)
                allPsms[j] = new List<ParentSpectrumMatch>();

            status("Loading proteins...");
            var proteinList = xmlDbFilenameList.SelectMany(b => getProteins(searchDecoy, identifiedModsInXML, b)).ToList();

            List<CompactPeptide> peptideIndex = null;
            Dictionary<float, List<int>> fragmentIndexDict = null;
            float[] keys = null;
            List<int>[] fragmentIndex = null;

            if (!classicSearch)
            {
                status("Getting fragment dictionary...");

                GetPeptideAndFragmentIndices(out peptideIndex, out fragmentIndexDict, listOfModListsForSearch, searchDecoy, variableModifications, fixedModifications, localizeableModifications, proteinList, protease, output_folder);

                keys = fragmentIndexDict.OrderBy(b => b.Key).Select(b => b.Key).ToArray();
                fragmentIndex = fragmentIndexDict.OrderBy(b => b.Key).Select(b => b.Value).ToArray();
            }

            var currentRawFileList = rawDataFilenameList;
            for (int spectraFileIndex = 0; spectraFileIndex < currentRawFileList.Count; spectraFileIndex++)
            {
                var origDataFile = currentRawFileList[spectraFileIndex];
                status("Loading spectra file...");
                IMsDataFile<IMzSpectrum<MzPeak>> myMsDataFile;
                if (Path.GetExtension(origDataFile).Equals(".mzML"))
                    myMsDataFile = new Mzml(origDataFile, 400);
                else
                    myMsDataFile = new ThermoRawFile(origDataFile, 400);
                status("Opening spectra file...");
                myMsDataFile.Open();

                ClassicSearchEngine classicSearchEngine = null;
                ClassicSearchResults classicSearchResults = null;

                ModernSearchEngine modernSearchEngine = null;
                ModernSearchResults modernSearchResults = null;

                if (classicSearch)
                {
                    classicSearchEngine = new ClassicSearchEngine(myMsDataFile, spectraFileIndex, variableModifications, fixedModifications, localizeableModifications, proteinList, productMassTolerance, protease, searchModesS);

                    classicSearchResults = (ClassicSearchResults)classicSearchEngine.Run();
                    for (int i = 0; i < searchModesS.Count; i++)
                        allPsms[i].AddRange(classicSearchResults.outerPsms[i]);

                    AnalysisEngine analysisEngine = new AnalysisEngine(classicSearchResults.outerPsms, compactPeptideToProteinPeptideMatching, proteinList, variableModifications, fixedModifications, localizeableModifications, protease, searchModesS, myMsDataFile, productMassTolerance, (BinTreeStructure myTreeStructure, string s) => WriteTree(myTreeStructure, output_folder, Path.GetFileNameWithoutExtension(origDataFile) + s), (List<NewPsmWithFDR> h, string s) => WriteToTabDelimitedTextFileWithDecoys(h, output_folder, Path.GetFileNameWithoutExtension(origDataFile) + s), doParsimony);

                    AnalysisResults analysisResults = (AnalysisResults)analysisEngine.Run();
                }
                else
                {
                    modernSearchEngine = new ModernSearchEngine(myMsDataFile, spectraFileIndex, peptideIndex, keys, fragmentIndex, variableModifications, fixedModifications, localizeableModifications, proteinList, productMassTolerance.Value, protease, searchModesS);

                    modernSearchResults = (ModernSearchResults)modernSearchEngine.Run();
                    for (int i = 0; i < searchModesS.Count; i++)
                        allPsms[i].AddRange(modernSearchResults.newPsms[i]);

                    AnalysisEngine analysisEngine = new AnalysisEngine(modernSearchResults.newPsms.Select(b => b.ToArray()).ToArray(), compactPeptideToProteinPeptideMatching, proteinList, variableModifications, fixedModifications, localizeableModifications, protease, searchModesS, myMsDataFile, productMassTolerance, (BinTreeStructure myTreeStructure, string s) => WriteTree(myTreeStructure, output_folder, Path.GetFileNameWithoutExtension(origDataFile) + s), (List<NewPsmWithFDR> h, string s) => WriteToTabDelimitedTextFileWithDecoys(h, output_folder, Path.GetFileNameWithoutExtension(origDataFile) + s), doParsimony);

                    AnalysisResults analysisResults = (AnalysisResults)analysisEngine.Run();
                }
            }

            if (currentRawFileList.Count > 1)
            {
                AnalysisEngine analysisEngine = new AnalysisEngine(allPsms.Select(b => b.ToArray()).ToArray(), compactPeptideToProteinPeptideMatching, proteinList, variableModifications, fixedModifications, localizeableModifications, protease, searchModesS, null, productMassTolerance, (BinTreeStructure myTreeStructure, string s) => WriteTree(myTreeStructure, output_folder, "aggregate"), (List<NewPsmWithFDR> h, string s) => WriteToTabDelimitedTextFileWithDecoys(h, output_folder, "aggregate" + s), doParsimony);

                AnalysisResults analysisResults = (AnalysisResults)analysisEngine.Run();
            }
            return new MySearchTaskResults(this);
        }

        #endregion Protected Methods

        #region Private Methods

        private void GetPeptideAndFragmentIndices(out List<CompactPeptide> peptideIndex, out Dictionary<float, List<int>> fragmentIndexDict, List<ModListForSearchTask> collectionOfModLists, bool doFDRanalysis, List<MorpheusModification> variableModifications, List<MorpheusModification> fixedModifications, List<MorpheusModification> localizeableModifications, List<Protein> hm, Protease protease, string output_folder)
        {
            string pathToFolderWithIndices = GetFolderWithIndices(xmlDbFilenameList);

            if (pathToFolderWithIndices == null)
            {
                status("Generating indices...");
                var output_folderForIndices = GetOutputFolderForIndices(xmlDbFilenameList);

                IndexEngine indexEngine = new IndexEngine(hm, variableModifications, fixedModifications, localizeableModifications, protease);
                IndexResults indexResults = (IndexResults)indexEngine.Run();
                peptideIndex = indexResults.peptideIndex;
                fragmentIndexDict = indexResults.fragmentIndexDict;

                status("Writing peptide index...");
                writePeptideIndex(peptideIndex, Path.Combine(output_folderForIndices, "peptideIndex.ind"));
                status("Writing fragment index...");
                writeFragmentIndexNetSerializer(fragmentIndexDict, Path.Combine(output_folderForIndices, "fragmentIndex.ind"));
                status("Writing log...");
                writeIndexEngineLog(indexEngine, Path.Combine(output_folderForIndices, "index.log"));
            }
            else
            {
                status("Reading peptide index...");
                peptideIndex = readPeptideIndex(Path.Combine(pathToFolderWithIndices, "peptideIndex.ind"));
                status("Reading fragment index...");
                fragmentIndexDict = readFragmentIndexNetSerializer(Path.Combine(pathToFolderWithIndices, "fragmentIndex.ind"));
            }
        }

        private string GetOutputFolderForIndices(List<string> xmlDbFilenameList)
        {
            return Path.Combine(Path.GetDirectoryName(xmlDbFilenameList.First()), Path.GetFileNameWithoutExtension(xmlDbFilenameList.First()));
        }

        private void writeIndexEngineLog(IndexEngine indexEngine, string fileName)
        {
            using (StreamWriter output = new StreamWriter(fileName))
            {
                output.Write(indexEngine.ToString());
            }
            SucessfullyFinishedWritingFile(fileName);
        }

        private string GetFolderWithIndices(List<string> xmlDbFilenameList)
        {
            foreach (var ok in xmlDbFilenameList)
            {
                var he = Path.Combine(Path.GetDirectoryName(ok), Path.GetFileNameWithoutExtension(ok));
                if (Directory.Exists(he))
                    return he;
            }
            return null;
        }

        private Dictionary<float, List<int>> readFragmentIndexNetSerializer(string fragmentIndexFile)
        {
            var messageTypes = GetSubclassesAndItself(typeof(Dictionary<float, List<int>>));
            var ser = new NetSerializer.Serializer(messageTypes);

            Dictionary<float, List<int>> newPerson;
            using (var file = File.OpenRead(fragmentIndexFile))
                newPerson = (Dictionary<float, List<int>>)ser.Deserialize(file);

            return newPerson;
        }

        private List<CompactPeptide> readPeptideIndex(string peptideIndexFile)
        {
            var messageTypes = GetSubclassesAndItself(typeof(List<CompactPeptide>));
            var ser = new NetSerializer.Serializer(messageTypes);
            List<CompactPeptide> newPerson;
            using (var file = File.OpenRead(peptideIndexFile))
            {
                newPerson = (List<CompactPeptide>)ser.Deserialize(file);
            }

            return newPerson;
        }

        private IEnumerable<Type> GetSubclassesAndItself(Type type)
        {
            foreach (var ok in type.Assembly.GetTypes().Where(t => t.IsSubclassOf(type)))
                yield return ok;
            yield return type;
        }

        private void writeFragmentIndexNetSerializer(Dictionary<float, List<int>> fragmentIndex, string fragmentIndexFile)
        {
            var messageTypes = GetSubclassesAndItself(typeof(Dictionary<float, List<int>>));
            var ser = new NetSerializer.Serializer(messageTypes);

            using (var file = File.Create(fragmentIndexFile))
                ser.Serialize(file, fragmentIndex);
            SucessfullyFinishedWritingFile(fragmentIndexFile);
        }

        private void writePeptideIndex(List<CompactPeptide> peptideIndex, string peptideIndexFile)
        {
            var messageTypes = GetSubclassesAndItself(typeof(List<CompactPeptide>));
            var ser = new NetSerializer.Serializer(messageTypes);

            using (var file = File.Create(peptideIndexFile))
            {
                ser.Serialize(file, peptideIndex);
            }

            SucessfullyFinishedWritingFile(peptideIndexFile);
        }

        #endregion Private Methods

    }
}