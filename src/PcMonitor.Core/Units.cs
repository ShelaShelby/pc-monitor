namespace PcMonitor.Core;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

public enum MetricUnit
{
    Percent,
    Celsius,
    MegaHertz,
    Watt,
    Rpm,
    /// <summary>Raw bytes.</summary>
    Bytes,
    BytesPerSecond,
    Hours,
    Volt,
    Count,
    Text,
}

public static class UnitConversion
{
    public static double CelsiusToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

    public static double FahrenheitToCelsius(double fahrenheit) => (fahrenheit - 32.0) * 5.0 / 9.0;

    public static double ToDisplayTemperature(double celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? CelsiusToFahrenheit(celsius) : celsius;

    public const double BytesPerKiB = 1024.0;
    public const double BytesPerMiB = 1024.0 * 1024.0;
    public const double BytesPerGiB = 1024.0 * 1024.0 * 1024.0;
}
