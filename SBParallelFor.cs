using System.Threading;
using System;

namespace SiegeBreaker
{
    public class SBParallel
    {
        private class ParallelForRun : IDisposable
        {
            private volatile int m_nextUnitIndex;
            private volatile int m_numFinishedUnits;
            private int m_numUnits;
            private int m_first;
            private EventWaitHandle m_joinEvent;
            ParallelForIterationDelegate m_iterationRun;

            public ParallelForRun(int numUnits, int first, ParallelForIterationDelegate iterationRun)
            {
                m_first = first;
                m_numUnits = numUnits;
                m_iterationRun = iterationRun;
                m_nextUnitIndex = 0;
                m_numFinishedUnits = 0;
                m_joinEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
            }

#pragma warning disable 0420    //a reference to a volatile field will not be treated as volatile
            public void ThreadRunIterations()
            {
                int startedUnit = Interlocked.Increment(ref m_nextUnitIndex) - 1;
                int finishedUnit = -1;
                while (startedUnit < m_numUnits)
                {
                    m_iterationRun(startedUnit + m_first);
                    finishedUnit = Interlocked.Increment(ref m_numFinishedUnits);
                    startedUnit = Interlocked.Increment(ref m_nextUnitIndex) - 1;
                }

                if (finishedUnit == m_numUnits)
                    m_joinEvent.Set();
            }
#pragma warning restore 0420

            public void Join()
            {
                m_joinEvent.WaitOne();
            }

            public void Dispose()
            {
                m_joinEvent.Close();
            }
        }

        private class ParallelForJob : SBJob
        {
            private ParallelForRun loopRun;

            public ParallelForJob(ParallelForRun loopRun)
            {
                this.loopRun = loopRun;
            }

            public sealed override void Run(SBThreadPool pool)
            {
                loopRun.ThreadRunIterations();
            }
        }

        public delegate void ParallelForIterationDelegate(int index);

        public static void For(SBThreadPool pool, int inclusiveStart, int exclusiveEnd, ParallelForIterationDelegate iterate)
        {
            if(inclusiveStart >= exclusiveEnd)
                return;

            using (ParallelForRun loopRun = new ParallelForRun(exclusiveEnd - inclusiveStart, inclusiveStart, iterate))
            {
                for (int i = 0; i < pool.NumThreads; i++)
                {
                    SBJob job = new ParallelForJob(loopRun);
                    job.QueueInPool(pool);
                }
                loopRun.ThreadRunIterations();
                loopRun.Join();
            }
        }
    }
}
