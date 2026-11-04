using Mvk.MVK01.Setpoints;

string devicesPath = args.Length > 0 ? args[0] : "mvkmodbus.json";
string setpointsPath = args.Length > 1 ? args[1] : "mvk-setpoints.json";

Console.WriteLine($"Устройства: {devicesPath}");
Console.WriteLine($"Уставки: {setpointsPath}");

Mvk01AlarmOptions options = new()
{
    OnReactionError = (exception, alarm) =>
        Console.Error.WriteLine($"Ошибка реакции {alarm.Setpoint.Id}: {exception.Message}")
};

await using Mvk01AlarmService alarms = await Mvk01AlarmService.StartAsync(
    devicesPath,
    setpointsPath,
    options);

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Наблюдение за уставками. Ctrl+C — остановка.");
Console.WriteLine();

try
{
    await alarms.RunAsync((alarm, _) =>
    {
        Console.WriteLine(
            $"{alarm.Timestamp.ToLocalTime():HH:mm:ss.fff}  {alarm.DeviceId}  "
            + $"{alarm.Setpoint.Name ?? alarm.Setpoint.Id}: "
            + $"{alarm.PreviousState} -> {alarm.State} = {alarm.Value:F3}");

        return ValueTask.CompletedTask;
    }, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Наблюдение остановлено.");
}
