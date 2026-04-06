using ValorantAfkBot.Core.Enums;
using ValorantAfkBot.Core.Models;

namespace ValorantAfkBot.Core.Services;

public static class MovementPlanner
{
    private const double RouteFollowScaleMultiplier = 0.05333333333333334;

    public static IReadOnlyList<string> BuildPattern(ProfileSettings profile, Random random)
    {
        return profile.PatternType switch
        {
            MovementPattern.Circle => ["W", "WD", "D", "SD", "S", "SA", "A", "WA"],
            MovementPattern.Strafe => Enumerable.Range(0, random.Next(4, 9)).Select(index => index % 2 == 0 ? "A" : "D").ToList(),
            MovementPattern.ForwardBack => Enumerable.Range(0, random.Next(4, 7)).Select(index => index % 2 == 0 ? "W" : "S").ToList(),
            MovementPattern.Custom => profile.MovementPath.Select(character => character.ToString()).ToList(),
            MovementPattern.RouteCanvas => BuildRouteCanvasPattern(profile),
            _ => BuildRandomPattern(profile, random),
        };
    }

    public static TimeSpan RandomizedJumpDelay(ProfileSettings profile, Random random)
    {
        double variation = profile.JumpDelaySeconds * 0.4;
        double minimum = profile.JumpDelaySeconds - variation;
        return TimeSpan.FromSeconds(minimum + (random.NextDouble() * (variation * 2.0)));
    }

    public static TimeSpan RandomizedKeyPress(ProfileSettings profile, Random random)
    {
        double duration = profile.KeyPressDelaySeconds * (0.5 + (profile.MovementIntensity * 0.8));
        return TimeSpan.FromSeconds(duration * (0.8 + (random.NextDouble() * 0.4)));
    }

    public static TimeSpan RandomizedMovementPause(ProfileSettings profile, Random random)
    {
        double basePause = 0.1 * (2.0 - profile.MovementSmoothness);
        return TimeSpan.FromSeconds((basePause * 0.5) + (random.NextDouble() * basePause));
    }

    public static TimeSpan RandomizedRouteFollowKeyPress(ProfileSettings profile, Random random)
    {
        double baseDuration = profile.KeyPressDelaySeconds * (0.38 + (profile.MovementIntensity * 0.22) + (profile.MovementSmoothness * 0.16));
        double jitter = 0.9 + (random.NextDouble() * 0.18);
        return TimeSpan.FromSeconds(Math.Clamp(baseDuration * jitter, 0.08, 0.42));
    }

    public static TimeSpan RandomizedRouteFollowTransitionPause(ProfileSettings profile, Random random)
    {
        double basePause = 0.012 + ((1.0 - profile.MovementSmoothness) * 0.035);
        double jitter = 0.75 + (random.NextDouble() * 0.4);
        return TimeSpan.FromSeconds(Math.Clamp(basePause * jitter, 0.004, 0.06));
    }

    public static TimeSpan RandomizedIdlePause(ProfileSettings profile, Random random)
    {
        double minimum = Math.Min(profile.MinPauseSeconds, profile.MaxPauseSeconds);
        double maximum = Math.Max(profile.MinPauseSeconds, profile.MaxPauseSeconds);
        return TimeSpan.FromSeconds(minimum + (random.NextDouble() * (maximum - minimum)));
    }

    private static IReadOnlyList<string> BuildRandomPattern(ProfileSettings profile, Random random)
    {
        List<string> directions = [];
        int length = random.Next(3, 9);
        for (int index = 0; index < length; index++)
        {
            bool preferStrafe = random.NextDouble() < profile.StrafePreference;
            directions.Add(preferStrafe
                ? (random.Next(0, 2) == 0 ? "A" : "D")
                : (random.Next(0, 2) == 0 ? "W" : "S"));
        }

        return directions;
    }

    private static IReadOnlyList<string> BuildRouteCanvasPattern(ProfileSettings profile)
    {
        IReadOnlyList<RouteCanvasPoint> points = profile.RouteCanvasPoints;
        if (points.Count < 2)
        {
            return profile.MovementPath.Select(character => character.ToString()).ToList();
        }

        List<RouteCanvasPoint> smoothed = SmoothRoute(points, profile);
        List<RouteCanvasPoint> traversal = BuildTraversalPath(smoothed);
        List<RouteCanvasPoint> sampled = ResampleTraversal(traversal, profile);
        List<string> directions = BuildRouteDirections(sampled);

        return directions.Count > 0
            ? directions
            : profile.MovementPath.Select(character => character.ToString()).ToList();
    }

    private static List<RouteCanvasPoint> SmoothRoute(IReadOnlyList<RouteCanvasPoint> points, ProfileSettings profile)
    {
        List<RouteCanvasPoint> smoothed = points.ToList();
        bool closed = IsClosedRoute(smoothed);
        int iterations = Math.Clamp((int)Math.Round(2 + (profile.MovementSmoothness * 3.0)), 3, 5);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (smoothed.Count < 3)
            {
                break;
            }

            List<RouteCanvasPoint> next = [];
            if (!closed)
            {
                next.Add(smoothed[0]);
            }

            int limit = closed ? smoothed.Count : smoothed.Count - 1;
            for (int index = 0; index < limit; index++)
            {
                RouteCanvasPoint a = smoothed[index];
                RouteCanvasPoint b = smoothed[(index + 1) % smoothed.Count];
                next.Add(Interpolate(a, b, 0.25));
                next.Add(Interpolate(a, b, 0.75));
            }

            if (!closed)
            {
                next.Add(smoothed[^1]);
            }

            smoothed = next;
        }

