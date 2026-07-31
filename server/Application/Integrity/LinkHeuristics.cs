namespace Fts.Application.Integrity;

/// <summary>Weights for the account-link score, and the threshold above which two accounts are treated as
/// probably the same person. All values are points; the score saturates at 100.</summary>
/// <param name="SharedAddressPoints">Both accounts seen from the same (hashed) address.</param>
/// <param name="SharedDevicePoints">Both accounts seen from the same device id — much stronger evidence
/// than an address, which a whole household or a phone network shares.</param>
/// <param name="CreatedTogetherPoints">The accounts were registered within <paramref name="CreatedTogetherMinutes"/>
/// of each other.</param>
/// <param name="CreatedTogetherMinutes">How close "registered together" is.</param>
/// <param name="Threshold">Score at or above which the pair is treated as linked.</param>
public readonly record struct LinkWeights(
    int SharedAddressPoints,
    int SharedDevicePoints,
    int CreatedTogetherPoints,
    int CreatedTogetherMinutes,
    int Threshold);

/// <summary>The evidence gathered about one pair of accounts.</summary>
/// <param name="SharedAddress">They have been seen from the same hashed address.</param>
/// <param name="SharedDevice">They have been seen from the same device id.</param>
/// <param name="MinutesApartAtRegistration">Absolute gap between the two registration timestamps.</param>
public readonly record struct LinkEvidence(bool SharedAddress, bool SharedDevice, double MinutesApartAtRegistration);

/// <summary>
/// Multi-account heuristics (Phase 9.5). PURE and deterministic — no I/O, no RNG — so the scoring rules are
/// unit-testable and reviewable in one place.
///
/// The design position, agreed with the user: <b>flag and separate, do not ban</b>. A shared address is
/// weak evidence (a household, a student flat, a phone carrier's NAT all look identical), so on its own it
/// must not cost anyone their account. What it CAN do without hurting an innocent pair is stop two linked
/// accounts from being seated in the same group, which is where the actual damage would be — feeding each
/// other points and players. A shared device plus a shared address, or a shared device and a joint
/// registration, clears the threshold on its own.
/// </summary>
public static class LinkHeuristics
{
    /// <summary>Default weights: address alone (50) stays under the threshold on purpose; device alone (80)
    /// or address + registered together (70) clears it.</summary>
    public static readonly LinkWeights Default = new(
        SharedAddressPoints: 50,
        SharedDevicePoints: 80,
        CreatedTogetherPoints: 20,
        CreatedTogetherMinutes: 60,
        Threshold: 60);

    /// <summary>0..100 — how likely the two accounts are the same person.</summary>
    public static int Score(LinkEvidence evidence, LinkWeights weights)
    {
        int score = 0;
        if (evidence.SharedAddress) score += weights.SharedAddressPoints;
        if (evidence.SharedDevice) score += weights.SharedDevicePoints;
        if (evidence.MinutesApartAtRegistration <= weights.CreatedTogetherMinutes
            && (evidence.SharedAddress || evidence.SharedDevice))
        {
            // Registration proximity only counts as corroboration — two strangers signing up in the same
            // minute from different places is a coincidence, not a link.
            score += weights.CreatedTogetherPoints;
        }
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>Whether the pair should be kept apart by matchmaking.</summary>
    public static bool AreLinked(LinkEvidence evidence, LinkWeights weights) =>
        Score(evidence, weights) >= weights.Threshold;
}
