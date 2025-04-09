using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QuestMap
{
    internal sealed class GraphWorker : IDisposable, IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;

        public Task<GraphInfo?> Task { get; }

        public GraphWorker(Task<GraphInfo?> task, CancellationTokenSource cts)
        {
            this.Task = task;
            this._cts = cts;
        }

        public void Dispose()
        {
            this._cts.Cancel();
            this._cts.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await this._cts.CancelAsync();
            this._cts.Dispose();
            GC.SuppressFinalize(this);
        }

        ~GraphWorker()
        {
            try { this.Dispose(); } catch { /* Swallow */ }
        }
    }
}
