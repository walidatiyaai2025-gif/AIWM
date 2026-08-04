using AIWordPressManager.Domain.Entities;
using AIWordPressManager.Domain.Enums;
using FluentAssertions;

namespace AIWordPressManager.UnitTests.Domain;

public sealed class SiteTests
{
    [Fact]
    public void Constructor_ShouldNormalizeSiteUrl()
    {
        DateTime now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

        Site site = new("Example", new Uri("https://example.com/path"), now);

        site.Name.Should().Be("Example");
        site.SiteUrl.Should().Be("https://example.com");
        site.ConnectionStatus.Should().Be(SiteConnectionStatus.Unknown);
    }

    [Fact]
    public void RecordConnectionStatus_ShouldUpdateTimestamp()
    {
        DateTime created = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
        DateTime tested = created.AddMinutes(5);
        Site site = new("Example", new Uri("https://example.com"), created);

        site.RecordConnectionStatus(SiteConnectionStatus.Connected, tested);

        site.ConnectionStatus.Should().Be(SiteConnectionStatus.Connected);
        site.LastConnectionTestAtUtc.Should().Be(tested);
    }
}
