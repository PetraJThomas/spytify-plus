using System;
using System.Threading.Tasks;
using EspionSpotify.Models;

namespace EspionSpotify
{
    /// <summary>
    /// Background encoder. Recorders hand off finished captures via <see cref="Enqueue"/>
    /// and return immediately; a single long-lived worker encodes them sequentially,
    /// off the recording path.
    /// </summary>
    public interface IEncodeService : IDisposable
    {
        /// <summary>Queue a finished capture for encoding. No-op once draining has started.</summary>
        void Enqueue(EncodeJob job);

        /// <summary>
        /// Stop accepting new jobs and wait for every queued encode to finish
        /// (drain-then-exit), so no captured song is dropped on stop.
        /// </summary>
        Task CompleteAndDrainAsync();
    }
}
