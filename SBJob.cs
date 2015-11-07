using System.Collections;
using System.Threading;
using System;

namespace SiegeBreaker
{
    public abstract class SBJob
    {
        private volatile int finishedVar;

        public bool IsFinished
        {
            get
            {
                return finishedVar != 0;
            }
        }

        public SBJob()
        {
            this.finishedVar = 0;
        }

#pragma warning disable 0420    // a reference to a volatile field will not be treated as volatile
        private void RunAndMarkDone(SBThreadPool pool)
        {
            Run(pool);
            Interlocked.Increment(ref finishedVar);
        }
#pragma warning restore 0420

        public void QueueInPool(SBThreadPool pool)
        {
            finishedVar = 0;
            pool.AddWork(RunAndMarkDone);
        }

        public abstract void Run(SBThreadPool pool);
    }
}
