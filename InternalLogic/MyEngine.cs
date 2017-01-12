﻿using Proteomics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace InternalLogicEngineLayer
{
    public abstract class MyEngine
    {
        public static UsefulProteomicsDatabases.Generated.unimod unimodDeserialized;

        public static Dictionary<int, ChemicalFormulaModification> uniprotDeseralized;

        public static event EventHandler<SingleEngineEventArgs> startingSingleEngineHander;

        public static event EventHandler<SingleEngineFinishedEventArgs> finishedSingleEngineHandler;

        public static event EventHandler<string> outLabelStatusHandler;

        public static event EventHandler<ProgressEventArgs> outProgressHandler;

        public static event EventHandler<string> outRichTextBoxHandler;

        protected void status(string v)
        {
            outLabelStatusHandler?.Invoke(this, v);
        }

        private void startingSingleEngine()
        {
            startingSingleEngineHander?.Invoke(this, new SingleEngineEventArgs(this));
        }

        protected void ReportProgress(ProgressEventArgs v)
        {
            outProgressHandler?.Invoke(this, v);
        }

        protected void output(string v)
        {
            outRichTextBoxHandler?.Invoke(this, v);
        }

        public MyResults Run()
        {
            startingSingleEngine();
            ValidateParams();
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            var myResults = RunSpecific();
            stopWatch.Stop();
            myResults.Time = stopWatch.Elapsed;
            finishedSingleEngine(myResults);
            return myResults;
        }

        private void finishedSingleEngine(MyResults myResults)
        {
            finishedSingleEngineHandler?.Invoke(this, new SingleEngineFinishedEventArgs(myResults));
        }

        public abstract void ValidateParams();
        protected abstract MyResults RunSpecific();
    }
}