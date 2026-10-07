using FluentAssertions;
using TouchlineManager.Application.World.Validation;
using TouchlineManager.Contracts.World;

namespace TouchlineManager.Application.Tests.World;

/// <summary>
/// The request that chooses a club's colours. The colour picker produces <c>#rrggbb</c>, so that is the only
/// form the server accepts, and the two colours must be different or the second one means nothing.
/// </summary>
public sealed class ChangeClubColoursRequestValidatorTests
{
    private readonly ChangeClubColoursRequestValidator _validator = new();

    [Fact]
    public void Two_different_hex_colours_are_accepted()
    {
        var result = _validator.Validate(Request("#1f4e79", "#d6e4f0"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Upper_case_digits_are_accepted_because_the_domain_stores_them_lower_case()
    {
        _validator.Validate(Request("#1F4E79", "#D6E4F0")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "#d6e4f0")]
    [InlineData("#fff", "#d6e4f0")]
    [InlineData("1f4e79", "#d6e4f0")]
    [InlineData("#1f4e7g", "#d6e4f0")]
    [InlineData("red", "#d6e4f0")]
    [InlineData("#1f4e79", "")]
    [InlineData("#1f4e79", "#12345")]
    public void A_colour_that_is_not_six_hex_digits_names_the_field_that_is_wrong(string primary, string secondary)
    {
        var result = _validator.Validate(Request(primary, secondary));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().Contain(
            primary == "#1f4e79"
                ? nameof(ChangeClubColoursRequest.SecondaryColour)
                : nameof(ChangeClubColoursRequest.PrimaryColour));
    }

    [Theory]
    [InlineData("#1f4e79", "#1f4e79")]
    [InlineData("#1f4e79", "#1F4E79")]
    public void The_same_colour_twice_is_refused_on_the_second_colour(string primary, string secondary)
    {
        var result = _validator.Validate(Request(primary, secondary));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(ChangeClubColoursRequest.SecondaryColour));
    }

    private static ChangeClubColoursRequest Request(string primary, string secondary) =>
        new() { PrimaryColour = primary, SecondaryColour = secondary };
}
