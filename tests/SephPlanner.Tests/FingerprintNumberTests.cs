using SephPlanner.Core;

namespace SephPlanner.Tests;

public sealed class FingerprintNumberTests
{
    [Theory]
    [InlineData("0000000000000000", "0", "0")]
    [InlineData("8000000000000000", "0", "0")]
    [InlineData("3ff0002000000000", "1.00003051757813", "1.000030517578125")]
    [InlineData("bff0002000000000", "-1.00003051757813", "-1.000030517578125")]
    [InlineData("3ff0000800000000", "1.00000762939453", "1.0000076293945313")]
    [InlineData("430c6bf52633fffc", "1E+15", "999999999999999.5")]
    [InlineData("3ee4f8b588e368f1", "1E-05", "1E-05")]
    [InlineData("0000000000000001", "4.94065645841247E-324", "4.94065645841247E-324")]
    [InlineData("7fefffffffffffff", "1.79769313486232E+308", "1.7976931348623157E+308")]
    public void LegacyFormattingMatchesCapturedMonoGeneralAndRoundTripStrings(string bits, string general, string roundTrip)
    {
        var value = BitConverter.Int64BitsToDouble(Convert.ToInt64(bits, 16));
        Assert.Equal(general, FingerprintNumber.Of(value, FingerprintFormat.LegacyMono));
        Assert.Equal(roundTrip, FingerprintNumber.Of(value, FingerprintFormat.LegacyMono, true));
    }
}
