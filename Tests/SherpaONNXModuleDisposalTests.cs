using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.SherpaONNXUnity.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Eitan.SherpaONNXUnity.Tests
{
    public sealed class SherpaONNXModuleDisposalTests
    {
        private const int TEST_TIMEOUT_MILLISECONDS = 5000;

        [UnityTest]
        [Timeout(TEST_TIMEOUT_MILLISECONDS)]
        public IEnumerator DisposalTask_DoesNotCompleteBeforePostedDestroyCallbackReturns()
        {
            var previousContext = SynchronizationContext.Current;
            var queuedContext = new QueuedSynchronizationContext();
            ProbeModule module;

            try
            {
                SynchronizationContext.SetSynchronizationContext(queuedContext);
                module = new ProbeModule();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            try
            {
                Task disposeAsyncTask = module.DisposeAsync();
                yield return new WaitUntil(() => queuedContext.HasPendingCallback);
                yield return null;

                bool disposalTaskCompletedBeforeDestroy = module.DisposalTask.IsCompleted;
                bool disposeAsyncCompletedBeforeDestroy = disposeAsyncTask.IsCompleted;

                queuedContext.ExecuteNext();
                yield return new WaitUntil(() => module.DisposalTask.IsCompleted && disposeAsyncTask.IsCompleted);

                Assert.That(disposalTaskCompletedBeforeDestroy, Is.False,
                    "DisposalTask completed while the posted OnDestroy callback was still queued.");
                Assert.That(disposeAsyncCompletedBeforeDestroy, Is.False,
                    "DisposeAsync completed while the posted OnDestroy callback was still queued.");
                Assert.That(module.DestroyCount, Is.EqualTo(1));
                Assert.That(queuedContext.HasPendingCallback, Is.False,
                    "The module left a destruction callback queued after DisposeAsync completed.");
            }
            finally
            {
                queuedContext.ExecuteAll();
                module.Dispose();
            }
        }

        [UnityTest]
        [Timeout(TEST_TIMEOUT_MILLISECONDS)]
        public IEnumerator RepeatedDisposal_WithoutCapturedContext_CompletesAfterDestroyRunsOnce()
        {
            var previousContext = SynchronizationContext.Current;
            ProbeModule module;

            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                module = new ProbeModule();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            Task firstDisposeAsyncTask = module.DisposeAsync();
            module.Dispose();
            Task secondDisposeAsyncTask = module.DisposeAsync();
            yield return new WaitUntil(() =>
                module.DisposalTask.IsCompleted &&
                firstDisposeAsyncTask.IsCompleted &&
                secondDisposeAsyncTask.IsCompleted);

            Assert.That(module.DestroyCount, Is.EqualTo(1));
            Assert.That(module.DisposalTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(firstDisposeAsyncTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(secondDisposeAsyncTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [UnityTest]
        [Timeout(TEST_TIMEOUT_MILLISECONDS)]
        public IEnumerator DisposalTask_WhenDestroyThrows_CompletesAfterPostedCallbackReturns()
        {
            var previousContext = SynchronizationContext.Current;
            var queuedContext = new QueuedSynchronizationContext();
            ProbeModule module;

            try
            {
                SynchronizationContext.SetSynchronizationContext(queuedContext);
                module = new ProbeModule(throwOnDestroy: true);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            module.Dispose();
            yield return new WaitUntil(() => queuedContext.HasPendingCallback);

            bool loggingEnabled = SherpaLog.Enabled;
            SherpaLogLevel loggingLevel = SherpaLog.Level;
            try
            {
                SherpaLog.Configure(SherpaLogLevel.Off, enabled: false);
                queuedContext.ExecuteNext();
            }
            finally
            {
                SherpaLog.Configure(loggingLevel, loggingEnabled);
            }
            yield return new WaitUntil(() => module.DisposalTask.IsCompleted);

            Assert.That(module.DestroyCount, Is.EqualTo(1));
            Assert.That(module.DisposalTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        private sealed class ProbeModule : SherpaONNXModule
        {
            private int _destroyCount;
            private readonly bool _throwOnDestroy;

            public ProbeModule(bool throwOnDestroy = false)
                : base("sherpa-onnx-disposal-contract-test", startImmediately: false)
            {
                _throwOnDestroy = throwOnDestroy;
            }

            public int DestroyCount => Volatile.Read(ref _destroyCount);

            protected override SherpaONNXModuleType ModuleType => SherpaONNXModuleType.SpeechRecognition;

            protected override Task<bool> Initialization(
                SherpaONNXModelMetadata metadata,
                int sampleRate,
                bool isMobilePlatform,
                SherpaONNXFeedbackReporter reporter,
                CancellationToken ct)
            {
                return Task.FromResult(true);
            }

            protected override void OnDestroy()
            {
                Interlocked.Increment(ref _destroyCount);
                if (_throwOnDestroy)
                {
                    throw new InvalidOperationException("destroy-contract-test-failure");
                }
            }
        }

        private sealed class QueuedSynchronizationContext : SynchronizationContext
        {
            private readonly object _gate = new object();
            private readonly Queue<WorkItem> _callbacks = new Queue<WorkItem>();

            public bool HasPendingCallback
            {
                get
                {
                    lock (_gate)
                    {
                        return _callbacks.Count > 0;
                    }
                }
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                if (d == null)
                {
                    throw new ArgumentNullException(nameof(d));
                }

                lock (_gate)
                {
                    _callbacks.Enqueue(new WorkItem(d, state));
                }
            }

            public void ExecuteNext()
            {
                WorkItem workItem;
                lock (_gate)
                {
                    if (_callbacks.Count == 0)
                    {
                        throw new InvalidOperationException("No synchronization-context callback is queued.");
                    }

                    workItem = _callbacks.Dequeue();
                }

                workItem.Callback(workItem.State);
            }

            public void ExecuteAll()
            {
                while (true)
                {
                    WorkItem workItem;
                    lock (_gate)
                    {
                        if (_callbacks.Count == 0)
                        {
                            return;
                        }

                        workItem = _callbacks.Dequeue();
                    }

                    workItem.Callback(workItem.State);
                }
            }

            private readonly struct WorkItem
            {
                public WorkItem(SendOrPostCallback callback, object state)
                {
                    Callback = callback;
                    State = state;
                }

                public SendOrPostCallback Callback { get; }
                public object State { get; }
            }
        }
    }
}
