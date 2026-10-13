using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using System;
using System.Threading;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// One attached TIA Portal instance, keyed by its Windows process id.
    ///
    /// Callers: Portal (registry, request scope). Affected API: none - new type. Reads/writes no
    /// data files.
    ///
    /// The project and session live here, not on Portal, so that two instances never share
    /// "the open project". Gate serializes Openness calls into this instance; it is a
    /// SemaphoreSlim rather than a Monitor because the request scope spans an await.
    /// </summary>
    public sealed class PortalContext : IDisposable
    {
        public int Id { get; }

        public TiaPortal TiaPortal { get; }

        public ProjectBase? Project { get; set; }

        public LocalSession? Session { get; set; }

        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);

        // Written by the thread running the current request's tool body, read by 'GetState' from
        // whatever thread is handling that call - neither takes Gate, so this stays visible while
        // a long call is in flight. OperationProgress is immutable; a new instance is swapped in
        // on each update instead of mutating one in place, so a concurrent read never sees a
        // half-written value.
        private volatile OperationProgress? _progress;

        public OperationProgress? Progress => _progress;

        internal void SetProgress(OperationProgress? progress) => _progress = progress;

        // written from the Openness thread that raises Disposed
        private volatile bool _isDisposed;

        public bool IsDisposed
        {
            get => _isDisposed;
            private set => _isDisposed = value;
        }

        public PortalContext(int id, TiaPortal tiaPortal)
        {
            Id = id;
            TiaPortal = tiaPortal;

            // Raised when this client detaches and when the TIA Portal process goes away.
            TiaPortal.Disposed += (sender, args) => IsDisposed = true;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            Project = null;
            Session = null;

            try
            {
                TiaPortal.Dispose();
            }
            catch (Exception)
            {
                // the instance may already be gone
            }
        }
    }

    /// <summary>
    /// A running TIA Portal process as reported by 'GetPortals'.
    /// </summary>
    public class PortalInstance
    {
        public int PortalId { get; set; }
        public string? ProjectPath { get; set; }
        public string? Mode { get; set; }
        public bool IsAttached { get; set; }
    }
}
