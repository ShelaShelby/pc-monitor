using PcMonitor.Core;

namespace PcMonitor.Core.Tests;

public class UnitConversionTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(89, 192.2)]
    public void CelsiusToFahrenheit(double c, double f) =>
        Assert.Equal(f, UnitConversion.CelsiusToFahrenheit(c), 9);

    [Theory]
    [InlineData(32, 0)]
    [InlineData(212, 100)]
    public void FahrenheitToCelsius(double f, double c) =>
        Assert.Equal(c, UnitConversion.FahrenheitToCelsius(f), 9);

    [Fact]
    public void RoundTrip()
    {
        for (double c = -50; c <= 150; c += 7.3)
            Assert.Equal(c, UnitConversion.FahrenheitToCelsius(UnitConversion.CelsiusToFahrenheit(c)), 9);
    }

    [Fact]
    public void DisplayTemperatureHonoursUnit()
    {
        Assert.Equal(50, UnitConversion.ToDisplayTemperature(50, TemperatureUnit.Celsius));
        Assert.Equal(122, UnitConversion.ToDisplayTemperature(50, TemperatureUnit.Fahrenheit), 9);
    }
}
