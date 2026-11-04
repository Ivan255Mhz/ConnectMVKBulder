using Mvk.MVK01;
using Mvk.MVK01.Polling;

namespace Mvk.Sample.Polling;

/// <summary>
/// Минимальный пример: опрос СКЗ виброускорения в полосе 10…1000 Гц
/// в цикле каждые 500 мс с выводом значения.
/// <code>
/// dotnet run --project samples\Mvk.Sample.Polling
/// dotnet run --project samples\Mvk.Sample.Polling -- 10.20.0.211 10 1
/// </code>
/// Аргументы: [host] [секунды] [канал].
/// По умолчанию: 10.20.0.211, опрос до Ctrl+C, канал 1.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private static async Task Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "10.20.0.211";
        TimeSpan duration = args.Length > 1 && int.TryParse(args[1], out int seconds)
            ? TimeSpan.FromSeconds(seconds)
            : Timeout.InfiniteTimeSpan;
        int channel = args.Length > 2 && int.TryParse(args[2], out int parsed) ? parsed : 1;

        if (channel < 1 || channel > Mvk01RegisterMap.MaxChannels)
        {
            Console.WriteLine($"Номер канала должен быть в диапазоне 1..{Mvk01RegisterMap.MaxChannels}.");
            return;
        }

        Console.WriteLine($"Подключение к МВК01 {host}…");
        await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync(host);

        Console.WriteLine(
            $"Подключено: {mvk.Device.Device.Settings.Endpoint}. "
            + $"Опрос СКЗ 10…1000 Гц (канал {channel}) каждые {PollInterval.TotalMilliseconds:F0} мс. Ctrl+C — остановка.");
        Console.WriteLine();

        using CancellationTokenSource cts = new(duration);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await foreach (Mvk01Sample sample in mvk.WatchAsync(
                Mvk01MeasurementNumber.Rms10To1000Acceleration,
                channel,
                PollInterval,
                cts.Token))
            {
                if (sample.Error is { } error)
                {
                    // Ошибка связи не завершает наблюдение: реконнект выполняется автоматически.
                    Console.WriteLine($"{sample.Timestamp.ToLocalTime():HH:mm:ss.fff}  ошибка: {error.ErrorKind} — {error.Message}");
                    continue;
                }

                float value = sample.ChannelValue ?? float.NaN;
                Console.WriteLine($"{sample.Timestamp.ToLocalTime():HH:mm:ss.fff}  канал {channel}: {value:F4} м/с2");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Опрос остановлен.");
        }
    }
}
