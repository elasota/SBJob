using System.Collections;
using System.Threading;

// Simple worker thread pool
// Worker threads float freely and only interact with the pool when idle.
//
// If a worker thread runs out of work, it attempts to dequeue a job,
// and if that fails, enters the idle thread queue and waits for a kickoff event.
//
// If a thread adds work, it attempts to find an idle thread, set the work for kickoff,
// and sets the kickoff event.  If there are no idle threads, it queues the job.
//
// Both of the above mentioned operations lock the thread pool.
namespace SiegeBreaker
{
    public class SBThreadPool
    {
        private class WorkerThread
        {
            private EventWaitHandle m_idleWaitHandle;
            private SBThreadPool m_threadPool;
            private WorkFunc m_kickOffWorkFunc;

            public WorkerThread(SBThreadPool threadPool)
            {
                m_idleWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                m_threadPool = threadPool;
                m_kickOffWorkFunc = null;
            }

            public void Run()
            {
                while (true)
                {
                    WorkFunc work = m_threadPool.GetMoreWork(this);
                    if (work == null)
                    {
                        m_threadPool.InternalNotifyThreadDone();
                        m_idleWaitHandle.Close();
                        return;
                    }
                    work(m_threadPool);
                }
            }

            public WorkFunc WaitForKickOffWork()
            {
                m_idleWaitHandle.WaitOne();
                return this.m_kickOffWorkFunc;
            }

            public void KickOffWork(WorkFunc workFunc)
            {
                this.m_kickOffWorkFunc = workFunc;
                m_idleWaitHandle.Set();
            }
        };

        private Queue m_jobs;
        private Queue m_idleThreads;
        private int m_shutdownCountdown;
        private EventWaitHandle m_shutdownJoinHandle;

        public delegate void WorkFunc(SBThreadPool pool);
        public int NumThreads { get; private set; }

        private static void WorkerThreadStart(object obj)
        {
            WorkerThread workerThread = (WorkerThread)obj;
            workerThread.Run();
        }

        public SBThreadPool(int numThreads)
        {
            this.NumThreads = numThreads;
            m_jobs = new Queue();
            m_idleThreads = new Queue();
            for (int i = 0; i < NumThreads; i++)
            {
                WorkerThread workerThread = new WorkerThread(this);
                ParameterizedThreadStart pts = new ParameterizedThreadStart(WorkerThreadStart);
                Thread t = new Thread(pts);
                t.Start(workerThread);
            }
        }

        public void Dispose()
        {
            // Add shutdown work jobs
            m_shutdownCountdown = NumThreads;
            m_shutdownJoinHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            for (int i = 0; i < NumThreads; i++)
                InternalAddWork(null);
            m_shutdownJoinHandle.WaitOne();
            m_shutdownJoinHandle.Close();
        }

        public void AddWork(WorkFunc workFunc)
        {
            if (workFunc == null)
                throw new System.InvalidOperationException("AddWork was passed a null work delegate");
            InternalAddWork(workFunc);
        }

        private void InternalNotifyThreadDone()
        {
            if (Interlocked.Decrement(ref m_shutdownCountdown) == 0)
                m_shutdownJoinHandle.Set();
        }

        private void InternalAddWork(WorkFunc workFunc)
        {
            WorkerThread idleWorkerThread;
            lock(this)
            {
                if (m_idleThreads.Count == 0)
                {
                    m_jobs.Enqueue(workFunc);
                    return;
                }

                idleWorkerThread = (WorkerThread)m_idleThreads.Dequeue();
            }
            idleWorkerThread.KickOffWork(workFunc);
        }

        // Gets more pending work or enters the idle thread queue and waits for a kickoff event
        private WorkFunc GetMoreWork(WorkerThread workerThread)
        {
            lock(this)
            {
                if (m_jobs.Count > 0)
                    return (WorkFunc)m_jobs.Dequeue();
                m_idleThreads.Enqueue(workerThread);
            }

            return workerThread.WaitForKickOffWork();
        }
    }
}
