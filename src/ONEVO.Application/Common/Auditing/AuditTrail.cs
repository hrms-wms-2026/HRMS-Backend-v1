namespace ONEVO.Application.Common.Auditing;

/// <summary>Scoped. A stack of frames, so an audited command that sends another keeps its own before-state.</summary>
public sealed class AuditTrail : IAuditTrail
{
    private readonly Stack<Frame> _frames = new();

    public void RecordBefore(object before)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (_frames.TryPeek(out var frame))
            frame.Before = before;
    }

    public void RecordTarget(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (_frames.TryPeek(out var frame))
            frame.TargetId = targetId;
    }

    internal Frame BeginFrame()
    {
        var frame = new Frame(this);
        _frames.Push(frame);
        return frame;
    }

    internal sealed class Frame : IDisposable
    {
        private readonly AuditTrail _owner;

        public Frame(AuditTrail owner) => _owner = owner;

        public object? Before { get; set; }

        public string? TargetId { get; set; }

        public void Dispose()
        {
            if (_owner._frames.TryPeek(out var top) && ReferenceEquals(top, this))
                _owner._frames.Pop();
        }
    }
}
