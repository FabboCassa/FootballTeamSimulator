using System.Text.Json;
using Sim.Core.Match;

namespace Fts.Infrastructure.Leagues;

/// <summary>
/// The one place a <see cref="MatchReport"/> becomes a stored replay, and back (engine phase 2).
///
/// It exists because of a measurement: a ninety-minute report weighs <b>2 KB without the movement
/// stream and 2077 KB with it</b> — half a million small integers written as decimal ASCII into a
/// Postgres <c>text</c> column, which for a ten-club league season is 180 MB.
/// <see cref="PositionStream.Pack"/> puts the four integer tracks into a per-lane
/// delta/varint/base64 blob and takes the stored replay to <b>794 KB</b>, with the action list left
/// as readable JSON.
///
/// The film itself is NOT optional and was never the waste: <c>GetReplayAsync</c> hands this string
/// to any league member and the client draws it — watching your own match is the feature. What was
/// waste is the encoding.
///
/// Reading is backward compatible: a replay stored before the packed form carries its arrays, and
/// <see cref="PositionStream.Unpack"/> is a no-op on it.
/// </summary>
public static class ReplayStore
{
    /// <summary>Serializes a report for storage or for the wire, with the stream packed.</summary>
    public static string Write(MatchReport report)
    {
        report.Positions?.Pack();
        return JsonSerializer.Serialize(report);
    }

    /// <summary>Parses a stored replay, in either shape, with the stream ready to render.</summary>
    public static MatchReport? Read(string json)
    {
        MatchReport? report = JsonSerializer.Deserialize<MatchReport>(json);
        report?.Positions?.Unpack();
        return report;
    }
}
