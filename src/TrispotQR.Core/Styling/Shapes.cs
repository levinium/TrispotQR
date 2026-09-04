namespace TrispotQR.Core.Styling;

/// <summary>Shape drawn for each dark data module.</summary>
public enum ModuleShape
{
    /// <summary>Plain squares. The classic look and the most forgiving to scan.</summary>
    Square,

    /// <summary>Squares with softened corners.</summary>
    RoundedSquare,

    /// <summary>Round dots.</summary>
    Circle,

    /// <summary>Squares rotated 45 degrees.</summary>
    Diamond,

    /// <summary>
    /// Neighbour aware rounding: a corner is rounded only where the two modules touching
    /// it are light, so runs of dark modules flow together into one shape.
    /// </summary>
    Fluid,
}

/// <summary>Shape of the outer ring of each of the three corner markers.</summary>
public enum MarkerFrameShape
{
    Square,
    RoundedSquare,
    Circle,

    /// <summary>Rounded on three corners and square on the outer one, a common branded look.</summary>
    Leaf,
}

/// <summary>
/// Shape of the solid core inside each corner marker.
///
/// Deliberately narrow. A scanner finds a QR code by looking for the 1:1:3:1:1 run of
/// dark and light along lines crossing a corner marker, so the core has to stay roughly
/// square in every direction. A diamond core was built and tested here and failed to
/// decode in every single combination, so it is not offered. Do not add one back.
/// </summary>
public enum MarkerCenterShape
{
    Square,
    RoundedSquare,
    Circle,
}

/// <summary>Which parts of the code an outline is stroked around.</summary>
public enum OutlineTarget
{
    Modules,
    Markers,
    Both,
}

/// <summary>Shape of the clear area punched out behind a centre logo.</summary>
public enum LogoPunchShape
{
    /// <summary>Square hole. Suits a logo that fills its bounding box.</summary>
    Square,

    /// <summary>Square hole with softened corners.</summary>
    RoundedSquare,

    /// <summary>Round hole. Suits a circular mark.</summary>
    Circle,

    /// <summary>No hole. Only sensible when the logo itself is opaque and light on dark.</summary>
    None,
}