        return smoothed;
    }

    private static List<RouteCanvasPoint> ResampleTraversal(IReadOnlyList<RouteCanvasPoint> points, ProfileSettings profile)
    {
        if (points.Count < 2)
        {
            return points.ToList();
        }

        double totalLength = 0.0;
        for (int index = 0; index < points.Count - 1; index++)
        {
            totalLength += GetDistance(points[index], points[index + 1]);
        }

        if (totalLength <= double.Epsilon)
        {
            return points.ToList();
        }

        double densityBoost = 1.2 + (profile.MovementSmoothness * 1.1) + (profile.MovementIntensity * 0.9);
        double targetSpacing = Math.Clamp(0.03 / densityBoost, 0.0045, 0.015);
        int targetSamples = Math.Clamp((int)Math.Ceiling((totalLength * RouteFollowScaleMultiplier) / targetSpacing), 36, 720);
        double spacing = totalLength / targetSamples;

        List<RouteCanvasPoint> sampled = [points[0]];
        double distanceToNextSample = spacing;
        RouteCanvasPoint segmentStart = points[0];

        for (int index = 1; index < points.Count; index++)
        {
            RouteCanvasPoint segmentEnd = points[index];
            double segmentLength = GetDistance(segmentStart, segmentEnd);
            if (segmentLength <= double.Epsilon)
            {
                segmentStart = segmentEnd;
                continue;
            }

            while (segmentLength >= distanceToNextSample)
            {
                double t = distanceToNextSample / segmentLength;
                RouteCanvasPoint sample = Interpolate(segmentStart, segmentEnd, t);
                sampled.Add(sample);
                segmentStart = sample;
                segmentLength = GetDistance(segmentStart, segmentEnd);
                distanceToNextSample = spacing;
            }

            distanceToNextSample -= segmentLength;
            segmentStart = segmentEnd;
        }

        if (GetDistance(sampled[^1], points[^1]) > 0.0025)
        {
            sampled.Add(points[^1]);
        }

        return sampled;
    }

    private static List<RouteCanvasPoint> BuildTraversalPath(IReadOnlyList<RouteCanvasPoint> points)
    {
        List<RouteCanvasPoint> traversal = points.ToList();
        if (IsClosedRoute(points))
        {
            traversal.Add(points[0]);
            return traversal;
        }

        for (int index = points.Count - 2; index >= 0; index--)
        {
            traversal.Add(points[index]);
        }

        return traversal;
    }

    private static List<string> BuildRouteDirections(IReadOnlyList<RouteCanvasPoint> sampled)
    {
        List<string> directions = [];
        string? previousDirection = null;

        for (int index = 0; index < sampled.Count - 1; index++)
        {
            int leftIndex = Math.Max(0, index - 1);
            int rightIndex = Math.Min(sampled.Count - 1, index + 2);
            RouteCanvasPoint left = sampled[leftIndex];
            RouteCanvasPoint right = sampled[rightIndex];
            string direction = ComposeDirection(right.X - left.X, right.Y - left.Y, previousDirection);

            if (string.IsNullOrEmpty(direction))
            {
                continue;
            }

            directions.Add(direction);
            previousDirection = direction;
        }

        return SmoothDirectionNoise(directions);
    }

    private static List<string> SmoothDirectionNoise(IReadOnlyList<string> directions)
    {
        if (directions.Count < 3)
        {
            return directions.ToList();
        }

        List<string> smoothed = directions.ToList();
        for (int index = 1; index < smoothed.Count - 1; index++)
        {
            if (smoothed[index - 1] == smoothed[index + 1] && smoothed[index] != smoothed[index - 1])
            {
                smoothed[index] = smoothed[index - 1];
            }
        }

        return smoothed;
    }

    private static string ComposeDirection(double dx, double dy, string? previousDirection)
    {
        const double deadZone = 0.003;
        const double diagonalThreshold = 0.32;
        const double axisCommitThreshold = 0.18;
        string direction = string.Empty;
        double absX = Math.Abs(dx);
        double absY = Math.Abs(dy);
        double dominant = Math.Max(absX, absY);

        if (dominant <= deadZone)
        {
            return previousDirection ?? string.Empty;
        }

        double minorRatio = Math.Min(absX, absY) / dominant;
        bool useDiagonal = minorRatio >= diagonalThreshold;

        if (absY >= deadZone && (useDiagonal || (absY / dominant) >= axisCommitThreshold))
        {
            direction += dy < 0 ? "W" : "S";
        }

        if (absX >= deadZone && (useDiagonal || (absX / dominant) >= axisCommitThreshold))
        {
            direction += dx < 0 ? "A" : "D";
        }

        return string.IsNullOrEmpty(direction) ? previousDirection ?? "W" : direction;
    }

    private static bool IsClosedRoute(IReadOnlyList<RouteCanvasPoint> points)
    {
        if (points.Count < 3)
        {
            return false;
        }

        RouteCanvasPoint first = points[0];
        RouteCanvasPoint last = points[^1];
        double dx = first.X - last.X;
        double dy = first.Y - last.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= 0.08;
    }

    private static RouteCanvasPoint Interpolate(RouteCanvasPoint start, RouteCanvasPoint end, double t) =>
        new()
        {
            X = start.X + ((end.X - start.X) * t),
            Y = start.Y + ((end.Y - start.Y) * t),
        };

    private static double GetDistance(RouteCanvasPoint first, RouteCanvasPoint second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
