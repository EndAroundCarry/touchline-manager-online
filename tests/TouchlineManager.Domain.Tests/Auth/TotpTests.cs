using System.Text;
using FluentAssertions;
using TouchlineManager.Domain.Auth;

namespace TouchlineManager.Domain.Tests.Auth;

/// <summary>
/// The time-based one-time password, pinned against the RFC 6238 Appendix B vectors (ADR-0042).
/// </summary>
public sealed class TotpTests
{
    // The RFC 6238 seed for its SHA-1 vectors, as ASCII.
    private static readonly byte[] RfcSeed = Encoding.ASCII.GetBytes("12345678901234567890");

    // The RFC's table is eight digits; a six-digit code is its low six digits, because 10^6 divides 10^8.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void The_rfc_6238_vectors_match(long unixTime, string expected)
    {
        var step = unixTime / Totp.StepSeconds;

        Totp.Compute(RfcSeed, step).Should().Be(expected);
    }

    [Fact]
    public void A_code_is_accepted_within_one_step_of_its_time()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var code = Totp.Compute(RfcSeed, Totp.CurrentStep(now));

        Totp.Verify(RfcSeed, code, now).Should().BeTrue();
        Totp.Verify(RfcSeed, code, now.AddSeconds(20)).Should().BeTrue("the next step is still in the window");
        Totp.Verify(RfcSeed, code, now.AddSeconds(-20)).Should().BeTrue("the previous step is still in the window");
        Totp.Verify(RfcSeed, code, now.AddSeconds(90)).Should().BeFalse("three steps away is outside the window");
    }

    [Fact]
    public void A_code_from_another_secret_is_refused()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var other = Encoding.ASCII.GetBytes("09876543210987654321");
        var code = Totp.Compute(other, Totp.CurrentStep(now));

        Totp.Verify(RfcSeed, code, now).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void A_malformed_code_is_refused(string code)
    {
        var now = DateTimeOffset.UnixEpoch;

        Totp.Verify(RfcSeed, code, now).Should().BeFalse();
    }

    [Fact]
    public void The_window_can_be_widened_or_narrowed()
    {
        var now = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var nextStepCode = Totp.Compute(RfcSeed, Totp.CurrentStep(now) + 1);

        Totp.Verify(RfcSeed, nextStepCode, now, windowSteps: 0).Should().BeFalse();
        Totp.Verify(RfcSeed, nextStepCode, now, windowSteps: 1).Should().BeTrue();
    }

    [Fact]
    public void The_provisioning_uri_carries_the_secret_and_issuer()
    {
        var uri = Totp.BuildOtpAuthUri("Touchline Manager", "ops@example.com", RfcSeed);

        uri.Should().StartWith("otpauth://totp/");
        uri.Should().Contain("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
        uri.Should().Contain("issuer=Touchline%20Manager");
        uri.Should().Contain("digits=6");
        uri.Should().Contain("period=30");
    }
}

/// <summary>The base32 encoding RFC 4648 defines and the authenticator URI relies on.</summary>
public sealed class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void The_rfc_4648_vectors_match(string input, string expected)
    {
        Base32.Encode(Encoding.ASCII.GetBytes(input)).Should().Be(expected);
    }

    [Theory]
    [InlineData("MY", "f")]
    [InlineData("MZXW6", "foo")]
    [InlineData("MZXW6YTBOI", "foobar")]
    public void Decoding_reverses_encoding(string encoded, string expected)
    {
        Encoding.ASCII.GetString(Base32.Decode(encoded)).Should().Be(expected);
    }

    [Fact]
    public void A_secret_round_trips_through_base32()
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");

        Base32.Decode(Base32.Encode(secret)).Should().Equal(secret);
    }

    [Fact]
    public void A_non_base32_character_is_refused()
    {
        var act = () => Base32.Decode("MZXW1");

        act.Should().Throw<ArgumentException>();
    }
}
