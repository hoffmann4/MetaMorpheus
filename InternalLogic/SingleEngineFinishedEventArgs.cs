﻿namespace InternalLogicEngineLayer
{
    public class SingleEngineFinishedEventArgs
    {
        private MyResults myResults;

        public SingleEngineFinishedEventArgs(MyResults myResults)
        {
            this.myResults = myResults;
        }

        public override string ToString()
        {
            return myResults.ToString();
        }
    }
}