using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mvk.MVK01;
using Mvk.MVK01.DependencyInjection;
using Mvk.Modbus.DependencyInjection;

namespace Mvk.Sample.Console;

/// <summary>
/// Пример использования библиотеки: подключение к МВК01 из конфигурации,
/// свой middleware, чтение сведений об устройстве, канала, спектра и тахометра.
/// </summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "mvkmodbus.json"),
            optional: false,
            reloadOnChange: false);

        builder.Services.AddMvkModbus(builder.Configuration);
        builder.Services.AddMvk01Devices();
        builder.Services.AddMvkModbusHostedService();
        builder.Services.AddModbusMiddleware<AuditMiddleware>("audit");

        using IHost host = builder.Build();
        await host.StartAsync();

        IMvk01DeviceFactory factory = host.Services.GetRequiredService<IMvk01DeviceFactory>();
        IMvk01Device device = factory.Devices.First();

        System.Console.WriteLine();
        System.Console.WriteLine($"Устройство: {device.Id}, адрес {device.Settings.Endpoint}");

        Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();
        System.Console.WriteLine($"Серийный номер: {info.SerialNumber}, ПО: {info.FirmwareVersion}, наработка: {info.Uptime}");
        System.Console.WriteLine($"Ошибки: 0x{info.Errors.Raw:X8} (чисто: {info.Errors.IsClean})");

        Mvk01Channel channel = await device.ReadChannelAsync(1);
        System.Console.WriteLine();
        System.Console.WriteLine($"Канал 1: напряжение {channel.SensorVoltage:F4} В, пик-фактор {channel.PeakFactor:F3}, "
            + $"СКЗ X…25кГц {channel.RmsX25kHzAcceleration:F3} м/с2, DC {channel.DcComponent:F4}");

        System.Console.WriteLine("  СКЗ по полосам:");
        foreach (Mvk01RmsSet set in channel.RmsSets)
        {
            System.Console.WriteLine($"    {set.Name,-12} уск {set.Acceleration,8:F4}  скорость {set.Velocity,8:F4}  перемещение {set.Displacement,8:F4}");
        }

        System.Console.WriteLine("  Топ-5 полос спектра:");
        foreach ((int band, float value) in channel.Spectrum
            .Select((value, index) => (Band: index + 1, Value: value))
            .OrderByDescending(item => item.Value)
            .Take(5))
        {
            System.Console.WriteLine($"    {Mvk01SpectrumBands.FormatBand(band),-10} {value,10:F4}");
        }

        Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1);
        System.Console.WriteLine();
        System.Console.WriteLine($"Тахометр 1: {tacho.FrequencyHz:F3} Гц, направление {tacho.Direction}, переходов {tacho.TransitionCount}");

        System.Console.WriteLine();
        System.Console.WriteLine($"Состояние: {device.State}, соединение: {device.Device.IsConnected}");

        await host.StopAsync();
    }
}
