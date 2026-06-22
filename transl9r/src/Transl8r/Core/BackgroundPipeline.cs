using System;
using System.Threading;
using System.Threading.Tasks;

namespace Transl8r.Core;

/// <summary>
/// Shared lifecycle for the background pipelines (screen OCR, system audio): a
/// single cancellable Task running <see cref="Run"/>, plus Status/Error events.
/// Subclasses implement only the loop body and raise progress via
/// <see cref="ReportStatus"/> / <see cref="ReportError"/>. The divergent work
/// (region polling vs. producer/consumer audio chunking) and the typed
/// <c>TextReady</c> events live in the subclasses.
/// </summary>
internal abstract class BackgroundPipeline
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<string>? Status;
    public event Action<string>? Error;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _task = Task.Run(() => Run(token), token);
    }

    public void Stop() => _cts?.Cancel();

    /// <summary>The pipeline body; runs on a background Task until cancelled.</summary>
    protected abstract void Run(CancellationToken token);

    protected void ReportStatus(string message) => Status?.Invoke(message);

    protected void ReportError(string message) => Error?.Invoke(message);
}
