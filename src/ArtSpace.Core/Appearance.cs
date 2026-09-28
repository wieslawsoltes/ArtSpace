namespace ArtSpace.Core;

public enum OpacityMaskMode { Luminance, Alpha }
/// <summary>Legacy preserves ArtSpace's original normalized endpoint/circular-radial convention.</summary>
public enum GradientSpace { Legacy, ObjectBoundingBox, UserSpaceOnUse }
public enum GradientSpread { Pad, Repeat, Reflect }

public static class AffineGeometry
{
    public static bool IsFinite(Matrix2D matrix) =>
        double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) &&
        double.IsFinite(matrix.DX) && double.IsFinite(matrix.DY);
    public static bool IsInvertible(Matrix2D matrix) => IsFinite(matrix) &&
        Math.Abs(matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21) >= 1e-12;
}
