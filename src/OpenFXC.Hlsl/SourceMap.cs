using System.Collections.Generic;
using System.Linq;

namespace OpenFXC.Hlsl;

/// <summary>
/// Maps spans in the preprocessed output back to their originating file spans.
/// </summary>
public sealed class SourceMap
{
    private readonly List<SourceSegment> _segments;

    internal SourceMap(List<SourceSegment> segments)
    {
        _segments = segments.OrderBy(s => s.OutputSpan.Start).ToList();
    }

    public Diagnostic[] AttachOrigins(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(AttachOrigin).ToArray();

    public Diagnostic AttachOrigin(Diagnostic diagnostic)
    {
        var origin = Map(diagnostic.Span);
        if (origin is null)
        {
            return diagnostic;
        }

        return diagnostic with { Origin = origin };
    }

    public DiagnosticOrigin? Map(Span outputSpan)
    {
        // Use the diagnostic start to choose a segment.
        var segment = _segments.FirstOrDefault(s => outputSpan.Start >= s.OutputSpan.Start && outputSpan.Start < s.OutputSpan.End);
        if (segment is null)
        {
            return null;
        }

        var relativeStart = outputSpan.Start - segment.OutputSpan.Start;
        var relativeEnd = outputSpan.End - segment.OutputSpan.Start;
        var mappedStart = segment.InputSpan.Start + Clamp(relativeStart, 0, segment.InputSpan.End - segment.InputSpan.Start);
        var mappedEnd = segment.InputSpan.Start + Clamp(relativeEnd, 0, segment.InputSpan.End - segment.InputSpan.Start);

        return new DiagnosticOrigin(segment.FileName, new Span { Start = mappedStart, End = mappedEnd });
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
}

internal sealed record SourceSegment(Span OutputSpan, string FileName, Span InputSpan);
