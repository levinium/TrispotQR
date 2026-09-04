using Net.Codecrete.QrCodeGenerator;

namespace TrispotQR.Core.Qr;

/// <summary>
/// The outcome of an encode attempt. Failures carry a message written for the person
/// using the app rather than an exception, because running out of QR capacity is an
/// ordinary thing for a user to do, not a program error.
/// </summary>
public sealed class QrEncodeResult
{
    private QrEncodeResult(QrMatrix? matrix, string? errorMessage)
    {
        Matrix = matrix;
        ErrorMessage = errorMessage;
    }

    public QrMatrix? Matrix { get; }

    public string? ErrorMessage { get; }

    public bool Success => Matrix is not null;

    internal static QrEncodeResult Ok(QrMatrix matrix) => new(matrix, null);

    internal static QrEncodeResult Failure(string message) => new(null, message);
}

/// <summary>
/// Turns a string into a <see cref="QrMatrix"/>. Thin wrapper over the Nayuki port in
/// Net.Codecrete.QrCodeGenerator, which handles segment selection and mask scoring.
/// </summary>
public static class QrEncoder
{
    /// <summary>
    /// Maximum bytes storable in a version 40 symbol at each error correction level,
    /// used for the capacity readout in the UI. Content encoded as digits or uppercase
    /// alphanumerics packs tighter than this, so treat it as a conservative floor.
    /// </summary>
    private static readonly Dictionary<EccLevel, int> ByteCapacities = new()
    {
        [EccLevel.Low] = 2953,
        [EccLevel.Medium] = 2331,
        [EccLevel.Quartile] = 1663,
        [EccLevel.High] = 1273,
    };

    /// <summary>Bytes storable at version 40 for the given error correction level.</summary>
    public static int ByteCapacity(EccLevel level) => ByteCapacities[level];

    /// <summary>
    /// Encodes <paramref name="text"/> at no less than <paramref name="minimumEcc"/>.
    /// When the content leaves slack in the chosen symbol the level is boosted for free,
    /// so the resulting <see cref="QrMatrix.EffectiveEcc"/> can be stronger than asked.
    /// </summary>
    public static QrEncodeResult Encode(string text, EccLevel minimumEcc)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return QrEncodeResult.Failure("Enter some text or a link to make a QR code.");
        }

        try
        {
            var qr = QrCode.EncodeTextAdvanced(text, ToLibraryEcc(minimumEcc), boostEcl: true);
            return QrEncodeResult.Ok(ToMatrix(qr));
        }
        catch (DataTooLongException)
        {
            var limit = ByteCapacity(minimumEcc);
            return QrEncodeResult.Failure(
                $"That content is too long for a QR code. The limit is about {limit:N0} characters " +
                $"at {Describe(minimumEcc)} error correction. Shorten the content, or lower the " +
                "error correction level in Advanced options to fit more in.");
        }
    }

    private static QrMatrix ToMatrix(QrCode qr)
    {
        var size = qr.Size;
        var modules = new bool[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                modules[(y * size) + x] = qr.GetModule(x, y);
            }
        }

        return new QrMatrix(size, qr.Version, FromLibraryEcc(qr.ErrorCorrectionLevel), modules);
    }

    private static QrCode.Ecc ToLibraryEcc(EccLevel level) => level switch
    {
        EccLevel.Low => QrCode.Ecc.Low,
        EccLevel.Medium => QrCode.Ecc.Medium,
        EccLevel.Quartile => QrCode.Ecc.Quartile,
        EccLevel.High => QrCode.Ecc.High,
        _ => QrCode.Ecc.Medium,
    };

    private static EccLevel FromLibraryEcc(QrCode.Ecc ecc) => ecc switch
    {
        QrCode.Ecc.Low => EccLevel.Low,
        QrCode.Ecc.Medium => EccLevel.Medium,
        QrCode.Ecc.Quartile => EccLevel.Quartile,
        QrCode.Ecc.High => EccLevel.High,
        _ => EccLevel.Medium,
    };

    private static string Describe(EccLevel level) => level switch
    {
        EccLevel.Low => "low",
        EccLevel.Medium => "medium",
        EccLevel.Quartile => "quartile",
        EccLevel.High => "high",
        _ => "medium",
    };
}
