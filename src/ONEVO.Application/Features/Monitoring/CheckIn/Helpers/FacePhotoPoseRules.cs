using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.CheckIn.Helpers;

/// <summary>Tray face setup steps: look straight, then turn the head to each side.</summary>
public static class FacePhotoPose
{
    public const string Front = "front";
    public const string Left = "left";
    public const string Right = "right";

    public static readonly string[] All = [Front, Left, Right];
}

public static class FacePhotoPoseRules
{
    /// <summary>
    /// Whether the analysed head pose fits the requested setup step. Left and right are only
    /// judged by how far the head is turned — which way is checked when the three photos are
    /// committed together (the two side photos must be turned in opposite directions).
    /// </summary>
    public static bool Matches(string? pose, FaceQualityOutcome quality) =>
        string.Equals(pose, FacePhotoPose.Left, StringComparison.OrdinalIgnoreCase)
        || string.Equals(pose, FacePhotoPose.Right, StringComparison.OrdinalIgnoreCase)
            ? quality.TurnedSideways
            : quality.FacingFront;

    /// <summary>The two side photos are turned in opposite directions.</summary>
    public static bool AreOppositeSides(FaceQualityOutcome left, FaceQualityOutcome right) =>
        left.Yaw is { } l && right.Yaw is { } r && Math.Sign(l) != 0 && Math.Sign(l) == -Math.Sign(r);
}
